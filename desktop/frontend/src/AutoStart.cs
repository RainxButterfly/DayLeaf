using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DayLeaf
{
    /// <summary>
    /// 开机自启：只在当前用户的 Run 项里写一条 "DayLeaf" = "&lt;exe&gt;" --tray。
    /// 不碰安装器、不写系统目录，卸载时清掉这条就够了。
    /// </summary>
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DayLeaf";

        public static void SetEnabled(bool enabled)
        {
            try
            {
                if (enabled) Enable();
                else Disable();
            }
            catch (Exception) { /* 注册表被策略锁住也不影响主流程 */ }
        }

        private static void Enable()
        {
            string exe = Application.ExecutablePath;
            if (string.IsNullOrEmpty(exe)) return;

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null) return;
                key.SetValue(ValueName, "\"" + exe + "\" --tray", RegistryValueKind.String);
            }
        }

        private static void Disable()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null) return;
                object cur = key.GetValue(ValueName);
                if (cur != null) key.DeleteValue(ValueName, false);
            }
        }
    }
}
