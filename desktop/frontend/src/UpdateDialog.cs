using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace DayLeaf
{
    /// <summary>
    /// 软件内更新弹窗：展示新版本信息，右下角「立即下载」直接在应用内下载安装包，
    /// 下载完成后自动拉起安装程序；左下角「稍后下载」关闭弹窗。
    /// </summary>
    internal sealed class UpdateDialog : Form
    {
        private readonly DesktopUpdate.Info _info;
        private readonly string _current;
        private readonly bool _dark;

        private readonly Color _bg;
        private readonly Color _card;
        private readonly Color _title;
        private readonly Color _sub;
        private readonly Color _border;
        private static readonly Color Primary = Color.FromArgb(0x4C, 0x6F, 0xFF);
        private static readonly Color PrimaryHover = Color.FromArgb(0x3D, 0x5A, 0xE8);
        private static readonly Color PrimaryDown = Color.FromArgb(0x35, 0x4F, 0xD0);
        private static readonly Color Danger = Color.FromArgb(0xE0, 0x4F, 0x5A);

        private readonly Label _versionLabel;
        private readonly TextBox _notesBox;
        private readonly ProgressBar _progress;
        private readonly Label _progressLabel;
        private readonly Button _laterButton;
        private readonly Button _downloadButton;

        private WebClient _client;
        private string _targetPath;

        public UpdateDialog(DesktopUpdate.Info info, string current, bool dark)
        {
            _info = info;
            _current = current;
            _dark = dark;

            _bg = dark ? Color.FromArgb(0x0E, 0x11, 0x18) : Color.FromArgb(0xEC, 0xEF, 0xF6);
            _card = dark ? Color.FromArgb(0x1A, 0x1E, 0x2A) : Color.White;
            _title = dark ? Color.FromArgb(0xE6, 0xE9, 0xF0) : Color.FromArgb(0x11, 0x14, 0x1C);
            _sub = dark ? Color.FromArgb(0x9A, 0xA3, 0xB2) : Color.FromArgb(0x5B, 0x64, 0x72);
            _border = dark ? Color.FromArgb(0x3A, 0x43, 0x52) : Color.FromArgb(0xC6, 0xCD, 0xDA);

            FontFamily ff = SystemFonts.MessageBoxFont.FontFamily;

            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = _bg;
            Text = "叶历 DayLeaf";
            ClientSize = new Size(440, 372);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            /* 图标 */
            var iconBox = new PictureBox
            {
                Bounds = new Rectangle(24, 22, 44, 44),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            try { using (var ic = new Icon(Icon, 44, 44)) iconBox.Image = ic.ToBitmap(); }
            catch { /* 用不到图标就算了 */ }
            Controls.Add(iconBox);

            /* 标题 */
            var titleLabel = new Label
            {
                Text = "发现新版本",
                Font = new Font(ff, 14f, FontStyle.Bold),
                ForeColor = _title,
                Location = new Point(78, 20),
                AutoSize = true
            };
            Controls.Add(titleLabel);

            /* 版本号一行 */
            _versionLabel = new Label
            {
                Text = "当前 v" + current + "　→　最新 v" + info.VersionName,
                Font = new Font(ff, 9f),
                ForeColor = _sub,
                Location = new Point(78, 52),
                AutoSize = true
            };
            Controls.Add(_versionLabel);

            /* 更新说明 */
            string note = string.IsNullOrEmpty(info.Notes) ? "没有附加说明。" : info.Notes;
            if (info.Size > 0) note += "\r\n\r\n安装包大小约 " + HumanSize(info.Size);
            _notesBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = _card,
                ForeColor = _title,
                ScrollBars = ScrollBars.Vertical,
                Bounds = new Rectangle(24, 92, 392, 108),
                Font = new Font(ff, 9.5f),
                Text = note
            };
            Controls.Add(_notesBox);

            /* 下载进度 */
            _progress = new ProgressBar
            {
                Bounds = new Rectangle(24, 210, 392, 10),
                Visible = false
            };
            Controls.Add(_progress);

            _progressLabel = new Label
            {
                Bounds = new Rectangle(24, 226, 392, 20),
                ForeColor = _sub,
                Font = new Font(ff, 8.5f),
                Visible = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(_progressLabel);

            /* 底部按钮：左下「稍后下载」、右下「立即下载」 */
            _laterButton = new Button
            {
                Text = "稍后下载",
                Bounds = new Rectangle(24, 320, 110, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = _card,
                ForeColor = _sub,
                Cursor = Cursors.Hand,
                Font = new Font(ff, 9.5f)
            };
            _laterButton.FlatAppearance.BorderSize = 1;
            _laterButton.FlatAppearance.BorderColor = _border;
            _laterButton.FlatAppearance.MouseOverBackColor =
                dark ? Color.FromArgb(0x24, 0x29, 0x36) : Color.FromArgb(0xE4, 0xE9, 0xF2);
            _laterButton.Click += delegate { Close(); };
            Controls.Add(_laterButton);

            _downloadButton = new Button
            {
                Text = "立即下载",
                Bounds = new Rectangle(304, 316, 112, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Primary,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font(ff, 10f, FontStyle.Bold)
            };
            _downloadButton.FlatAppearance.BorderSize = 0;
            _downloadButton.FlatAppearance.MouseOverBackColor = PrimaryHover;
            _downloadButton.FlatAppearance.MouseDownBackColor = PrimaryDown;
            _downloadButton.Click += delegate { StartDownload(); };
            Controls.Add(_downloadButton);
        }

        /* ------------------------------------------------------------------
         * 下载
         * ---------------------------------------------------------------- */

        private void StartDownload()
        {
            if (_client != null) return;

            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { /* 旧环境不支持就忽略 */ }

            /* 回到初始按钮状态（失败后点「重试」会再进来） */
            _downloadButton.Text = "立即下载";
            _laterButton.Text = "稍后下载";
            _downloadButton.Enabled = false;
            _laterButton.Enabled = false;
            _progressLabel.ForeColor = _sub;
            _progressLabel.Text = "正在连接下载服务器...";
            _progressLabel.Visible = true;
            _progress.Visible = true;
            _progress.Style = ProgressBarStyle.Marquee;

            try
            {
                Directory.CreateDirectory(AppPaths.UpdateDir);
                foreach (string f in Directory.GetFiles(AppPaths.UpdateDir, "DayLeaf-Setup-*.exe"))
                {
                    try { File.Delete(f); } catch { /* 旧的删不掉不影响 */ }
                }

                _targetPath = Path.Combine(AppPaths.UpdateDir, "DayLeaf-Setup-" + _info.VersionName + ".exe");
                _client = new WebClient();
                _client.Headers[HttpRequestHeader.UserAgent] = "DayLeaf-Desktop/" + _current;
                _client.DownloadProgressChanged += OnProgress;
                _client.DownloadFileCompleted += OnCompleted;
                _client.DownloadFileAsync(new Uri(_info.Url), _targetPath);
            }
            catch (Exception ex)
            {
                Fail("下载失败：" + ex.Message);
            }
        }

        private void OnProgress(object sender, DownloadProgressChangedEventArgs e)
        {
            if (IsDisposed) return;
            try
            {
                long total = e.TotalBytesToReceive > 0 ? e.TotalBytesToReceive : (_info.Size > 0 ? _info.Size : 0);
                if (total > 0)
                {
                    _progress.Style = ProgressBarStyle.Blocks;
                    int pct = (int)Math.Min(100L, e.BytesReceived * 100L / total);
                    _progress.Value = pct;
                    _progressLabel.Text = HumanSize(e.BytesReceived) + " / " + HumanSize(total) + "　" + pct + "%";
                }
                else
                {
                    _progress.Style = ProgressBarStyle.Marquee;
                    _progressLabel.Text = "已下载 " + HumanSize(e.BytesReceived) + " ...";
                }
            }
            catch { /* 进度刷新失败不致命 */ }
        }

        private void OnCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (IsDisposed) return;

            var client = _client;
            _client = null;
            if (client != null) { try { client.Dispose(); } catch { } }

            if (e.Cancelled) { Close(); return; }

            if (e.Error != null)
            {
                Fail("下载失败：" + e.Error.Message);
                return;
            }

            /* 有预期大小时校验一下，防止下载不完整 */
            try
            {
                if (_info.Size > 0 && File.Exists(_targetPath))
                {
                    long actual = new FileInfo(_targetPath).Length;
                    if (actual < _info.Size)
                    {
                        Fail("下载不完整（" + HumanSize(actual) + " / " + HumanSize(_info.Size) + "），请重试。");
                        return;
                    }
                }
            }
            catch { /* 校验不了就按成功处理 */ }

            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = 100;
            _progressLabel.Text = "下载完成，正在打开安装程序...";

            try
            {
                var psi = new ProcessStartInfo(_targetPath) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Fail("无法打开安装程序：" + ex.Message);
                return;
            }

            Close();
        }

        private void Fail(string message)
        {
            _downloadButton.Text = "重试";
            _downloadButton.Enabled = true;
            _laterButton.Text = "关闭";
            _laterButton.Enabled = true;
            _progress.Visible = false;
            _progressLabel.ForeColor = Danger;
            _progressLabel.Text = message;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_client != null)
            {
                try { _client.CancelAsync(); } catch { }
            }
            base.OnFormClosing(e);
        }

        private static string HumanSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return Math.Max(1, (long)Math.Round(bytes / 1024.0)) + " KB";
            return (bytes / 1048576.0).ToString("0.0") + " MB";
        }
    }
}
