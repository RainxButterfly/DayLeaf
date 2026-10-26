using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DayLeaf
{
    /// <summary>托盘 / 桌面小组件共用的一条「下一项」。</summary>
    internal sealed class WidgetItem
    {
        public string Title = "";
        public string Time = "";
        public string Body = "";
    }

    /// <summary>widget.json 里存的位置与开关状态。</summary>
    internal sealed class WidgetState
    {
        public int X { get; set; }
        public int Y { get; set; }
        public bool Placed { get; set; }
        public bool Open { get; set; }
    }

    internal static class WidgetStore
    {
        public static WidgetState Load()
        {
            try
            {
                if (!File.Exists(AppPaths.WidgetFile)) return new WidgetState();
                string json = File.ReadAllText(AppPaths.WidgetFile);
                if (string.IsNullOrEmpty(json)) return new WidgetState();
                WidgetState st = new JavaScriptSerializer().Deserialize<WidgetState>(json);
                return st ?? new WidgetState();
            }
            catch (Exception)
            {
                return new WidgetState();
            }
        }

        public static void Save(WidgetState st)
        {
            if (st == null) return;
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.WriteAllText(AppPaths.WidgetFile, new JavaScriptSerializer().Serialize(st), new UTF8Encoding(false));
            }
            catch (Exception) { /* 存不下不影响显示 */ }
        }
    }

    /// <summary>
    /// 桌面「下一项」小窗：无边框、置顶、不进任务栏、可拖动，
    /// 关掉只是隐藏（不 Dispose），位置与开关记在 widget.json。
    /// </summary>
    internal sealed class NextUpForm : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly float _scale;
        private readonly string _family;
        private readonly Font _fontHeader;
        private readonly Font _fontCaption;
        private readonly Font _fontTime;
        private readonly Font _fontTitle;
        private readonly Font _fontEmpty;

        private static readonly Color Accent = Color.FromArgb(0x5B, 0x7C, 0xFF);

        private readonly Timer _saveTimer;
        private bool _allowClose;
        private bool _open;
        private List<WidgetItem> _items = new List<WidgetItem>();

        public NextUpForm(bool dark)
        {
            _bgDark = dark;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;
            _family = PickFamily();

            _fontHeader = MakeFont(8.5f, FontStyle.Bold);
            _fontCaption = MakeFont(7.5f, FontStyle.Regular);
            _fontTime = MakeFont(9f, FontStyle.Bold);
            _fontTitle = MakeFont(9f, FontStyle.Regular);
            _fontEmpty = MakeFont(8.5f, FontStyle.Regular);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            KeyPreview = false;
            DoubleBuffered = true;
            Text = "叶历 · 下一项";
            BackColor = dark ? Color.FromArgb(0x17, 0x1B, 0x24) : Color.FromArgb(0xFF, 0xFF, 0xFF);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            _saveTimer = new Timer();
            _saveTimer.Interval = 500;
            _saveTimer.Tick += delegate { _saveTimer.Stop(); Persist(); };

            RestorePlacement();
            UpdateLayout();
        }

        /* ---------------- 对外接口 ---------------- */

        public bool WidgetOpen { get { return _open; } }

        public void ApplyTheme(bool dark)
        {
            if (dark == _bgDark) return;
            _bgDark = dark;
            BackColor = dark ? Color.FromArgb(0x17, 0x1B, 0x24) : Color.FromArgb(0xFF, 0xFF, 0xFF);
            Invalidate();
        }

        private bool _bgDark;

        public void SetItems(List<WidgetItem> items)
        {
            _items = items ?? new List<WidgetItem>();
            if (_items.Count > 3) _items = _items.GetRange(0, 3);
            UpdateLayout();
            Invalidate();
        }

        /// <summary>显示但不抢焦点；已经显示时只刷新内容。</summary>
        public void ShowWidget()
        {
            if (IsDisposed) return;
            _open = true;
            try
            {
                if (!Visible) Show();
                else Invalidate();
            }
            catch (Exception) { /* ignore */ }
            Persist();
        }

        public void HideWidget()
        {
            _open = false;
            try { if (Visible) Hide(); } catch (Exception) { }
            Persist();
        }

        /// <summary>退出程序时真正销毁用。</summary>
        public void CloseForExit()
        {
            _allowClose = true;
            try { _saveTimer.Stop(); } catch (Exception) { }
            try { if (!IsDisposed) Close(); } catch (Exception) { }
        }

        public static List<WidgetItem> ParseItems(string json)
        {
            var list = new List<WidgetItem>();
            if (string.IsNullOrEmpty(json)) return list;

            try
            {
                Dictionary<string, object> dict =
                    new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (dict == null) return list;

                object arrObj;
                if (!dict.TryGetValue("items", out arrObj)) return list;
                var arr = arrObj as object[];
                if (arr == null) return list;

                foreach (object o in arr)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    var it = new WidgetItem
                    {
                        Title = Str(d, "title"),
                        Time = Str(d, "time"),
                        Body = Str(d, "body")
                    };
                    if (it.Title.Length == 0 && it.Time.Length == 0) continue;
                    list.Add(it);
                    if (list.Count >= 3) break;
                }
            }
            catch (Exception) { /* 网页传来的结构不对就当空 */ }
            return list;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                string s = Convert.ToString(v);
                if (s != null) return s.Trim();
            }
            return "";
        }

        /* ---------------- 布局与绘制 ---------------- */

        private int Px(float v) { return (int)Math.Round(v * _scale); }

        private void UpdateLayout()
        {
            int w = Px(252);
            int count = _items.Count > 0 ? _items.Count : 1;
            int h = Px(34) + count * Px(30) + Px(10);
            if (ClientSize.Width != w || ClientSize.Height != h)
            {
                ClientSize = new Size(w, h);
            }
            ApplyRegion();
            KeepOnScreen();
        }

        private void ApplyRegion()
        {
            try
            {
                if (Width <= 0 || Height <= 0) return;
                using (GraphicsPath path = RoundPath(new Rectangle(0, 0, Width, Height), Px(10)))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            catch (Exception) { /* 用方角也能看 */ }
        }

        private static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (d >= r.Width || d >= r.Height)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Color surface = _bgDark ? Color.FromArgb(0x17, 0x1B, 0x24) : Color.FromArgb(0xFF, 0xFF, 0xFF);
            Color border = _bgDark ? Color.FromArgb(0x2A, 0x31, 0x40) : Color.FromArgb(0xDF, 0xE4, 0xED);
            Color text = _bgDark ? Color.FromArgb(0xE9, 0xED, 0xF5) : Color.FromArgb(0x15, 0x1A, 0x24);
            Color faint = _bgDark ? Color.FromArgb(0x9A, 0xA4, 0xB4) : Color.FromArgb(0x8A, 0x94, 0xA6);

            using (var b = new SolidBrush(surface)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Px(10)))
            using (var pen = new Pen(border)) g.DrawPath(pen, path);

            var flagsLeft = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                            | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis
                            | TextFormatFlags.SingleLine;

            TextRenderer.DrawText(g, "叶历", _fontHeader,
                new Rectangle(Px(14), Px(8), Px(80), Px(20)), Accent, flagsLeft);
            TextRenderer.DrawText(g, "下一项", _fontCaption,
                new Rectangle(Width - Px(14) - Px(80), Px(9), Px(80), Px(20)), faint,
                flagsLeft | TextFormatFlags.Right);

            using (var pen = new Pen(border)) g.DrawLine(pen, Px(14), Px(30), Width - Px(14), Px(30));

            if (_items.Count == 0)
            {
                TextRenderer.DrawText(g, "暂无安排", _fontEmpty,
                    new Rectangle(Px(14), Px(34), Width - Px(28), Px(30)), faint, flagsLeft);
                return;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                WidgetItem it = _items[i];
                int y = Px(34) + i * Px(30) + Px(3);

                TextRenderer.DrawText(g, string.IsNullOrEmpty(it.Time) ? "--:--" : it.Time,
                    _fontTime, new Rectangle(Px(14), y, Px(46), Px(24)), Accent, flagsLeft);

                string title = it.Title.Length > 0 ? it.Title : it.Body;
                TextRenderer.DrawText(g, title, _fontTitle,
                    new Rectangle(Px(64), y, Width - Px(64) - Px(14), Px(24)), text, flagsLeft);
            }
        }

        /* ---------------- 位置记忆与拖动 ---------------- */

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0x00A2;
        private static readonly IntPtr HTCAPTION = new IntPtr(2);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            try
            {
                // 交给系统的窗口拖动，鼠标位置与多显示器都能正确处理
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
            }
            catch (Exception) { /* ignore */ }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (!Visible) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void RestorePlacement()
        {
            WidgetState st = WidgetStore.Load();
            _open = st.Open;
            if (st.Placed)
            {
                var rect = new Rectangle(st.X, st.Y, ClientSize.Width, ClientSize.Height);
                if (IsOnAnyScreen(rect))
                {
                    Location = new Point(st.X, st.Y);
                    return;
                }
            }
            PlaceDefault();
        }

        private void PlaceDefault()
        {
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                int w = Px(252);
                int h = Px(34) + Px(30) + Px(10);
                Location = new Point(wa.Right - w - Px(24), wa.Bottom - h - Px(24));
            }
            catch (Exception) { /* ignore */ }
        }

        private void KeepOnScreen()
        {
            if (!IsOnAnyScreen(new Rectangle(Location.X, Location.Y, Width, Height))) PlaceDefault();
        }

        private static bool IsOnAnyScreen(Rectangle rect)
        {
            try
            {
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.WorkingArea.IntersectsWith(rect)) return true;
                }
            }
            catch (Exception) { /* ignore */ }
            return false;
        }

        private void Persist()
        {
            if (IsDisposed) return;
            try
            {
                WidgetStore.Save(new WidgetState
                {
                    X = Location.X,
                    Y = Location.Y,
                    Placed = true,
                    Open = _open
                });
            }
            catch (Exception) { /* ignore */ }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                HideWidget();
                return;
            }
            Persist();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _saveTimer.Dispose(); } catch (Exception) { }
                try { _fontHeader.Dispose(); } catch (Exception) { }
                try { _fontCaption.Dispose(); } catch (Exception) { }
                try { _fontTime.Dispose(); } catch (Exception) { }
                try { _fontTitle.Dispose(); } catch (Exception) { }
                try { _fontEmpty.Dispose(); } catch (Exception) { }
            }
            base.Dispose(disposing);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;   // 不进 Alt+Tab
                return cp;
            }
        }

        private Font MakeFont(float pt, FontStyle style)
        {
            try { return new Font(_family, pt, style, GraphicsUnit.Point); }
            catch (Exception) { return new Font(SystemFonts.MessageBoxFont.FontFamily, pt, style, GraphicsUnit.Point); }
        }

        private static string PickFamily()
        {
            string[] preferred = { "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Segoe UI" };
            try
            {
                foreach (string name in preferred)
                {
                    foreach (FontFamily ff in FontFamily.Families)
                    {
                        if (string.Equals(ff.Name, name, StringComparison.OrdinalIgnoreCase)) return name;
                    }
                }
            }
            catch (Exception) { /* ignore */ }
            return SystemFonts.MessageBoxFont.FontFamily.Name;
        }
    }
}
