using System;
using System.IO;
using Microsoft.Win32;

namespace DayLeaf
{
    /// <summary>桌面端的数据与配置位置（全部放在用户本地目录，不写注册表以外的地方）。</summary>
    internal static class AppPaths
    {
        public static readonly string Root =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DayLeaf");

        public static string WebViewData { get { return Path.Combine(Root, "webview"); } }
        public static string WwwDir { get { return Path.Combine(Root, "www"); } }
        public static string ThemeFile { get { return Path.Combine(Root, "theme.txt"); } }
        public static string WindowFile { get { return Path.Combine(Root, "window.json"); } }
        public static string NotifiedFile { get { return Path.Combine(Root, "update-notified.txt"); } }
        public static string UpdateDir { get { return Path.Combine(Root, "update"); } }
        public static string WidgetFile { get { return Path.Combine(Root, "widget.json"); } }

        public static void Ensure()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(WwwDir);
            Directory.CreateDirectory(WebViewData);
            Directory.CreateDirectory(UpdateDir);
        }

        /* ---------- 主题偏好（与网页共用同一个值：light / dark / system） ---------- */

        public static string ReadTheme()
        {
            try
            {
                if (File.Exists(ThemeFile))
                {
                    string t = File.ReadAllText(ThemeFile).Trim();
                    if (t == "light" || t == "dark" || t == "system") return t;
                }
            }
            catch (Exception) { /* 读不到按浅色 */ }
            return "light";
        }

        public static void WriteTheme(string mode)
        {
            try
            {
                if (mode != "light" && mode != "dark" && mode != "system") mode = "light";
                Directory.CreateDirectory(Root);
                File.WriteAllText(ThemeFile, mode);
            }
            catch (Exception) { /* ignore */ }
        }

        /// <summary>把 light/dark/system 解析成「现在是不是深色」。</summary>
        public static bool IsDark(string mode)
        {
            if (mode == "dark") return true;
            if (mode != "system") return false;

            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AppsUseLightTheme");
                        if (v is int) return (int)v == 0;
                    }
                }
            }
            catch (Exception) { /* ignore */ }
            return false;
        }

        public static bool IsDarkNow()
        {
            return IsDark(ReadTheme());
        }

        /* ---------- 更新提醒记录（同一版本只提示一次） ---------- */

        public static string ReadNotifiedVersion()
        {
            try
            {
                if (File.Exists(NotifiedFile)) return File.ReadAllText(NotifiedFile).Trim();
            }
            catch (Exception) { /* ignore */ }
            return "";
        }

        public static void WriteNotifiedVersion(string version)
        {
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(NotifiedFile, version ?? "");
            }
            catch (Exception) { /* ignore */ }
        }

        /* ---------- 窗口尺寸 ---------- */

        public static readonly System.Drawing.Color LightBg = System.Drawing.Color.FromArgb(0xEC, 0xEF, 0xF6);
        public static readonly System.Drawing.Color DarkBg = System.Drawing.Color.FromArgb(0x0E, 0x11, 0x18);

        public static System.Drawing.Color BgColor(bool dark)
        {
            return dark ? DarkBg : LightBg;
        }
    }
}
