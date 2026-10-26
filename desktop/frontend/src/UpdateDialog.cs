using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace DayLeaf
{
    /// <summary>
    /// 软件内更新弹窗：无边框圆角卡片、自绘按钮与进度条、多源下载回退、
    /// 大小与 SHA-256 校验。下载完成后拉起 NSIS 安装程序。
    /// </summary>
    internal sealed class UpdateDialog : Form
    {
        /* ---------------- 外观 ---------------- */

        private readonly DesktopUpdate.Info _info;
        private readonly string _current;
        private readonly bool _dark;
        private readonly float _scale;

        private readonly Color _bg;
        private readonly Color _card;
        private readonly Color _card2;
        private readonly Color _text;
        private readonly Color _sub;
        private readonly Color _faint;
        private readonly Color _border;
        private readonly Color _errorBg;
        private readonly Color _errorFg;
        private readonly Color _accent = Color.FromArgb(0x5B, 0x7C, 0xFF);
        private readonly Color _accent2 = Color.FromArgb(0x8A, 0x5C, 0xFF);

        private readonly Font _fontTitle;
        private readonly Font _fontVersion;
        private readonly Font _fontVersionStrong;
        private readonly Font _fontSection;
        private readonly Font _fontBody;
        private readonly Font _fontMeta;
        private readonly Font _fontStatus;
        private readonly Font _fontButton;
        private readonly Font _fontButtonSmall;
        private readonly Font _fontErrorTitle;
        private readonly Font _fontErrorDetail;
        private readonly Font _fontClose6;
        private readonly string _family;
        private Image _appIcon;

        /* ---------------- 控件 ---------------- */

        private readonly NotesCard _notes;
        private readonly ProgressEx _progress;
        private readonly Label _meta;
        private readonly Label _status;
        private readonly ErrorCard _error;
        private readonly PillButton _primary;
        private readonly PillButton _secondary;
        private readonly PillButton _close;
        private readonly Label _versionOld;
        private readonly Label _versionArrow;
        private readonly Label _versionNew;
        private readonly Label _title;

        /* ---------------- 下载 ---------------- */

        private readonly List<string> _sources = new List<string>();
        private TimedWebClient _client;
        private int _sourceIndex;
        private string _partPath;
        private string _finalPath;
        private string _lastError = "";
        private bool _downloading;
        private bool _closing;
        private Timer _retryTimer;
        private Timer _doneTimer;

        public UpdateDialog(DesktopUpdate.Info info, string current, bool dark)
        {
            _info = info;
            _current = current;
            _dark = dark;

            _bg = dark ? Color.FromArgb(0x0E, 0x11, 0x18) : Color.FromArgb(0xF4, 0xF6, 0xFB);
            _card = dark ? Color.FromArgb(0x17, 0x1B, 0x24) : Color.White;
            _card2 = dark ? Color.FromArgb(0x20, 0x26, 0x32) : Color.FromArgb(0xEC, 0xF0, 0xF7);
            _text = dark ? Color.FromArgb(0xE9, 0xED, 0xF5) : Color.FromArgb(0x15, 0x1A, 0x24);
            _sub = dark ? Color.FromArgb(0x9A, 0xA4, 0xB4) : Color.FromArgb(0x5F, 0x68, 0x78);
            _faint = dark ? Color.FromArgb(0x66, 0x70, 0x82) : Color.FromArgb(0x8A, 0x94, 0xA6);
            _border = dark ? Color.FromArgb(0x2A, 0x31, 0x40) : Color.FromArgb(0xDF, 0xE4, 0xED);
            _errorBg = dark ? Color.FromArgb(0x2A, 0x19, 0x1E) : Color.FromArgb(0xFF, 0xEF, 0xF1);
            _errorFg = dark ? Color.FromArgb(0xFF, 0x93, 0x9C) : Color.FromArgb(0xD2, 0x3A, 0x48);

            using (var g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;

            _family = PickFamily();
            _fontTitle = MakeFont(18f, FontStyle.Bold);
            _fontVersion = MakeFont(9.5f, FontStyle.Regular);
            _fontVersionStrong = MakeFont(9.5f, FontStyle.Bold);
            _fontSection = MakeFont(9f, FontStyle.Bold);
            _fontBody = MakeFont(9.5f, FontStyle.Regular);
            _fontMeta = MakeFont(8.5f, FontStyle.Regular);
            _fontStatus = MakeFont(8.5f, FontStyle.Regular);
            _fontButton = MakeFont(10f, FontStyle.Bold);
            _fontButtonSmall = MakeFont(9.5f, FontStyle.Regular);
            _fontErrorTitle = MakeFont(9.5f, FontStyle.Bold);
            _fontErrorDetail = MakeFont(8.5f, FontStyle.Regular);
            _fontClose6 = MakeFont(12f, FontStyle.Regular);

            FormBorderStyle = FormBorderStyle.None;
            /* 所有控件尺寸已经按 DPI 用 Px() 算过，绝不能再让 WinForms 自动缩放一次，
               否则 125%/150% 下标题、版本和按钮会互相压住。 */
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            KeyPreview = true;
            BackColor = _bg;
            DoubleBuffered = true;
            ClientSize = new Size(Px(500), Px(500));
            Text = "发现新版本 · 叶历 DayLeaf";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            try
            {
                using (Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    if (ico != null) _appIcon = ico.ToBitmap();
                }
            }
            catch { /* 没图标也能画 */ }

            _title = new Label
            {
                Text = "发现新版本",
                Font = _fontTitle,
                ForeColor = _text,
                AutoSize = true,
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            _title.Location = new Point(Px(92), Px(22));
            Controls.Add(_title);

            _versionOld = new Label
            {
                Text = "当前 v" + current,
                Font = _fontVersion,
                ForeColor = _sub,
                AutoSize = true,
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            Controls.Add(_versionOld);

            _versionArrow = new Label
            {
                Text = "→",
                Font = _fontVersion,
                ForeColor = _faint,
                AutoSize = true,
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            Controls.Add(_versionArrow);

            _versionNew = new Label
            {
                Text = "最新 v" + info.VersionName,
                Font = _fontVersionStrong,
                ForeColor = _accent,
                AutoSize = true,
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            Controls.Add(_versionNew);
            LayoutVersion();

            string note = string.IsNullOrEmpty(info.Notes) ? "本次更新包含稳定性改进与细节优化。" : info.Notes;
            _notes = new NotesCard(this, "更新内容", note.Replace("\r\n", "\n").Replace('\r', '\n'));
            _notes.Bounds = new Rectangle(Px(24), Px(118), Px(452), Px(178));
            Controls.Add(_notes);

            string meta = "可直接覆盖安装，日程与记事不会丢失";
            if (info.Size > 0) meta = "约 " + HumanSize(info.Size) + "　·　" + meta;
            _meta = new Label
            {
                Text = meta,
                Font = _fontMeta,
                ForeColor = _faint,
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                UseMnemonic = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _meta.Bounds = new Rectangle(Px(26), Px(306), Px(450), Px(22));
            Controls.Add(_meta);

            _progress = new ProgressEx(this);
            _progress.Bounds = new Rectangle(Px(24), Px(344), Px(452), Px(6));
            _progress.Visible = false;
            Controls.Add(_progress);

            _status = new Label
            {
                Text = "",
                Font = _fontStatus,
                ForeColor = _sub,
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                UseMnemonic = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            _status.Bounds = new Rectangle(Px(26), Px(358), Px(450), Px(20));
            Controls.Add(_status);

            _error = new ErrorCard(this);
            _error.Bounds = new Rectangle(Px(24), Px(334), Px(452), Px(66));
            _error.Visible = false;
            Controls.Add(_error);

            _secondary = new PillButton(this, false, "稍后");
            _secondary.TabStop = false;
            _secondary.Font = _fontButtonSmall;
            _secondary.Bounds = new Rectangle(Px(212), Px(424), Px(100), Px(44));
            _secondary.Click += delegate { SecondaryClick(); };
            Controls.Add(_secondary);

            _primary = new PillButton(this, true, "立即更新");
            _primary.TabStop = false;
            _primary.Font = _fontButton;
            _primary.Bounds = new Rectangle(Px(324), Px(424), Px(152), Px(44));
            _primary.Click += delegate { PrimaryClick(); };
            Controls.Add(_primary);

            _close = new PillButton(this, false, "×");
            _close.TabStop = false;
            _close.Font = _fontClose6;
            _close.Bounds = new Rectangle(Px(444), Px(24), Px(32), Px(32));
            _close.RadiusBase = 8;
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            MouseEventHandler drag = delegate { DragWindow(); };
            MouseDown += drag;
            _title.MouseDown += drag;
            _versionOld.MouseDown += drag;
            _versionArrow.MouseDown += drag;
            _versionNew.MouseDown += drag;

            BuildSources();
            _primary.Enabled = _sources.Count > 0;
            if (_sources.Count == 0)
            {
                ShowError("没有可用的下载地址", "更新清单缺少安装包地址，请稍后重试。");
            }
        }

        /* ---------------- 基础绘制 ---------------- */

        private int Px(int v) { return (int)Math.Round(v * _scale); }

        private Font MakeFont(float pt, FontStyle style)
        {
            try { return new Font(_family, pt, style, GraphicsUnit.Point); }
            catch { return new Font(SystemFonts.MessageBoxFont.FontFamily, pt, style, GraphicsUnit.Point); }
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
            catch { /* ignore */ }
            return SystemFonts.MessageBoxFont.FontFamily.Name;
        }

        internal static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            try
            {
                using (GraphicsPath p = RoundPath(new Rectangle(0, 0, Width, Height), Px(16)))
                {
                    Region old = Region;
                    Region = new Region(p);
                    if (old != null) old.Dispose();
                }
            }
            catch { /* ignore */ }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            /* 顶部一层很淡的主题光晕，让大卡片不至于死板 */
            var glow = new Rectangle(0, 0, Width, Px(150));
            using (var lg = new LinearGradientBrush(glow,
                Color.FromArgb(_dark ? 34 : 22, _accent), Color.FromArgb(0, _accent), 90f))
                g.FillRectangle(lg, glow);

            /* 版本号做成胶囊，当前 / 最新一眼分开 */
            if (_versionOld != null && _versionNew != null)
            {
                int x1 = _versionOld.Left - Px(8);
                int x2 = _versionNew.Right + Px(8);
                var chip = new Rectangle(x1, _versionOld.Top - Px(4),
                    Math.Max(Px(20), x2 - x1), _versionOld.Height + Px(8));
                using (GraphicsPath cp = RoundPath(chip, Px(9)))
                using (var fill = new SolidBrush(Color.FromArgb(_dark ? 150 : 200, _card2)))
                using (var pen = new Pen(_border))
                {
                    g.FillPath(fill, cp);
                    g.DrawPath(pen, cp);
                }
            }

            using (var pen = new Pen(_border))
            {
                using (GraphicsPath p = RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), Px(16)))
                {
                    g.DrawPath(pen, p);
                }
            }

            using (var pen = new Pen(Color.FromArgb(28, _accent)))
            {
                g.DrawLine(pen, Px(24), Px(104), Width - Px(24), Px(104));
            }
            using (var pen = new Pen(_border))
            {
                g.DrawLine(pen, Px(24), Px(410), Width - Px(24), Px(410));
            }
            TextRenderer.DrawText(g, "安装包会校验大小与 SHA-256 · 数据不会丢失", _fontMeta,
                new Rectangle(Px(26), Px(414), Px(280), Px(18)), _faint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            DrawAppIcon(g);
        }

        private void DrawAppIcon(Graphics g)
        {
            var plate = new Rectangle(Px(28), Px(26), Px(48), Px(48));
            using (GraphicsPath p = RoundPath(plate, Px(13)))
            using (var brush = new SolidBrush(Color.FromArgb(34, _accent)))
            {
                g.FillPath(brush, p);
            }
            if (_appIcon != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(_appIcon, new Rectangle(Px(33), Px(31), Px(38), Px(38)));
            }
            else
            {
                using (var brush = new SolidBrush(_accent))
                using (var f = new Font(_family, 18f, FontStyle.Bold, GraphicsUnit.Point))
                {
                    TextRenderer.DrawText(g, "叶", f, plate, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        private void LayoutVersion()
        {
            int x = Px(92);
            int y = Px(64);
            _versionOld.Location = new Point(x, y);
            x += _versionOld.PreferredWidth + Px(9);
            _versionArrow.Location = new Point(x, y);
            x += _versionArrow.PreferredWidth + Px(9);
            _versionNew.Location = new Point(x, y);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private void DragWindow()
        {
            try
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
            catch { /* ignore */ }
        }

        /* ---------------- 下载源与状态 ---------------- */

        private void BuildSources()
        {
            AddSource(_info.Url);
            foreach (string u in _info.Urls) AddSource(u);
        }

        private void AddSource(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            string u = url.Trim();
            if (u.Length == 0) return;
            if (!_sources.Contains(u)) _sources.Add(u);
        }

        private void PrimaryClick()
        {
            if (_downloading) return;
            StartDownload();
        }

        private void SecondaryClick()
        {
            if (_downloading)
            {
                CancelDownload();
                return;
            }
            Close();
        }

        private void StartDownload()
        {
            if (_downloading || _sources.Count == 0) return;

            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { /* 旧环境不支持就忽略 */ }

            _downloading = true;
            _sourceIndex = 0;
            _lastError = "";
            _primary.Text = "下载中…";
            _primary.Enabled = false;
            _secondary.Text = "取消";
            _secondary.Enabled = true;
            _error.Visible = false;
            _progress.Visible = true;
            _progress.Marquee = true;
            _progress.Value = 0;
            _status.Visible = true;
            _status.ForeColor = _sub;
            _status.Text = "正在连接下载服务器…";

            try
            {
                Directory.CreateDirectory(AppPaths.UpdateDir);
                string safeVersion = SafeName(_info.VersionName);
                _finalPath = Path.Combine(AppPaths.UpdateDir, "DayLeaf-Setup-" + safeVersion + ".exe");
                _partPath = _finalPath + ".part";
                foreach (string f in Directory.GetFiles(AppPaths.UpdateDir, "DayLeaf-Setup-*.exe"))
                {
                    try { File.Delete(f); } catch { /* 旧安装包删不掉不影响 */ }
                }
                try { if (File.Exists(_partPath)) File.Delete(_partPath); } catch { }
            }
            catch (Exception ex)
            {
                FailAll("无法准备下载目录", ex.Message);
                return;
            }

            TrySource();
        }

        private void TrySource()
        {
            if (_closing) return;
            if (_sourceIndex >= _sources.Count)
            {
                FailAll("下载失败", _lastError);
                return;
            }

            string url = _sources[_sourceIndex];
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                _lastError = "下载地址无效";
                _sourceIndex++;
                TrySource();
                return;
            }

            _status.ForeColor = _sub;
            _status.Text = _sources.Count > 1
                ? "正在连接下载服务器" + (_sourceIndex > 0 ? "（备用源 " + (_sourceIndex + 1) + "）" : "") + "…"
                : "正在连接下载服务器…";
            _progress.Marquee = true;
            _progress.Value = 0;

            try
            {
                _client = new TimedWebClient();
                _client.Headers[HttpRequestHeader.UserAgent] = "DayLeaf-Desktop/" + _current;
                _client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                _client.DownloadProgressChanged += OnProgress;
                _client.DownloadFileCompleted += OnCompleted;
                _client.DownloadFileAsync(new Uri(url), _partPath);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _sourceIndex++;
                ScheduleNextSource();
            }
        }

        private void ScheduleNextSource()
        {
            try { if (_client != null) { _client.Dispose(); _client = null; } } catch { }
            try { if (_partPath != null && File.Exists(_partPath)) File.Delete(_partPath); } catch { }

            if (_sourceIndex >= _sources.Count)
            {
                FailAll("下载失败", _lastError);
                return;
            }

            _status.ForeColor = _sub;
            _status.Text = "下载源不可用，正在切换备用源…";
            _progress.Marquee = true;
            if (_retryTimer == null)
            {
                _retryTimer = new Timer();
                _retryTimer.Interval = 280;
                _retryTimer.Tick += delegate
                {
                    _retryTimer.Stop();
                    TrySource();
                };
            }
            _retryTimer.Start();
        }

        private void OnProgress(object sender, DownloadProgressChangedEventArgs e)
        {
            if (IsDisposed || _closing) return;
            try
            {
                long total = e.TotalBytesToReceive > 0 ? e.TotalBytesToReceive : (_info.Size > 0 ? _info.Size : 0);
                if (total > 0)
                {
                    int pct = (int)Math.Min(100L, e.BytesReceived * 100L / total);
                    _progress.Marquee = false;
                    _progress.Value = pct;
                    _status.Text = "正在下载 " + pct + "%　·　" + HumanSize(e.BytesReceived) + " / " + HumanSize(total);
                }
                else
                {
                    _progress.Marquee = true;
                    _status.Text = "正在下载 " + HumanSize(e.BytesReceived) + "…";
                }
            }
            catch { /* 进度刷新失败不致命 */ }
        }

        private void OnCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (IsDisposed || _closing) return;

            TimedWebClient client = _client;
            _client = null;
            if (client != null) { try { client.Dispose(); } catch { } }

            if (e.Cancelled)
            {
                CleanPart();
                ResetIdle();
                return;
            }

            if (e.Error != null)
            {
                _lastError = e.Error.Message;
                Log("source[" + _sourceIndex + "] " + e.Error.GetType().Name + ": " + e.Error.Message);
                _sourceIndex++;
                ScheduleNextSource();
                return;
            }

            string verifyError = VerifyPackage(_partPath, _info);
            if (verifyError != null)
            {
                _lastError = verifyError;
                Log("verify failed: " + verifyError + " (" + _partPath + ")");
                _sourceIndex++;
                ScheduleNextSource();
                return;
            }

            try
            {
                if (File.Exists(_finalPath)) File.Delete(_finalPath);
                File.Move(_partPath, _finalPath);
            }
            catch (Exception ex)
            {
                FailAll("无法保存安装包", ex.Message);
                return;
            }

            _downloading = false;
            _progress.Marquee = false;
            _progress.Value = 100;
            _status.ForeColor = _sub;
            _status.Text = "下载完成，正在启动安装程序…";
            _primary.Enabled = false;
            _secondary.Enabled = false;

            try
            {
                var psi = new ProcessStartInfo(_finalPath) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                FailAll("无法打开安装程序", ex.Message);
                return;
            }

            _doneTimer = new Timer();
            _doneTimer.Interval = 700;
            _doneTimer.Tick += delegate
            {
                _doneTimer.Stop();
                Close();
            };
            _doneTimer.Start();
        }

        private void CancelDownload()
        {
            if (!_downloading) return;
            _secondary.Enabled = false;
            _status.Text = "正在取消…";
            try
            {
                if (_client != null) _client.CancelAsync();
                else ResetIdle();
            }
            catch { ResetIdle(); }
        }

        private void ResetIdle()
        {
            if (_closing) return;
            _downloading = false;
            _primary.Text = "立即更新";
            _primary.Enabled = _sources.Count > 0;
            _secondary.Text = "稍后";
            _secondary.Enabled = true;
            _progress.Visible = false;
            _progress.Marquee = false;
            _status.Visible = false;
            _error.Visible = false;
            _meta.Visible = true;
            try { PerformLayout(); Invalidate(true); } catch { /* ignore */ }
        }

        private void FailAll(string title, string raw)
        {
            if (_closing) return;
            _downloading = false;
            _progress.Visible = false;
            _progress.Marquee = false;
            _status.Visible = false;
            _primary.Text = "重试";
            _primary.Enabled = _sources.Count > 0;
            _secondary.Text = "关闭";
            _secondary.Enabled = true;
            ShowError(title, FriendlyError(raw));
            Log("failed: " + title + " | " + (raw ?? ""));
        }

        private void ShowError(string title, string detail)
        {
            _error.Title = title;
            _error.Detail = detail;
            _error.Visible = true;
            _meta.Visible = false;
            try { PerformLayout(); Invalidate(true); } catch { /* ignore */ }
        }

        private void CleanPart()
        {
            try { if (_partPath != null && File.Exists(_partPath)) File.Delete(_partPath); } catch { }
        }

        private static string VerifyPackage(string path, DesktopUpdate.Info info)
        {
            if (!File.Exists(path)) return "下载文件不存在";
            long len;
            try { len = new FileInfo(path).Length; }
            catch (Exception ex) { return "无法读取安装包：" + ex.Message; }
            if (len <= 0) return "下载到的文件为空";
            if (info.Size > 0 && len != info.Size)
            {
                return "安装包大小不符（应为 " + info.Size + " 字节，实际 " + len + " 字节）";
            }
            if (!string.IsNullOrEmpty(info.Sha256) && info.Sha256.Length == 64)
            {
                string got = Sha256(path);
                if (!string.Equals(got, info.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return "安装包校验失败（文件可能被缓存替换）";
                }
            }
            return null;
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string FriendlyError(string raw)
        {
            string s = (raw ?? "").ToLowerInvariant();
            if (s.Contains("remote name") || s.Contains("resolve") || s.Contains("dns") || s.Contains("解析"))
                return "网络无法解析下载地址，请检查网络或代理设置。";
            if (s.Contains("404") || s.Contains("not found"))
                return "下载地址暂时不可用，请稍后重试。";
            if (s.Contains("timed out") || s.Contains("超时"))
                return "连接下载服务器超时，请检查网络后重试。";
            if (s.Contains("校验") || s.Contains("大小不符"))
                return "下载文件校验失败，请稍后重试。";
            return "网络连接中断或服务器不可用，请稍后重试。";
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "new";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') sb.Append(c);
            }
            return sb.Length == 0 ? "new" : sb.ToString();
        }

        private static string HumanSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return Math.Max(1, (long)Math.Round(bytes / 1024.0)) + " KB";
            return (bytes / 1048576.0).ToString("0.0") + " MB";
        }

        private void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.AppendAllText(Path.Combine(AppPaths.Root, "update.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + "\r\n", Encoding.UTF8);
            }
            catch { /* ignore */ }
        }

        /* ---------------- 生命周期 ---------------- */

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            /* 更新弹窗只接受 Esc 和 Enter；其它键一律吞掉，避免焦点框、
               方向键或输入法把自绘布局搅乱。 */
            if (keyData == Keys.Escape)
            {
                if (!_downloading) Close();
                return true;
            }
            if (keyData == Keys.Enter)
            {
                if (!_downloading && _primary != null && _primary.Enabled) _primary.PerformClick();
                return true;
            }
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            if (_retryTimer != null) { try { _retryTimer.Stop(); } catch { } }
            if (_doneTimer != null) { try { _doneTimer.Stop(); } catch { } }
            if (_client != null)
            {
                try { _client.CancelAsync(); } catch { }
            }
            CleanPart();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_retryTimer != null) { try { _retryTimer.Dispose(); } catch { } }
                if (_doneTimer != null) { try { _doneTimer.Dispose(); } catch { } }
                if (_client != null) { try { _client.Dispose(); } catch { } }
                Font[] fonts = { _fontTitle, _fontVersion, _fontVersionStrong, _fontSection, _fontBody,
                    _fontMeta, _fontStatus, _fontButton, _fontButtonSmall, _fontErrorTitle,
                    _fontErrorDetail, _fontClose6 };
                foreach (Font f in fonts) { try { if (f != null) f.Dispose(); } catch { } }
                try { if (_appIcon != null) _appIcon.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        /// <summary>测试用：直接切到错误态（--update-preview）。</summary>
        internal void ShowPreviewError()
        {
            _primary.Text = "重试";
            _secondary.Text = "关闭";
            _meta.Visible = false;
            _progress.Visible = false;
            _status.Visible = false;
            ShowError("下载失败", "网络无法解析下载地址，请检查网络或代理设置。");
        }

        /// <summary>测试用：直接切到下载中状态（--update-preview）。</summary>
        internal void ShowPreviewProgress(int percent, string text)
        {
            _meta.Visible = true;
            _error.Visible = false;
            _progress.Visible = true;
            _progress.Marquee = false;
            _progress.Value = percent;
            _status.Visible = true;
            _status.ForeColor = _sub;
            _status.Text = text;
            _primary.Text = "下载中…";
            _primary.Enabled = false;
            _secondary.Text = "取消";
        }

        /// <summary>测试用：显示后截图并自动关闭（--update-preview）。</summary>
        internal void CaptureAndClose(string path, int delayMs)
        {
            var timer = new Timer();
            timer.Interval = Math.Max(300, delayMs);
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                try
                {
                    using (var bmp = new Bitmap(Width, Height))
                    {
                        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                        bmp.Save(path, ImageFormat.Png);
                    }
                }
                catch { /* ignore */ }
                Close();
            };
            Shown += delegate { timer.Start(); };
        }

        /* ==================================================================
         * 自绘控件
         * ================================================================ */

        /// <summary>圆角按钮：主按钮渐变，次按钮描边，支持悬停/按下/焦点。</summary>
        private sealed class PillButton : Button
        {
            private readonly UpdateDialog _o;
            private readonly bool _primary;
            private bool _hover;
            private bool _down;
            internal int RadiusBase = 10;

            internal PillButton(UpdateDialog owner, bool primary, string text)
            {
                _o = owner;
                _primary = primary;
                Text = text;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = Color.Transparent;
                UseVisualStyleBackColor = false;
                Cursor = Cursors.Hand;
                TabStop = true;
                UseMnemonic = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                int radius = _o.Px(RadiusBase);
                using (GraphicsPath path = UpdateDialog.RoundPath(r, radius))
                {
                    if (_primary)
                    {
                        if (!Enabled)
                        {
                            using (var brush = new SolidBrush(_o._card2)) g.FillPath(brush, path);
                        }
                        else
                        {
                            Color c1 = _o._accent;
                            Color c2 = _o._accent2;
                            if (_down) { c1 = Darken(c1, 0.10f); c2 = Darken(c2, 0.12f); }
                            else if (_hover) { c1 = Lighten(c1, 0.10f); c2 = Lighten(c2, 0.10f); }
                            using (var brush = new LinearGradientBrush(r, c1, c2, 0f)) g.FillPath(brush, path);
                        }
                    }
                    else
                    {
                        using (var brush = new SolidBrush(_hover ? _o._card2 : Color.FromArgb(0, _o._card2)))
                            g.FillPath(brush, path);
                        using (var pen = new Pen(_o._border)) g.DrawPath(pen, path);
                        if (_hover)
                        {
                            using (var brush = new SolidBrush(Color.FromArgb(_o._dark ? 12 : 20, _o._text)))
                                g.FillPath(brush, path);
                        }
                        if (_down)
                        {
                            using (var brush = new SolidBrush(Color.FromArgb(22, 0, 0, 0)))
                                g.FillPath(brush, path);
                        }
                    }

                    if (Focused && ShowFocusCues)
                    {
                        var inner = Rectangle.Inflate(r, -_o.Px(3), -_o.Px(3));
                        using (GraphicsPath fp = UpdateDialog.RoundPath(inner, Math.Max(2, radius - _o.Px(3))))
                        using (var pen = new Pen(Color.FromArgb(150, _o._accent), _o.Px(1))) g.DrawPath(pen, fp);
                    }
                }

                Color fg;
                if (!Enabled) fg = _o._faint;
                else if (_primary) fg = Color.White;
                else fg = _o._sub;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            private static Color Lighten(Color c, float amount)
            {
                return Color.FromArgb(c.A,
                    Math.Min(255, (int)(c.R + (255 - c.R) * amount)),
                    Math.Min(255, (int)(c.G + (255 - c.G) * amount)),
                    Math.Min(255, (int)(c.B + (255 - c.B) * amount)));
            }

            private static Color Darken(Color c, float amount)
            {
                return Color.FromArgb(c.A,
                    Math.Max(0, (int)(c.R * (1 - amount))),
                    Math.Max(0, (int)(c.G * (1 - amount))),
                    Math.Max(0, (int)(c.B * (1 - amount))));
            }
        }

        /// <summary>更新说明卡片：圆角底、标题、自动换行正文、细滚动条。</summary>
        private sealed class NotesCard : Control
        {
            private readonly UpdateDialog _o;
            private readonly string _title;
            private readonly string _text;
            private int _scroll;
            private int _contentHeight;
            private int _visibleHeight;

            internal NotesCard(UpdateDialog owner, string title, string text)
            {
                _o = owner;
                _title = title;
                _text = text ?? "";
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = owner._card;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = UpdateDialog.RoundPath(r, _o.Px(14)))
                using (var brush = new SolidBrush(_o._card)) g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(_o._dark ? 60 : 90, _o._border)))
                using (GraphicsPath path = UpdateDialog.RoundPath(r, _o.Px(14))) g.DrawPath(pen, path);

                int pad = _o.Px(16);
                var accentBar = new Rectangle(pad, _o.Px(17), _o.Px(3), _o.Px(12));
                using (var brush = new SolidBrush(_o._accent))
                using (GraphicsPath p = UpdateDialog.RoundPath(accentBar, _o.Px(2))) g.FillPath(brush, p);

                TextRenderer.DrawText(g, _title, _o._fontSection,
                    new Rectangle(pad + _o.Px(11), _o.Px(13), Width - pad * 2, _o.Px(20)),
                    _o._sub, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                int top = _o.Px(42);
                int rightGap = _o.Px(16);
                int w = Width - pad - rightGap;
                _visibleHeight = Height - top - _o.Px(14);
                if (_visibleHeight < 20) _visibleHeight = 20;

                TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding |
                                        TextFormatFlags.TextBoxControl | TextFormatFlags.Left;
                Size measured = TextRenderer.MeasureText(g, _text, _o._fontBody,
                    new Size(w - _o.Px(2), int.MaxValue), flags);
                _contentHeight = Math.Max(measured.Height, 0);
                int maxScroll = Math.Max(0, _contentHeight - _visibleHeight);
                if (_scroll > maxScroll) _scroll = maxScroll;
                if (_scroll < 0) _scroll = 0;

                var clip = new Rectangle(pad, top, w, _visibleHeight);
                Region oldClip = g.Clip;
                g.SetClip(clip);
                TextRenderer.DrawText(g, _text, _o._fontBody,
                    new Rectangle(pad, top - _scroll, w, Math.Max(_contentHeight, _visibleHeight)),
                    _o._text, flags);
                g.Clip = oldClip;

                if (_contentHeight > _visibleHeight)
                {
                    int barW = _o.Px(3);
                    int barX = Width - _o.Px(10);
                    int trackH = _visibleHeight;
                    var track = new Rectangle(barX, top, barW, trackH);
                    using (var brush = new SolidBrush(Color.FromArgb(_o._dark ? 70 : 50, _o._border)))
                    using (GraphicsPath p = UpdateDialog.RoundPath(track, barW / 2)) g.FillPath(brush, p);

                    int thumbH = Math.Max(_o.Px(18), (int)((long)trackH * _visibleHeight / Math.Max(1, _contentHeight)));
                    if (thumbH > trackH) thumbH = trackH;
                    int thumbY = top + (maxScroll == 0 ? 0 : (int)((long)(trackH - thumbH) * _scroll / maxScroll));
                    var thumb = new Rectangle(barX, thumbY, barW, thumbH);
                    using (var brush = new SolidBrush(Color.FromArgb(170, _o._accent)))
                    using (GraphicsPath p = UpdateDialog.RoundPath(thumb, barW / 2)) g.FillPath(brush, p);
                }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                int maxScroll = Math.Max(0, _contentHeight - _visibleHeight);
                if (maxScroll <= 0) return;
                _scroll -= e.Delta / 2;
                if (_scroll < 0) _scroll = 0;
                if (_scroll > maxScroll) _scroll = maxScroll;
                Invalidate();
            }
        }

        /// <summary>细圆角进度条；未知总大小时显示循环滑块。</summary>
        private sealed class ProgressEx : Control
        {
            private readonly UpdateDialog _o;
            private int _value;
            private bool _marquee;
            private int _pos;
            private Timer _timer;

            internal ProgressEx(UpdateDialog owner)
            {
                _o = owner;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = owner._bg;
            }

            internal int Value
            {
                get { return _value; }
                set
                {
                    int v = Math.Max(0, Math.Min(100, value));
                    if (v == _value) return;
                    _value = v;
                    Invalidate();
                }
            }

            internal bool Marquee
            {
                get { return _marquee; }
                set
                {
                    if (_marquee == value) return;
                    _marquee = value;
                    if (_marquee) StartTimer(); else StopTimer();
                    Invalidate();
                }
            }

            private void StartTimer()
            {
                if (_timer == null)
                {
                    _timer = new Timer();
                    _timer.Interval = 28;
                    _timer.Tick += delegate
                    {
                        _pos += 4;
                        if (_pos > Width + _o.Px(80)) _pos = -_o.Px(80);
                        Invalidate();
                    };
                }
                _timer.Start();
            }

            private void StopTimer()
            {
                if (_timer != null) _timer.Stop();
            }

            protected override void OnVisibleChanged(EventArgs e)
            {
                base.OnVisibleChanged(e);
                if (!Visible) StopTimer();
                else if (_marquee) StartTimer();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int h = Height;
                var track = new Rectangle(0, 0, Width - 1, h - 1);
                using (var brush = new SolidBrush(Color.FromArgb(_o._dark ? 120 : 80, _o._border)))
                using (GraphicsPath p = UpdateDialog.RoundPath(track, h / 2)) g.FillPath(brush, p);

                if (_marquee)
                {
                    int segW = Math.Max(_o.Px(60), Width / 4);
                    int x = _pos - segW;
                    if (x < 0) x = 0;
                    int w = Math.Min(segW, Width - x);
                    var seg = new Rectangle(x, 0, Math.Max(2, w - 1), h - 1);
                    using (var brush = new LinearGradientBrush(
                        new Rectangle(0, 0, Math.Max(2, Width), Math.Max(2, h)),
                        Color.FromArgb(0, _o._accent), Color.FromArgb(255, _o._accent), 0f))
                    using (GraphicsPath p = UpdateDialog.RoundPath(seg, h / 2)) g.FillPath(brush, p);
                }
                else
                {
                    int w = (int)Math.Round((Width - 1) * (_value / 100.0));
                    if (w > 3)
                    {
                        var fill = new Rectangle(0, 0, w, h - 1);
                        using (var brush = new LinearGradientBrush(
                            new Rectangle(0, 0, Math.Max(2, Width), Math.Max(2, h)),
                            _o._accent, _o._accent2, 0f))
                        using (GraphicsPath p = UpdateDialog.RoundPath(fill, h / 2)) g.FillPath(brush, p);
                    }
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _timer != null) { try { _timer.Dispose(); } catch { } }
                base.Dispose(disposing);
            }
        }

        /// <summary>错误提示卡片：左侧感叹号，标题 + 友好说明，详细异常写日志不吓用户。</summary>
        private sealed class ErrorCard : Control
        {
            private readonly UpdateDialog _o;
            private string _title = "";
            private string _detail = "";

            internal string Title
            {
                get { return _title; }
                set { _title = value ?? ""; Invalidate(); }
            }

            internal string Detail
            {
                get { return _detail; }
                set { _detail = value ?? ""; Invalidate(); }
            }

            internal ErrorCard(UpdateDialog owner)
            {
                _o = owner;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = owner._bg;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = UpdateDialog.RoundPath(r, _o.Px(12)))
                using (var brush = new SolidBrush(_o._errorBg)) g.FillPath(brush, path);
                using (GraphicsPath path = UpdateDialog.RoundPath(r, _o.Px(12)))
                using (var pen = new Pen(Color.FromArgb(_o._dark ? 70 : 90, _o._errorFg))) g.DrawPath(pen, path);

                var dot = new Rectangle(_o.Px(14), _o.Px(18), _o.Px(20), _o.Px(20));
                using (var brush = new SolidBrush(Color.FromArgb(40, _o._errorFg)))
                using (GraphicsPath p = UpdateDialog.RoundPath(dot, dot.Width / 2)) g.FillPath(brush, p);
                TextRenderer.DrawText(g, "!", _o._fontErrorTitle, dot, _o._errorFg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                int x = _o.Px(44);
                int w = Width - x - _o.Px(14);
                TextRenderer.DrawText(g, _title, _o._fontErrorTitle,
                    new Rectangle(x, _o.Px(9), w, _o.Px(20)), _o._errorFg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, _detail, _o._fontErrorDetail,
                    new Rectangle(x, _o.Px(30), w, Height - _o.Px(36)), _o._sub,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }

        /// <summary>带连接/读取超时的 WebClient，防止某个源卡死整个更新流程。</summary>
        private sealed class TimedWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest r = base.GetWebRequest(address);
                if (r != null)
                {
                    r.Timeout = 20000;
                    var http = r as HttpWebRequest;
                    if (http != null) http.ReadWriteTimeout = 30000;
                }
                return r;
            }
        }
    }
}
