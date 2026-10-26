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
        public string Id = "";
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
        /// <summary>true = 不置顶（用户主动取消钉住）</summary>
        public bool Unpinned { get; set; }
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
    /// 桌面「下一项」小窗：无边框、可拖动、可钉住置顶、可隐藏。
    /// 位置、开关与置顶状态记在 widget.json。
    /// </summary>
    internal sealed class NextUpForm : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WM_NCLBUTTONDOWN = 0x00A2;
        private static readonly IntPtr HTCAPTION = new IntPtr(2);

        private readonly float _scale;
        private readonly string _family;
        private readonly Font _fontBrand;
        private readonly Font _fontCaption;
        private readonly Font _fontTime;
        private readonly Font _fontTitle;
        private readonly Font _fontBody;
        private readonly Font _fontEmpty;
        private readonly Font _fontTiny;

        private static readonly Color Accent = Color.FromArgb(0x5B, 0x7C, 0xFF);
        private static readonly Color Accent2 = Color.FromArgb(0x8A, 0x5C, 0xFF);
        private static readonly Color[] RowAccents =
        {
            Color.FromArgb(0x5B, 0x7C, 0xFF),
            Color.FromArgb(0x8A, 0x5C, 0xFF),
            Color.FromArgb(0x16, 0xA3, 0x4A)
        };

        private readonly Timer _saveTimer;
        private bool _allowClose;
        private bool _open;
        private bool _unpinned;
        private bool _bgDark;
        private List<WidgetItem> _items = new List<WidgetItem>();

        private Rectangle _pinRect;
        private Rectangle _hideRect;
        private readonly List<Rectangle> _doneRects = new List<Rectangle>();
        private readonly List<Rectangle> _snoozeRects = new List<Rectangle>();

        /// <summary>点小窗里的「完成」：交给主体页面统一改状态</summary>
        public event Action<WidgetItem> ItemDone;
        /// <summary>点小窗里的「+10」：交给主体页面延后提醒</summary>
        public event Action<WidgetItem> ItemSnooze;

        public NextUpForm(bool dark)
        {
            _bgDark = dark;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;
            _family = PickFamily();

            _fontBrand = MakeFont(9f, FontStyle.Bold);
            _fontCaption = MakeFont(7.5f, FontStyle.Regular);
            _fontTime = MakeFont(9f, FontStyle.Bold);
            _fontTitle = MakeFont(9.5f, FontStyle.Regular);
            _fontBody = MakeFont(7.5f, FontStyle.Regular);
            _fontEmpty = MakeFont(9f, FontStyle.Regular);
            _fontTiny = MakeFont(7f, FontStyle.Regular);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
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

            WidgetState st = WidgetStore.Load();
            _unpinned = st.Unpinned;
            TopMost = !_unpinned;
            RestorePlacement(st);
            UpdateLayout();
        }

        public bool WidgetOpen { get { return _open; } }

        public bool Pinned { get { return !_unpinned; } }

        public void SetPinned(bool pinned)
        {
            _unpinned = !pinned;
            TopMost = pinned;
            if (pinned)
            {
                try { Activate(); BringToFront(); } catch (Exception) { /* ignore */ }
            }
            Persist();
            Invalidate();
        }

        public void ApplyTheme(bool dark)
        {
            if (dark == _bgDark) return;
            _bgDark = dark;
            BackColor = dark ? Color.FromArgb(0x17, 0x1B, 0x24) : Color.FromArgb(0xFF, 0xFF, 0xFF);
            Invalidate();
        }

        public void SetItems(List<WidgetItem> items)
        {
            _items = items ?? new List<WidgetItem>();
            if (_items.Count > 3) _items = _items.GetRange(0, 3);
            UpdateLayout();
            Invalidate();
        }

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
                        Id = Str(d, "id"),
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

        private int Px(float v) { return (int)Math.Round(v * _scale); }

        private void UpdateLayout()
        {
            int w = Px(292);
            int count = _items.Count > 0 ? _items.Count : 1;
            int h = Px(50) + count * Px(46) + Px(10);
            if (ClientSize.Width != w || ClientSize.Height != h) ClientSize = new Size(w, h);
            ApplyRegion();
            KeepOnScreen();
        }

        private void ApplyRegion()
        {
            try
            {
                if (Width <= 0 || Height <= 0) return;
                using (GraphicsPath path = RoundPath(new Rectangle(0, 0, Width, Height), Px(14)))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            catch (Exception) { /* ignore */ }
        }

        private static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (d >= r.Width || d >= r.Height) { path.AddRectangle(r); return path; }
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
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_bgDark ? Color.FromArgb(0x14, 0x18, 0x24) : Color.FromArgb(0xF7, 0xF9, 0xFE));

            Color c1 = _bgDark ? Color.FromArgb(0x20, 0x26, 0x38) : Color.FromArgb(0xFF, 0xFF, 0xFF);
            Color c2 = _bgDark ? Color.FromArgb(0x15, 0x19, 0x24) : Color.FromArgb(0xF0, 0xF4, 0xFC);
            using (var grad = new LinearGradientBrush(ClientRectangle, c1, c2, 65f))
                g.FillRectangle(grad, ClientRectangle);

            Color border = _bgDark ? Color.FromArgb(0x34, 0x3C, 0x50) : Color.FromArgb(0xD9, 0xE1, 0xEF);
            using (GraphicsPath path = RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Px(14)))
            using (var pen = new Pen(border)) g.DrawPath(pen, path);

            Color text = _bgDark ? Color.FromArgb(0xEC, 0xEF, 0xF7) : Color.FromArgb(0x16, 0x1B, 0x26);
            Color faint = _bgDark ? Color.FromArgb(0x93, 0x9D, 0xB2) : Color.FromArgb(0x7A, 0x84, 0x98);

            /* 头部：品牌点 + 标题 + 置顶 / 隐藏按钮 */
            using (var b = new LinearGradientBrush(new Rectangle(Px(14), Px(12), Px(22), Px(22)), Accent, Accent2, 45f))
                g.FillEllipse(b, new Rectangle(Px(14), Px(12), Px(22), Px(22)));
            TextRenderer.DrawText(g, "叶", _fontBrand,
                new Rectangle(Px(14), Px(12), Px(22), Px(22)), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(g, "下一项", _fontBrand,
                new Rectangle(Px(44), Px(12), Px(120), Px(22)), text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            _pinRect = new Rectangle(Width - Px(68), Px(10), Px(26), Px(26));
            _hideRect = new Rectangle(Width - Px(36), Px(10), Px(26), Px(26));
            DrawHeadButton(g, _pinRect, !_unpinned, faint);
            DrawHeadButton(g, _hideRect, false, faint);

            using (var pen = new Pen(border)) g.DrawLine(pen, Px(14), Px(42), Width - Px(14), Px(42));

            if (_items.Count == 0)
            {
                TextRenderer.DrawText(g, "最近没有安排", _fontEmpty,
                    new Rectangle(Px(16), Px(56), Width - Px(32), Px(24)), text,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "把要做的事记下来，叶历会提醒你", _fontBody,
                    new Rectangle(Px(16), Px(78), Width - Px(32), Px(18)), faint,
                    TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                return;
            }

            _doneRects.Clear();
            _snoozeRects.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                WidgetItem it = _items[i];
                int top = Px(50) + i * Px(46);
                Color ac = RowAccents[i % RowAccents.Length];

                Rectangle doneR = new Rectangle(Width - Px(66), top + Px(10), Px(24), Px(24));
                Rectangle snoozeR = new Rectangle(Width - Px(38), top + Px(10), Px(26), Px(24));
                _doneRects.Add(doneR);
                _snoozeRects.Add(snoozeR);
                using (var bg = new SolidBrush(_bgDark ? Color.FromArgb(0x25, 0x2C, 0x3B) : Color.FromArgb(0xEE, 0xF3, 0xFB)))
                {
                    g.FillEllipse(bg, doneR);
                    g.FillEllipse(bg, snoozeR);
                }
                TextRenderer.DrawText(g, "✓", _fontTiny, doneR,
                    Color.FromArgb(0x16, 0xA3, 0x4A),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "+10", _fontTiny, snoozeR,
                    Color.FromArgb(0x8A, 0x5C, 0xFF),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                using (var bar = new SolidBrush(ac))
                    g.FillRectangle(bar, new Rectangle(Px(14), top + Px(9), Px(3), Px(28)));

                string time = string.IsNullOrEmpty(it.Time) ? "--:--" : it.Time;
                using (var pill = new SolidBrush(_bgDark ? Color.FromArgb(40, ac) : Color.FromArgb(24, ac)))
                    g.FillRectangle(pill, new Rectangle(Px(25), top + Px(11), Px(52), Px(24)));
                TextRenderer.DrawText(g, time, _fontTime,
                    new Rectangle(Px(25), top + Px(11), Px(52), Px(24)), ac,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                string title = it.Title.Length > 0 ? it.Title : it.Body;
                TextRenderer.DrawText(g, title, _fontTitle,
                    new Rectangle(Px(86), top + Px(7), Width - Px(86) - Px(72), Px(22)), text,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

                if (!string.IsNullOrEmpty(it.Body) && it.Body != it.Title)
                {
                    TextRenderer.DrawText(g, it.Body, _fontBody,
                        new Rectangle(Px(86), top + Px(27), Width - Px(86) - Px(72), Px(16)), faint,
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }

                if (i < _items.Count - 1)
                {
                    using (var pen = new Pen(_bgDark ? Color.FromArgb(0x26, 0x2C, 0x3A) : Color.FromArgb(0xEC, 0xF0, 0xF7)))
                        g.DrawLine(pen, Px(14), top + Px(45), Width - Px(14), top + Px(45));
                }
            }
        }

        private void DrawHeadButton(Graphics g, Rectangle r, bool active, Color faint)
        {
            Color fill = active ? Accent : (_bgDark ? Color.FromArgb(0x27, 0x2D, 0x3B) : Color.FromArgb(0xEE, 0xF2, 0xF9));
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, r);
            if (r == _pinRect)
            {
                using (var pen = new Pen(active ? Color.White : faint, Px(1.2f)))
                {
                    int cx = r.Left + r.Width / 2;
                    int cy = r.Top + r.Height / 2;
                    g.DrawLine(pen, cx - Px(4), cy + Px(4), cx + Px(4), cy - Px(4));
                    g.DrawLine(pen, cx + Px(1), cy - Px(5), cx + Px(6), cy);
                    g.DrawLine(pen, cx - Px(6), cy, cx - Px(1), cy + Px(5));
                }
            }
            else
            {
                using (var pen = new Pen(faint, Px(1.4f)))
                {
                    g.DrawLine(pen, r.Left + Px(9), r.Top + Px(9), r.Right - Px(9), r.Bottom - Px(9));
                    g.DrawLine(pen, r.Right - Px(9), r.Top + Px(9), r.Left + Px(9), r.Bottom - Px(9));
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (_pinRect.Contains(e.Location))
            {
                SetPinned(_unpinned);
                return;
            }
            if (_hideRect.Contains(e.Location))
            {
                HideWidget();
                return;
            }
            for (int i = 0; i < _doneRects.Count && i < _items.Count; i++)
            {
                if (_doneRects[i].Contains(e.Location))
                {
                    Action<WidgetItem> h = ItemDone;
                    if (h != null) h(_items[i]);
                    return;
                }
                if (_snoozeRects[i].Contains(e.Location))
                {
                    Action<WidgetItem> h = ItemSnooze;
                    if (h != null) h(_items[i]);
                    return;
                }
            }
            try
            {
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

        private void RestorePlacement(WidgetState st)
        {
            _open = st.Open;
            if (st.Placed)
            {
                var rect = new Rectangle(st.X, st.Y, Px(292), Px(50) + Px(46) + Px(10));
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
                int w = Px(292);
                int h = Px(50) + Px(46) + Px(10);
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
                    if (s.WorkingArea.IntersectsWith(rect)) return true;
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
                    Open = _open,
                    Unpinned = _unpinned
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
                foreach (Font f in new[] { _fontBrand, _fontCaption, _fontTime, _fontTitle, _fontBody, _fontEmpty, _fontTiny })
                    try { f.Dispose(); } catch (Exception) { }
            }
            base.Dispose(disposing);
        }

        private Font MakeFont(float pt, FontStyle style)
        {
            try { return new Font(_family, pt * _scale, style, GraphicsUnit.Point); }
            catch (Exception) { return new Font(FontFamily.GenericSansSerif, pt * _scale, style, GraphicsUnit.Point); }
        }

        private static string PickFamily()
        {
            string[] want = { "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Segoe UI" };
            foreach (string f in want)
            {
                try
                {
                    foreach (FontFamily ff in FontFamily.Families)
                        if (string.Equals(ff.Name, f, StringComparison.OrdinalIgnoreCase)) return f;
                }
                catch (Exception) { /* ignore */ }
            }
            return "Segoe UI";
        }
    }
}