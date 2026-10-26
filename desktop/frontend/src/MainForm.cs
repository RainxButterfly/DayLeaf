using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DayLeaf
{
    /// <summary>
    /// 主窗口：承载与安卓端同一份 index.html，补齐桌面端需要的原生能力。
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const string VirtualHost = "dayleaf.app";
        private const string StartUrl = "https://dayleaf.app/index.html";

        private readonly WebView2 _web = new WebView2();
        private bool _dark;
        private readonly bool _selfTest;
        private bool _selfTestDone;
        private readonly bool _updatePreview;
        private bool _updatePreviewDone;
        private bool _startupCheckDone;

        /* 后台提醒：窗口关闭后留在托盘，用 NotifyIcon 继续提醒 */
        private LanSyncServer _lanServer;
        private NotifyIcon _tray;
        private System.Windows.Forms.Timer _reminderTimer;
        private System.Windows.Forms.Timer _catchUpTimer;
        private bool _backgroundReminders;
        private bool _exiting;
        private bool _trayTipShown;
        private List<ReminderItem> _reminders = new List<ReminderItem>();
        private readonly HashSet<string> _firedReminders = new HashSet<string>();
        private ReminderItem _lastFired;                        // 最近响过的一条，供「稍后提醒」用
        private ToolStripMenuItem _snoozeItem;
        private ToolStripMenuItem _widgetMenuItem;
        private NextUpForm _widget;                             // 桌面「下一项」小窗，懒创建
        private List<WidgetItem> _widgetItems = new List<WidgetItem>();
        private bool _reminderStateReady;                       // 提醒开关是否已从磁盘/网页读到过
        private int _webdavBusy;                                // WebDAV 同时只允许跑一个
        private readonly bool _trayMode;                        // --tray：开机自启静默进托盘
        private string ReminderFile { get { return Path.Combine(AppPaths.Root, "reminders.json"); } }

        private sealed class ReminderItem
        {
            public string Id = "";
            public long At;
            public string Title = "日程提醒";
            public string Body = "";
            public string Kind = "rm";
        }

        public MainForm()
        {
            AppPaths.Ensure();
            _dark = AppPaths.IsDarkNow();
            _selfTest = Array.IndexOf(Environment.GetCommandLineArgs(), "--selftest") >= 0;
            _updatePreview = Array.IndexOf(Environment.GetCommandLineArgs(), "--update-preview") >= 0;
            _trayMode = Array.IndexOf(Environment.GetCommandLineArgs(), "--tray") >= 0;

            SuspendLayout();
            Text = "叶历 DayLeaf";
            StartPosition = FormStartPosition.CenterScreen;

            // 界面在 960px 以上才切换成桌面布局，而窗口尺寸是物理像素，
            // 系统开了缩放（125%/150%）时 CSS 宽度会按比例缩水，所以这里按 DPI 放大。
            float scale = SystemScale();
            int screenW = ScreenWidth();
            int screenH = ScreenHeight();
            int wantW = (int)Math.Round(1240 * scale);
            int wantH = (int)Math.Round(830 * scale);
            Size = new Size(
                Math.Min(wantW, (int)(screenW * 0.94)),
                Math.Min(wantH, (int)(screenH * 0.92)));
            MinimumSize = new Size(
                (int)Math.Round(980 * scale),
                (int)Math.Round(650 * scale));

            BackColor = AppPaths.BgColor(_dark);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { /* 用默认图标 */ }

            _web.Dock = DockStyle.Fill;
            _web.DefaultBackgroundColor = AppPaths.BgColor(_dark);
            Controls.Add(_web);
            ResumeLayout(false);

            RestoreWindowBounds();
            SetupTray();
            LoadRemindersFromDisk();
            ScheduleCatchUpPass();

            Load += OnLoadAsync;
            FormClosing += OnFormClosing;
            Shown += delegate { ApplyTheme(_dark); OnFirstShown(); };
        }

        /* ------------------------------------------------------------------
         * 启动
         * ---------------------------------------------------------------- */

        private async void OnLoadAsync(object sender, EventArgs e)
        {
            try
            {
                string indexPath = HtmlBundle.EnsureExtracted();
                string wwwDir = Path.GetDirectoryName(indexPath);

                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData);
                await _web.EnsureCoreWebView2Async(env);

                CoreWebView2 core = _web.CoreWebView2;
                CoreWebView2Settings s = core.Settings;
                s.IsStatusBarEnabled = false;
                s.AreDevToolsEnabled = false;
                s.AreDefaultContextMenusEnabled = false;  // 右键改用应用内菜单（网页自己实现）
                s.IsZoomControlEnabled = false;
                s.IsPinchZoomEnabled = false;
                s.IsSwipeNavigationEnabled = false;
                s.IsPasswordAutosaveEnabled = false;
                s.IsGeneralAutofillEnabled = false;
                try { s.AreBrowserAcceleratorKeysEnabled = false; }
                catch (Exception) { /* 旧运行时没有这个开关 */ }

                // 固定虚拟域名：localStorage / IndexedDB 有稳定来源，和安卓端行为一致
                core.SetVirtualHostNameToFolderMapping(VirtualHost, wwwDir, CoreWebView2HostResourceAccessKind.Allow);

                core.WebMessageReceived += OnWebMessage;
                core.NewWindowRequested += OnNewWindow;
                core.NavigationStarting += OnNavigationStarting;
                core.ProcessFailed += OnProcessFailed;
                core.NavigationCompleted += OnNavigationCompleted;
                core.PermissionRequested += OnPermissionRequested;
                try { core.DownloadStarting += OnDownloadStarting; } catch (Exception) { /* 旧运行时没有这个事件 */ }

                await core.AddScriptToExecuteOnDocumentCreatedAsync(BuildBridgeScript());

                ApplyTheme(_dark);
                core.Navigate(StartUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "界面初始化失败：" + ex.Message +
                    "\n\n请确认系统已安装 WebView2 运行时（Windows 10/11 通常自带）。",
                    "叶历 DayLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>注入 window.DayLeafNative，让网页里的调用原样可用。</summary>
        private static string BuildBridgeScript()
        {
            string version = DesktopUpdate.CurrentVersion().Replace("\\", "").Replace("'", "");
            var sb = new StringBuilder();
            sb.Append("(function(){ if (window.DayLeafNative) return;");
            sb.Append("var post=function(o){try{window.chrome.webview.postMessage(JSON.stringify(o));}catch(e){}};");
            sb.Append("window.DayLeafNative={");
            sb.Append("checkUpdate:function(){post({cmd:'checkUpdate'});},");
            sb.Append("setThemeMode:function(m){post({cmd:'theme',arg:String(m||'light')});},");
            sb.Append("saveTextFile:function(n,c){post({cmd:'save',name:String(n||'dayleaf.txt'),content:String(c==null?'':c)});},");
            sb.Append("syncReminders:function(j){post({cmd:'reminders',arg:String(j||'{}')});},");
            sb.Append("updateTrayStatus:function(j){post({cmd:'trayStatus',arg:String(j||'{}')});},");
            sb.Append("updateWidget:function(j){post({cmd:'widget',arg:String(j||'{}')});},");
            sb.Append("saveBinary:function(n,b,m){post({cmd:'saveBinary',name:String(n||'dayleaf.bin'),base64:String(b==null?'':b),mime:String(m==null?'':m)});},");
            sb.Append("webdavSync:function(j){post({cmd:'webdav',arg:String(j||'{}')});},");
            sb.Append("lanStart:function(t){post({cmd:'lanStart',arg:String(t||'')});},");
            sb.Append("lanStop:function(){post({cmd:'lanStop'});},");
            sb.Append("lanPublish:function(j){post({cmd:'lanPublish',arg:String(j||'{}')});},");
            sb.Append("lanInfo:function(){post({cmd:'lanInfo'});},");
            sb.Append("setDesktopWidget:function(on){post({cmd:'desktopWidget',arg:on?'1':'0'});},");
            sb.Append("getDesktopWidget:function(){post({cmd:'getDesktopWidget'});},");
            sb.Append("isForeground:function(){return !document.hidden && document.hasFocus();},");
            sb.Append("getVersionName:function(){return '").Append(version).Append("';},");
            // 桌面端不提供 notify/ensureNotifyPermission（置 undefined），网页会回落到
            // WebView2 自带的 Notification API；授权在 PermissionRequested 里统一放行。
            // 这样「点击通知跳转到日程」走网页里已有的 onclick 逻辑。
            sb.Append("notify:undefined,ensureNotifyPermission:undefined,vibrate:function(){},setHasModal:function(){}");
            sb.Append("};})();");
            return sb.ToString();
        }

        /* ------------------------------------------------------------------
         * 网页 → 原生
         * ---------------------------------------------------------------- */

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw;
            try { raw = e.TryGetWebMessageAsString(); }
            catch (Exception) { return; }
            if (string.IsNullOrEmpty(raw)) return;

            Dictionary<string, object> msg;
            try { msg = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw); }
            catch (Exception) { return; }
            if (msg == null) return;

            string cmd = msg.ContainsKey("cmd") ? Convert.ToString(msg["cmd"]) : "";

            if (cmd == "theme")
            {
                string mode = msg.ContainsKey("arg") ? Convert.ToString(msg["arg"]) : "light";
                AppPaths.WriteTheme(mode);
                ApplyTheme(AppPaths.IsDark(mode));
            }
            else if (cmd == "checkUpdate")
            {
                RunUpdateCheck();
            }
            else if (cmd == "save")
            {
                string name = msg.ContainsKey("name") ? Convert.ToString(msg["name"]) : "dayleaf.txt";
                string content = msg.ContainsKey("content") ? Convert.ToString(msg["content"]) : "";
                SaveTextFile(name, content);
            }
            else if (cmd == "reminders")
            {
                string json = msg.ContainsKey("arg") ? Convert.ToString(msg["arg"]) : "{}";
                ApplyReminders(json);
            }
            else if (cmd == "trayStatus")
            {
                string json = msg.ContainsKey("arg") ? Convert.ToString(msg["arg"]) : "{}";
                ApplyTrayStatus(json);
            }
            else if (cmd == "widget")
            {
                string json = msg.ContainsKey("arg") ? Convert.ToString(msg["arg"]) : "{}";
                ApplyWidget(json);
            }
            else if (cmd == "saveBinary")
            {
                string name = msg.ContainsKey("name") ? Convert.ToString(msg["name"]) : "dayleaf.bin";
                string base64 = msg.ContainsKey("base64") ? Convert.ToString(msg["base64"]) : "";
                string mime = msg.ContainsKey("mime") ? Convert.ToString(msg["mime"]) : "";
                SaveBinaryFile(name, base64, mime);
            }
            else if (cmd == "webdav")
            {
                string json = msg.ContainsKey("arg") ? Convert.ToString(msg["arg"]) : "{}";
                BeginWebDavSync(json);
            }
            else if (cmd == "desktopWidget")
            {
                string on = msg.ContainsKey("arg") ? Convert.ToString(msg["arg"]) : "0";
                SetDesktopWidget(on == "1");
            }
            else if (cmd == "getDesktopWidget")
            {
                ReportDesktopWidget();
            }
            else if (cmd == "lanStart")
            {
                string token = msg.ContainsKey("arg") ? Convert.ToString(msg["arg"]) : "";
                StartLanSync(token);
            }
            else if (cmd == "lanStop")
            {
                StopLanSync();
            }
            else if (cmd == "lanPublish")
            {
                if (_lanServer != null && _lanServer.Running)
                {
                    _lanServer.Latest = msg.ContainsKey("arg") ? Convert.ToString(msg["arg"]) : "";
                }
            }
            else if (cmd == "lanInfo")
            {
                ReportLanInfo();
            }
        }

        /// <summary>导出 JSON / ics：走系统「另存为」。</summary>
        private void SaveTextFile(string name, string content)
        {
            using (var dlg = new SaveFileDialog())
            {
                string ext = Path.GetExtension(name);
                if (string.IsNullOrEmpty(ext)) ext = ".txt";
                dlg.FileName = name;
                dlg.Filter = BuildFilter(ext);
                dlg.AddExtension = true;
                dlg.OverwritePrompt = true;
                dlg.Title = "保存导出文件";

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    // UTF-8 无 BOM：记事本与各家日历都能正常识别
                    File.WriteAllText(dlg.FileName, content ?? "", new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：" + ex.Message, "叶历 DayLeaf",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static string BuildFilter(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".json": return "JSON 备份 (*.json)|*.json|所有文件 (*.*)|*.*";
                case ".ics": return "日历文件 (*.ics)|*.ics|所有文件 (*.*)|*.*";
                case ".txt": return "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                case ".png": return "PNG 图片 (*.png)|*.png|所有文件 (*.*)|*.*";
                case ".jpg":
                case ".jpeg": return "JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg|所有文件 (*.*)|*.*";
                case ".gif": return "GIF 图片 (*.gif)|*.gif|所有文件 (*.*)|*.*";
                case ".webp": return "WebP 图片 (*.webp)|*.webp|所有文件 (*.*)|*.*";
                case ".pdf": return "PDF 文档 (*.pdf)|*.pdf|所有文件 (*.*)|*.*";
                case ".zip": return "压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*";
                default: return "所有文件 (*.*)|*.*";
            }
        }

        /// <summary>保存二进制（base64，可带 data:xxx;base64, 前缀）：解码后走系统「另存为」。</summary>
        private void SaveBinaryFile(string name, string base64, string mime)
        {
            byte[] bytes = DecodeBase64(base64);
            if (bytes == null || bytes.Length == 0)
            {
                MessageBox.Show(this, "内容为空或格式不对", "叶历 DayLeaf",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string safeName = string.IsNullOrEmpty(name) ? "dayleaf.bin" : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
            if (safeName.Length == 0) safeName = "dayleaf.bin";

            if (Path.GetExtension(safeName).Length == 0)
            {
                string fromMime = ExtFromMime(mime);
                if (fromMime.Length > 0) safeName += fromMime;
            }

            using (var dlg = new SaveFileDialog())
            {
                string ext = Path.GetExtension(safeName);
                if (string.IsNullOrEmpty(ext)) ext = ".bin";
                dlg.FileName = safeName;
                dlg.Filter = BuildFilter(ext);
                dlg.AddExtension = true;
                dlg.OverwritePrompt = true;
                dlg.Title = "保存文件";

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    File.WriteAllBytes(dlg.FileName, bytes);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：" + ex.Message, "叶历 DayLeaf",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static byte[] DecodeBase64(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string raw = s.Trim();
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = raw.IndexOf(',');
                if (comma < 0) return null;
                raw = raw.Substring(comma + 1);
            }
            try { return Convert.FromBase64String(raw); }
            catch (Exception) { return null; }
        }

        private static string ExtFromMime(string mime)
        {
            switch ((mime ?? "").Trim().ToLowerInvariant())
            {
                case "image/png": return ".png";
                case "image/jpeg": case "image/jpg": return ".jpg";
                case "image/gif": return ".gif";
                case "image/webp": return ".webp";
                case "application/pdf": return ".pdf";
                case "application/zip": return ".zip";
                case "application/json": return ".json";
                default: return "";
            }
        }

        private void RunUpdateCheck()
        {
            DesktopUpdate.Check(delegate(DesktopUpdate.Result r)
            {
                if (IsDisposed) return;
                try
                {
                    BeginInvoke((Action)delegate { ShowUpdateResult(r); });
                }
                catch (Exception) { /* 窗口已关 */ }
            });
        }

        private void ShowUpdateResult(DesktopUpdate.Result r)
        {
            if (r == null || r.Latest == null)
            {
                MessageBox.Show(this, "检查失败，请检查网络后重试", "检查更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!r.HasNewer)
            {
                MessageBox.Show(this, "已是最新版本 v" + r.Current, "检查更新",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ShowUpdateDialog(r);
        }

        /// <summary>启动后静默检测：不提示「已是最新」，只有新版才问一次。</summary>
        private void StartupUpdateCheck()
        {
            DesktopUpdate.Check(delegate(DesktopUpdate.Result r)
            {
                if (r == null || r.Latest == null || !r.HasNewer) return;

                // 同一个版本只提醒一次，别每次开都弹
                string seen = AppPaths.ReadNotifiedVersion();
                if (seen == r.Latest.VersionName) return;
                AppPaths.WriteNotifiedVersion(r.Latest.VersionName);

                if (IsDisposed) return;
                try
                {
                    BeginInvoke((Action)delegate { ShowStartupUpdate(r); });
                }
                catch (Exception) { /* 窗口已关 */ }
            });
        }

        private void ShowStartupUpdate(DesktopUpdate.Result r)
        {
            ShowUpdateDialog(r);
        }

        /// <summary>软件内更新弹窗：立即下载在应用内完成，下载后拉起安装程序。</summary>
        private void ShowUpdateDialog(DesktopUpdate.Result r)
        {
            try
            {
                var dlg = new UpdateDialog(r.Latest, r.Current, _dark);
                dlg.ShowDialog(this);
            }
            catch (Exception) { /* 弹不出来就不打扰用户 */ }
        }

        /// <summary>
        /// --update-preview：生成深色/浅色/错误态三张弹窗截图，供打包后做视觉回归。
        /// 只在测试参数下运行，正常启动不会走到这里。
        /// </summary>
        private void ShowUpdatePreview()
        {
            try
            {
                var info = new DesktopUpdate.Info
                {
                    VersionName = "1.0.12",
                    VersionCode = 13,
                    Url = "https://cdn.jsdelivr.net/gh/RainxButterfly/DayLeaf@v1.0.12/desktop/backend/DayLeaf-Setup.exe",
                    Size = 723786,
                    Notes = "· 修复部分机型（vivo NEX S）开场层被压成顶栏、点「设置」没有反应的问题。\r\n" +
                            "· 背景图片支持自由取景：拖动、双指缩放，可铺满裁切或完整显示。\r\n" +
                            "· 记事页「完成」按钮移到脚栏，快速选色改为圆形色块。\r\n" +
                            "· 更新下载改为多源自动切换，并校验安装包大小与 SHA-256。\r\n" +
                            "· 优化更新弹窗的排版、按钮与进度显示。\r\n" +
                            "· 其他稳定性与细节优化。"
                };
                info.Urls.Add("https://fastly.jsdelivr.net/gh/RainxButterfly/DayLeaf@v1.0.12/desktop/backend/DayLeaf-Setup.exe");
                info.Urls.Add("https://gcore.jsdelivr.net/gh/RainxButterfly/DayLeaf@v1.0.12/desktop/backend/DayLeaf-Setup.exe");

                var dark = new UpdateDialog(info, "1.0.10", true);
                dark.CaptureAndClose(Path.Combine(AppPaths.Root, "update-preview-dark.png"), 1000);
                dark.ShowDialog(this);

                var light = new UpdateDialog(info, "1.0.10", false);
                light.CaptureAndClose(Path.Combine(AppPaths.Root, "update-preview-light.png"), 1000);
                light.ShowDialog(this);

                var error = new UpdateDialog(info, "1.0.10", true);
                error.ShowPreviewError();
                error.CaptureAndClose(Path.Combine(AppPaths.Root, "update-preview-error.png"), 1000);
                error.ShowDialog(this);

                var longInfo = new DesktopUpdate.Info
                {
                    VersionName = info.VersionName,
                    VersionCode = info.VersionCode,
                    Url = info.Url,
                    Size = info.Size,
                    Sha256 = info.Sha256,
                    Notes = info.Notes + "\r\n· 这是一条用于检查长文案滚动的说明，正常情况下会自动换行。" +
                            "\r\n· 下载过程中会显示进度、已下载大小与总大小。" +
                            "\r\n· 全部下载源失败时会显示友好的错误提示，并把技术细节写入本地日志。"
                };
                longInfo.Urls.AddRange(info.Urls);
                var downloading = new UpdateDialog(longInfo, "1.0.10", true);
                downloading.ShowPreviewProgress(42, "正在下载 42%　·　297 KB / 707 KB");
                downloading.CaptureAndClose(Path.Combine(AppPaths.Root, "update-preview-download.png"), 1000);
                downloading.ShowDialog(this);
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(AppPaths.Root, "update-preview.log"),
                        DateTime.Now.ToString("s") + " " + ex + "\r\n", new UTF8Encoding(false));
                }
                catch { }
            }
            Close();
        }

        /* ------------------------------------------------------------------
         * 导航与外链
         * ---------------------------------------------------------------- */

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            string uri = e.Uri ?? "";
            if (uri.StartsWith("https://" + VirtualHost, StringComparison.OrdinalIgnoreCase)) return;
            if (uri == "about:blank" || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

            // 其余一律交给系统浏览器，应用内不跑外部页面
            e.Cancel = true;
            OpenExternal(uri);
        }

        private void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        }

        private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                MessageBox.Show(this, "界面进程已退出，请重新打开应用。", "叶历 DayLeaf",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>放行网页的 Notification 权限，让提醒能弹出系统通知（点击由网页内 onclick 处理跳转）。</summary>
        private void OnPermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            try
            {
                if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
                {
                    e.State = CoreWebView2PermissionState.Allow;
                }
                else if (e.PermissionKind == CoreWebView2PermissionKind.ClipboardRead)
                {
                    /* 右键菜单里的「粘贴」需要读剪贴板；这是本机应用自己的请求 */
                    e.State = CoreWebView2PermissionState.Allow;
                }
            }
            catch (Exception) { /* 旧运行时没有通知权限枚举就忽略 */ }
        }

        /// <summary>
        /// 拦截网页发起的下载：叶历所有导出都走原生「另存为」，
        /// 不该出现浏览器式的下载条 / 另存为。
        /// </summary>
        private void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                e.Cancel = true;
            }
            catch (Exception) { /* ignore */ }
            try
            {
                if (_web != null && _web.CoreWebView2 != null)
                {
                    _web.CoreWebView2.ExecuteScriptAsync(
                        "window.__dlNotice && window.__dlNotice('请在应用内使用导出/另存为按钮');");
                }
            }
            catch (Exception) { /* ignore */ }
        }

        /// <summary>
        /// --selftest：加载完界面后把关键状态写进 selftest.log 再退出。
        /// 用来在打包后确认「壳 + 网页 + 本地存储」这条链路是通的。
        /// </summary>
        private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            /* 启动更新检测：界面先正常显示，1.5 秒后后台问一次。
               只有真的发现新版本才打扰用户，且同一版本只提醒一次。 */
            if (!_selfTest)
            {
                if (_updatePreview && !_updatePreviewDone)
                {
                    _updatePreviewDone = true;
                    ShowUpdatePreview();
                    return;
                }
                if (e.IsSuccess && !_startupCheckDone)
                {
                    _startupCheckDone = true;
                    await System.Threading.Tasks.Task.Delay(1500);
                    StartupUpdateCheck();
                }
                return;
            }

            if (_selfTestDone) return;
            _selfTestDone = true;

            string log = "navigation_ok=" + e.IsSuccess;
            log += "\r\ndpi=" + DeviceDpi + " form=" + Width + "x" + Height + " web=" + _web.Width + "x" + _web.Height
                 + " scale=" + SystemScale().ToString("0.###") + " screen=" + ScreenWidth() + "x" + ScreenHeight();
            try
            {
                // 等开场动画收场，再取界面状态与截图
                await System.Threading.Tasks.Task.Delay(2400);

                const string probe =
                    "(function(){var o={};o.title=document.title;" +
                    "o.views=document.querySelectorAll('.view').length;" +
                    "o.navs=document.querySelectorAll('[data-nav]').length;" +
                    "o.grid=!!document.getElementById('gridBody');" +
                    "o.viewport=window.innerWidth+'x'+window.innerHeight;" +
                    "o.dpr=window.devicePixelRatio;" +
                    "o.bridge=!!(window.DayLeafNative&&window.DayLeafNative.checkUpdate);" +
                    "try{localStorage.setItem('__probe','1');o.ls=localStorage.getItem('__probe')==='1';localStorage.removeItem('__probe');}catch(e){o.ls=false;}" +
                    "o.bodyText=(document.body.innerText||'').replace(/\\s+/g,' ').slice(0,80);" +
                    "return JSON.stringify(o);})()";

                string raw = await _web.CoreWebView2.ExecuteScriptAsync(probe);
                log += "\r\n" + raw;

                using (var png = File.Create(Path.Combine(AppPaths.Root, "selftest.png")))
                {
                    await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png);
                }
                log += "\r\nscreenshot=selftest.png";

                // 再抓一张表格页（桌面端的主屏）
                await _web.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelector('[data-nav=\"grid\"]').click()");
                await System.Threading.Tasks.Task.Delay(900);
                using (var png2 = File.Create(Path.Combine(AppPaths.Root, "selftest-grid.png")))
                {
                    await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png2);
                }
                log += "\r\nscreenshot=selftest-grid.png";
            }
            catch (Exception ex)
            {
                log += "\r\nprobe_error=" + ex.Message;
            }

            try
            {
                File.WriteAllText(Path.Combine(AppPaths.Root, "selftest.log"), log, new UTF8Encoding(false));
            }
            catch (Exception) { /* ignore */ }

            BeginInvoke((Action)Close);
        }

        private static void OpenExternal(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return;
            bool ok = uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                      || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
            if (!ok) return;

            try
            {
                var psi = new ProcessStartInfo(uri);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception) { /* 没有默认程序就不处理 */ }
        }

        /* ------------------------------------------------------------------
         * 主题
         * ---------------------------------------------------------------- */

        private void ApplyTheme(bool dark)
        {
            _dark = dark;
            Color bg = AppPaths.BgColor(dark);
            BackColor = bg;
            try { _web.DefaultBackgroundColor = bg; }
            catch (Exception) { /* 还没初始化完成 */ }

            if (IsHandleCreated) SetDarkTitleBar(Handle, dark);

            if (_widget != null && !_widget.IsDisposed)
            {
                try { _widget.ApplyTheme(dark); } catch (Exception) { /* ignore */ }
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        /// <summary>屏幕物理像素宽（WinForms 的 Screen 在缩放下给的是逻辑值，不能用）。</summary>
        private static int ScreenWidth()
        {
            try
            {
                int w = GetSystemMetrics(0);
                if (w > 0) return w;
            }
            catch (Exception) { /* ignore */ }
            return Screen.PrimaryScreen.Bounds.Width;
        }

        private static int ScreenHeight()
        {
            try
            {
                int h = GetSystemMetrics(1);
                if (h > 0) return h;
            }
            catch (Exception) { /* ignore */ }
            return Screen.PrimaryScreen.Bounds.Height;
        }

        /// <summary>系统缩放比例（96 DPI 记为 1.0）。</summary>
        private static float SystemScale()
        {
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi >= 96) return dpi / 96f;
            }
            catch (Exception) { /* Win7 等没有这个接口，退回 GDI 读数 */ }

            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float s = g.DpiX / 96f;
                    return s > 0.5f ? s : 1f;
                }
            }
            catch (Exception)
            {
                return 1f;
            }
        }

        /// <summary>让标题栏跟随深色（20 = 20H1+，19 = 更早的预览版）。</summary>
        private static void SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
                }
            }
            catch (Exception) { /* 系统不支持就保持默认 */ }
        }

        /* ------------------------------------------------------------------
         * 窗口位置记忆
         * ---------------------------------------------------------------- */

        private sealed class WindowPlacement
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int W { get; set; }
            public int H { get; set; }
            public bool Max { get; set; }
            public bool Valid { get; set; }
        }

        private void RestoreWindowBounds()
        {
            WindowPlacement st = LoadWindowState();
            if (st == null || !st.Valid) return;

            var rect = new Rectangle(st.X, st.Y, st.W, st.H);
            bool visible = false;
            foreach (Screen sc in Screen.AllScreens)
            {
                if (sc.WorkingArea.IntersectsWith(rect)) { visible = true; break; }
            }
            if (!visible) return;   // 换了显示器就别把窗口放到看不见的地方

            StartPosition = FormStartPosition.Manual;
            Bounds = rect;
            if (st.Max) WindowState = FormWindowState.Maximized;
        }

        private static WindowPlacement LoadWindowState()
        {
            try
            {
                if (!File.Exists(AppPaths.WindowFile)) return null;
                string json = File.ReadAllText(AppPaths.WindowFile);
                var ser = new JavaScriptSerializer();
                return ser.Deserialize<WindowPlacement>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                var st = new WindowPlacement
                {
                    X = r.X, Y = r.Y, W = r.Width, H = r.Height,
                    Max = WindowState == FormWindowState.Maximized,
                    Valid = true
                };
                var ser = new JavaScriptSerializer();
                File.WriteAllText(AppPaths.WindowFile, ser.Serialize(st));
            }
            catch (Exception) { /* 存不下就算了，不影响关闭 */ }

            /* 开了后台提醒：点窗口 X 只是收进托盘，提醒继续 */
            if (_backgroundReminders && !_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!_trayTipShown && _tray != null)
                {
                    _trayTipShown = true;
                    try
                    {
                        _tray.ShowBalloonTip(6000, "叶历仍在后台运行",
                            "日程提醒会继续；右键托盘图标可以重新打开或彻底退出。",
                            ToolTipIcon.Info);
                    }
                    catch (Exception) { /* ignore */ }
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { if (_reminderTimer != null) { _reminderTimer.Stop(); _reminderTimer.Dispose(); } } catch (Exception) { }
            try { if (_catchUpTimer != null) { _catchUpTimer.Stop(); _catchUpTimer.Dispose(); } } catch (Exception) { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch (Exception) { }
            try { if (_widget != null) _widget.CloseForExit(); } catch (Exception) { }
            try { if (_lanServer != null) _lanServer.Dispose(); } catch (Exception) { }
            base.OnFormClosed(e);
        }

        /* ------------------------------------------------------------------
         * 托盘与后台提醒
         * ---------------------------------------------------------------- */

        private void SetupTray()
        {
            try
            {
                Icon ico = null;
                try { ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
                _tray = new NotifyIcon
                {
                    Icon = ico ?? SystemIcons.Application,
                    Text = "叶历 DayLeaf",
                    Visible = true
                };
                var menu = new ContextMenuStrip();

                var open = new ToolStripMenuItem("打开叶历");
                open.Click += delegate { ShowFromTray(); };
                menu.Items.Add(open);

                _snoozeItem = new ToolStripMenuItem("稍后提醒 10 分钟");
                _snoozeItem.Enabled = false;   // 还没有刚响过的提醒
                _snoozeItem.Click += delegate { SnoozeLastReminder(); };
                menu.Items.Add(_snoozeItem);

                _widgetMenuItem = new ToolStripMenuItem("桌面下一项");
                _widgetMenuItem.Click += delegate { ToggleWidget(); SyncWidgetMenuCheck(); };
                menu.Items.Add(_widgetMenuItem);

                menu.Items.Add(new ToolStripSeparator());
                var exit = new ToolStripMenuItem("退出");
                exit.Click += delegate { ExitFromTray(); };
                menu.Items.Add(exit);

                menu.Opening += delegate
                {
                    if (_snoozeItem != null) _snoozeItem.Enabled = _lastFired != null;
                    SyncWidgetMenuCheck();
                };

                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick += delegate { ShowFromTray(); };
                _tray.BalloonTipClicked += delegate { ShowFromTray(); };
            }
            catch (Exception) { /* 托盘失败不影响主界面 */ }
        }

        private void ShowFromTray()
        {
            try
            {
                ShowInTaskbar = true;
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                BringToFront();
            }
            catch (Exception) { /* ignore */ }
        }

        private void ExitFromTray()
        {
            _exiting = true;
            try { _tray.Visible = false; } catch (Exception) { }
            try
            {
                _backgroundReminders = false;
                ApplyReminders("{\"enabled\":false,\"items\":[]}");
            }
            catch (Exception) { }
            Close();
        }

        private void ApplyReminders(string json)
        {
            if (json == null || json.Length == 0) json = "{\"enabled\":false,\"items\":[]}";
            try { File.WriteAllText(ReminderFile, json, new UTF8Encoding(false)); } catch (Exception) { }

            bool enabled = false;
            var items = new List<ReminderItem>();
            try
            {
                var ser = new JavaScriptSerializer();
                var dict = ser.Deserialize<Dictionary<string, object>>(json);
                if (dict != null)
                {
                    object en;
                    if (dict.TryGetValue("enabled", out en)) enabled = Convert.ToBoolean(en);
                    object arrObj;
                    if (dict.TryGetValue("items", out arrObj))
                    {
                        var arr = arrObj as object[];
                        if (arr != null)
                        {
                            foreach (object o in arr)
                            {
                                var d = o as Dictionary<string, object>;
                                if (d == null) continue;
                                var it = new ReminderItem();
                                it.Id = GetStr(d, "id", "");
                                it.At = GetLong(d, "at", 0);
                                it.Title = GetStr(d, "title", "日程提醒");
                                it.Body = GetStr(d, "body", "");
                                it.Kind = GetStr(d, "kind", "rm");
                                if (it.At > 0) items.Add(it);
                            }
                        }
                    }
                }
            }
            catch (Exception) { enabled = false; }

            bool wasEnabled = _backgroundReminders;
            bool firstLoad = !_reminderStateReady;
            _reminderStateReady = true;

            _backgroundReminders = enabled;
            _reminders = items;
            if (enabled) StartReminderTimer();
            else StopReminderTimer();

            /* 后台提醒的开关同步到开机自启；自检 / 预览等测试模式下绝不写注册表 */
            if (!_selfTest && !_updatePreview && (enabled != wasEnabled || firstLoad))
            {
                AutoStart.SetEnabled(enabled);
            }
        }

        private void LoadRemindersFromDisk()
        {
            try
            {
                if (!File.Exists(ReminderFile)) return;
                string json = File.ReadAllText(ReminderFile);
                if (json == null || json.Length == 0) return;
                ApplyReminders(json);
            }
            catch (Exception) { /* ignore */ }
        }

        private void StartReminderTimer()
        {
            if (_reminderTimer == null)
            {
                _reminderTimer = new System.Windows.Forms.Timer();
                _reminderTimer.Interval = 15000;
                _reminderTimer.Tick += delegate { CheckReminders(); };
            }
            _reminderTimer.Start();
            CheckReminders();
        }

        private void StopReminderTimer()
        {
            try { if (_reminderTimer != null) _reminderTimer.Stop(); } catch (Exception) { }
        }

        private void CheckReminders()
        {
            if (!_backgroundReminders || _reminders == null || _tray == null) return;
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            const long catchUpWindow = 12 * 60 * 60 * 1000L;   // 刚开机/刚打开：补 12 小时内错过的
            const long onTimeGrace = 2 * 60 * 1000L;           // 迟到 2 分钟以上标成「补提醒」

            foreach (ReminderItem r in _reminders)
            {
                if (r.At > now || r.At < now - catchUpWindow) continue;
                string key = r.Id + "|" + r.At;
                if (_firedReminders.Contains(key)) continue;
                _firedReminders.Add(key);
                _lastFired = r;
                if (_snoozeItem != null) _snoozeItem.Enabled = true;

                string body = now - r.At > onTimeGrace ? "（补提醒）" + r.Body : r.Body;
                try
                {
                    _tray.Visible = true;
                    _tray.ShowBalloonTip(8000, r.Title, body,
                        r.Kind == "al" ? ToolTipIcon.Warning : ToolTipIcon.Info);
                    if (r.Kind == "al")
                    {
                        try { System.Media.SystemSounds.Exclamation.Play(); } catch (Exception) { }
                    }
                }
                catch (Exception) { /* ignore */ }
            }
        }

        private static string GetStr(Dictionary<string, object> d, string key, string fallback)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                string s = Convert.ToString(v);
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            return fallback;
        }

        private static long GetLong(Dictionary<string, object> d, string key, long fallback)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                long n;
                if (long.TryParse(Convert.ToString(v), out n)) return n;
            }
            return fallback;
        }

        /* ------------------------------------------------------------------
         * 稍后提醒 / 托盘状态 / 桌面小窗
         * ---------------------------------------------------------------- */

        /// <summary>启动后补扫一次：把 12 小时内错过的提醒集中补出来，之后交给 15 秒轮询。</summary>
        private void ScheduleCatchUpPass()
        {
            try
            {
                _catchUpTimer = new System.Windows.Forms.Timer();
                _catchUpTimer.Interval = 2500;
                _catchUpTimer.Tick += delegate
                {
                    try { _catchUpTimer.Stop(); _catchUpTimer.Dispose(); } catch (Exception) { }
                    _catchUpTimer = null;
                    CheckReminders();
                };
                _catchUpTimer.Start();
            }
            catch (Exception) { /* ignore */ }
        }

        /// <summary>把最近响过的一条推迟 10 分钟：追加一条新 id 的提醒并写回 reminders.json。</summary>
        private void SnoozeLastReminder()
        {
            ReminderItem last = _lastFired;
            if (last == null) return;

            string prefix = last.Id + ".sn";
            int seq = 0;
            foreach (ReminderItem r in _reminders)
            {
                if (!string.IsNullOrEmpty(r.Id) && r.Id.StartsWith(prefix, StringComparison.Ordinal)) seq++;
            }

            var snoozed = new ReminderItem
            {
                Id = prefix + (seq + 1),
                At = DateTimeOffset.Now.ToUnixTimeMilliseconds() + 10 * 60 * 1000,
                Title = last.Title,
                Body = last.Body,
                Kind = last.Kind
            };
            _reminders.Add(snoozed);
            PersistReminders();

            try
            {
                if (_tray != null)
                {
                    _tray.Visible = true;
                    _tray.ShowBalloonTip(4000, last.Title, "已设为 10 分钟后再次提醒", ToolTipIcon.Info);
                }
            }
            catch (Exception) { /* ignore */ }
        }

        /// <summary>把当前提醒列表写回 reminders.json（字段与 ApplyReminders 读的一致）。</summary>
        private void PersistReminders()
        {
            try
            {
                var items = new List<Dictionary<string, object>>();
                foreach (ReminderItem r in _reminders)
                {
                    items.Add(new Dictionary<string, object>
                    {
                        { "id", r.Id },
                        { "at", r.At },
                        { "title", r.Title },
                        { "body", r.Body },
                        { "kind", r.Kind }
                    });
                }
                var root = new Dictionary<string, object>
                {
                    { "enabled", _backgroundReminders },
                    { "items", items }
                };
                Directory.CreateDirectory(AppPaths.Root);
                File.WriteAllText(ReminderFile, new JavaScriptSerializer().Serialize(root), new UTF8Encoding(false));
            }
            catch (Exception) { /* 写不下就只在内存里生效 */ }
        }

        /// <summary>托盘悬浮提示：叶历 · 14:00 形策（≤60 字，没有内容就回落到默认名）。</summary>
        private void ApplyTrayStatus(string json)
        {
            if (_tray == null) return;

            string text = "叶历 DayLeaf";
            try
            {
                List<WidgetItem> items = NextUpForm.ParseItems(json);
                if (items.Count > 0)
                {
                    WidgetItem it = items[0];
                    string title = it.Title.Length > 0 ? it.Title : it.Body;
                    string head = (it.Time.Length > 0 ? it.Time + " " + title : title).Trim();
                    if (head.Length > 0) text = "叶历 · " + head;
                }
            }
            catch (Exception) { /* 结构不对就保持默认 */ }

            if (text.Length > 60) text = text.Substring(0, 60);
            try { _tray.Text = text; } catch (Exception) { /* ignore */ }
        }

        /// <summary>网页推送的「下一项」：缓存；小窗开着就顺手刷新（不动焦点）。</summary>
        /* ---------------- 局域网同步 ---------------- */

        private void StartLanSync(string token)
        {
            try
            {
                if (_lanServer == null) _lanServer = new LanSyncServer();
                _lanServer.Incoming -= OnLanIncoming;
                _lanServer.Incoming += OnLanIncoming;
                _lanServer.Touched -= OnLanTouched;
                _lanServer.Touched += OnLanTouched;
                int port;
                string err;
                if (_lanServer.Start(token, out port, out err))
                {
                    ReportLanInfo();
                    InvokeWeb("window.__dlLanStatus && window.__dlLanStatus({running:true,port:" + port + "});");
                }
                else
                {
                    InvokeWeb("window.__dlLanStatus && window.__dlLanStatus({running:false,error:" +
                        new JavaScriptSerializer().Serialize(err ?? "端口被占用") + "});");
                }
            }
            catch (Exception ex)
            {
                InvokeWeb("window.__dlLanStatus && window.__dlLanStatus({running:false,error:" +
                    new JavaScriptSerializer().Serialize(ex.Message) + "});");
            }
        }

        private void StopLanSync()
        {
            try { if (_lanServer != null) _lanServer.Stop(); } catch (Exception) { }
            InvokeWeb("window.__dlLanStatus && window.__dlLanStatus({running:false});");
        }

        private void ReportLanInfo()
        {
            bool running = _lanServer != null && _lanServer.Running;
            int port = running ? _lanServer.Port : 0;
            var ips = LanSyncServer.LocalIPv4();
            var data = new Dictionary<string, object>();
            data["running"] = running;
            data["port"] = port;
            data["token"] = _lanServer != null ? _lanServer.Token : "";
            data["ips"] = ips;
            string json = new JavaScriptSerializer().Serialize(data);
            InvokeWeb("window.__dlLanInfo && window.__dlLanInfo(" + json + ");");
        }

        private void OnLanTouched(string method)
        {
            InvokeWeb("window.__dlLanTouch && window.__dlLanTouch(" +
                new JavaScriptSerializer().Serialize(method ?? "") + ");");
        }

        private void OnLanIncoming(string body)
        {
            InvokeWeb("window.__dlLanIncoming && window.__dlLanIncoming(" +
                new JavaScriptSerializer().Serialize(body) + ");");
        }

        private void InvokeWeb(string js)
        {
            try
            {
                if (_web != null && _web.CoreWebView2 != null)
                    _web.CoreWebView2.ExecuteScriptAsync(js);
            }
            catch (Exception) { /* ignore */ }
        }

        private void ApplyWidget(string json)
        {
            _widgetItems = NextUpForm.ParseItems(json);
            if (_widget != null && !_widget.IsDisposed)
            {
                try { _widget.SetItems(_widgetItems); } catch (Exception) { /* ignore */ }
            }
        }

        private void EnsureWidget()
        {
            if (_widget != null && !_widget.IsDisposed) return;
            try { _widget = new NextUpForm(_dark); }
            catch (Exception) { _widget = null; }
        }

        private void ToggleWidget()
        {
            EnsureWidget();
            if (_widget == null) return;
            SetDesktopWidget(!_widget.WidgetOpen);
        }

        private void SetDesktopWidget(bool on)
        {
            EnsureWidget();
            if (_widget == null) { ReportDesktopWidget(); return; }
            try
            {
                if (on)
                {
                    _widget.SetItems(_widgetItems);
                    _widget.ShowWidget();
                }
                else
                {
                    _widget.HideWidget();
                }
            }
            catch (Exception) { /* ignore */ }
            SyncWidgetMenuCheck();
            ReportDesktopWidget();
        }

        private void ReportDesktopWidget()
        {
            bool open = _widget != null && !_widget.IsDisposed && _widget.WidgetOpen;
            InvokeWeb("window.__dlDesktopWidget && window.__dlDesktopWidget(" + (open ? "true" : "false") + ");");
        }

        private void SyncWidgetMenuCheck()
        {
            if (_widgetMenuItem == null) return;
            bool open = _widget != null && !_widget.IsDisposed && _widget.WidgetOpen;
            try { _widgetMenuItem.Checked = open; } catch (Exception) { /* ignore */ }
        }

        /// <summary>首次显示后：恢复桌面小窗、处理 --tray 静默启动。自检/预览模式不碰。</summary>
        private void OnFirstShown()
        {
            if (_selfTest || _updatePreview) return;

            try
            {
                if (WidgetStore.Load().Open)
                {
                    EnsureWidget();
                    if (_widget != null)
                    {
                        _widget.SetItems(_widgetItems);
                        _widget.ShowWidget();
                    }
                }
            }
            catch (Exception) { /* ignore */ }
            SyncWidgetMenuCheck();

            /* --tray（开机自启）：后台提醒开着时直接收进托盘，不弹主窗口 */
            if (_trayMode && _backgroundReminders)
            {
                try { Hide(); } catch (Exception) { /* ignore */ }
            }
        }

        /* ------------------------------------------------------------------
         * WebDAV 同步：{op:"pull"|"push", url, user, pass, body}
         * 全程后台线程，结束后回主线程调 window.__dlSyncResult(op, ok, data, error)。
         * ---------------------------------------------------------------- */

        private sealed class SyncResult
        {
            public string Op = "pull";
            public bool Ok;
            public string Data = "";
            public string Error = "";
        }

        private void BeginWebDavSync(string configJson)
        {
            if (Interlocked.CompareExchange(ref _webdavBusy, 1, 0) != 0)
            {
                PostSyncResult(new SyncResult { Op = "pull", Ok = false, Error = "同步进行中，请稍后再试" });
                return;
            }

            string cfg = string.IsNullOrEmpty(configJson) ? "{}" : configJson;
            SyncResult result = null;
            var worker = new Thread(new ThreadStart(delegate
            {
                try { result = RunWebDav(cfg); }
                catch (Exception ex)
                {
                    result = new SyncResult { Op = "pull", Ok = false, Error = ex.Message ?? ex.GetType().Name };
                }
                finally { Interlocked.Exchange(ref _webdavBusy, 0); }
                PostSyncResult(result);
            }));
            worker.IsBackground = true;
            worker.Name = "dayleaf-webdav";
            worker.Start();
        }

        private static SyncResult RunWebDav(string json)
        {
            var r = new SyncResult();
            string url = "", user = "", pass = "", body = "";
            try
            {
                var dict = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (dict != null)
                {
                    r.Op = GetStr(dict, "op", "pull") == "push" ? "push" : "pull";
                    url = GetStr(dict, "url", "").Trim();
                    user = GetStr(dict, "user", "");
                    pass = GetStr(dict, "pass", "");
                    body = GetStr(dict, "body", "");
                }
            }
            catch (Exception) { /* 配置坏了按默认处理 */ }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                r.Error = "URL 无效";
                return r;
            }

            HttpWebRequest req = null;
            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 20000;
                req.ReadWriteTimeout = 20000;
                req.AllowAutoRedirect = true;
                req.UserAgent = "DayLeaf-Desktop";
                req.Accept = "application/json, text/plain, */*";
                req.ContentType = "application/json; charset=utf-8";
                req.Method = r.Op == "push" ? "PUT" : "GET";
                if (user.Length > 0 || pass.Length > 0)
                {
                    req.Headers["Authorization"] = "Basic " +
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + pass));
                }

                if (r.Op == "push")
                {
                    byte[] payload = new UTF8Encoding(false).GetBytes(body ?? "");
                    req.ContentLength = payload.Length;
                    using (Stream os = req.GetRequestStream())
                    {
                        os.Write(payload, 0, payload.Length);
                    }
                }

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code >= 200 && code < 300)
                    {
                        r.Ok = true;
                        if (r.Op == "pull") r.Data = ReadLimited(resp.GetResponseStream());
                    }
                    else
                    {
                        r.Error = HttpError(code);
                    }
                }
            }
            catch (WebException we)
            {
                var resp = we.Response as HttpWebResponse;
                int code = resp == null ? 0 : (int)resp.StatusCode;
                if (r.Op == "pull" && code == 404)
                {
                    r.Ok = true;      // 远端还没有文件：当作空的
                    r.Data = "";
                }
                else
                {
                    r.Error = code > 0 ? HttpError(code) : (we.Message ?? we.GetType().Name);
                }
                if (resp != null) resp.Dispose();
            }
            catch (Exception ex)
            {
                r.Error = ex.Message ?? ex.GetType().Name;
            }
            finally
            {
                try { if (req != null) req.Abort(); } catch (Exception) { /* ignore */ }
            }

            return r;
        }

        private static string HttpError(int code)
        {
            if (code == 401) return "认证失败（401）";
            if (code == 403) return "没有权限（403）";
            if (code == 507) return "云端空间不足（507）";
            return "HTTP " + code;
        }

        /// <summary>读取响应正文，超过 8MB 直接失败（和安卓端一致）。</summary>
        private static string ReadLimited(Stream input)
        {
            if (input == null) return "";
            const int MaxBytes = 8 * 1024 * 1024;
            using (var buffer = new MemoryStream())
            {
                byte[] chunk = new byte[16 * 1024];
                int n;
                while ((n = input.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + n > MaxBytes) throw new InvalidOperationException("响应超过 8MB");
                    buffer.Write(chunk, 0, n);
                }
                return new UTF8Encoding(false).GetString(buffer.ToArray());
            }
        }

        private void PostSyncResult(SyncResult r)
        {
            if (r == null) r = new SyncResult();
            if (IsDisposed) return;
            try { BeginInvoke((Action)delegate { RunSyncCallback(r); }); }
            catch (Exception) { /* 窗口已关 */ }
        }

        private void RunSyncCallback(SyncResult r)
        {
            try
            {
                CoreWebView2 core = _web.CoreWebView2;
                if (core == null) return;
                string js = "(function(){try{if(window.__dlSyncResult){window.__dlSyncResult("
                    + QuoteJs(r.Op) + "," + (r.Ok ? "true" : "false") + ","
                    + QuoteJs(r.Data) + "," + QuoteJs(r.Error)
                    + ");}}catch(e){}})();";
                core.ExecuteScriptAsync(js);
            }
            catch (Exception) { /* 页面不可用就算了 */ }
        }

        /// <summary>JS 字符串字面量：JavaScriptSerializer 转义后再补 \u2028/\u2029。</summary>
        private static string QuoteJs(string s)
        {
            string q = new JavaScriptSerializer().Serialize(s ?? "");
            return q.Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
        }

        /// <summary>另一个实例被启动时，把本窗口提到前面。</summary>
        public void ActivateFromOther()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)ActivateFromOther); } catch (Exception) { }
                return;
            }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
        }
    }
}
