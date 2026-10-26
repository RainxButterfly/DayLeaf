using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DayLeaf
{
    internal static class Program
    {
        private const string MutexName = "Global\\DayLeaf.Desktop.SingleInstance";
        private const string ActivateEventName = "Global\\DayLeaf.Desktop.Activate";

        [STAThread]
        private static void Main()
        {
            /* 自检/更新预览可以和应用同时跑：用独立的单实例名，避免被正在使用的旧进程弹回 */
            string[] args = Environment.GetCommandLineArgs();
            bool isolated = Array.IndexOf(args, "--selftest") >= 0 || Array.IndexOf(args, "--update-preview") >= 0;
            string mutexName = isolated
                ? "Global\\DayLeaf.Desktop.SingleInstance.Test" + System.Diagnostics.Process.GetCurrentProcess().Id
                : MutexName;
            string eventName = isolated
                ? "Global\\DayLeaf.Desktop.Activate.Test" + System.Diagnostics.Process.GetCurrentProcess().Id
                : ActivateEventName;

            bool createdNew;
            using (var mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已经开着：把可能要分享的文件交给主实例，再叫醒它
                    try
                    {
                        for (int i = 1; i < args.Length; i++)
                        {
                            if (string.IsNullOrEmpty(args[i]) || args[i].StartsWith("--") || !File.Exists(args[i])) continue;
                            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DayLeaf");
                            Directory.CreateDirectory(root);
                            File.WriteAllText(Path.Combine(root, "open-share.txt"), args[i]);
                            break;
                        }
                    }
                    catch (Exception) { /* ignore */ }
                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(eventName))
                        {
                            ev.Set();
                        }
                    }
                    catch (Exception) { /* 打不开就静默退出 */ }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, eventName))
                {
                    var form = new MainForm();
                    RegisteredWaitHandle reg = ThreadPool.RegisterWaitForSingleObject(
                        activate,
                        delegate
                        {
                            if (form != null) form.ActivateFromOther();
                        },
                        null, Timeout.Infinite, false);

                    Application.Run(form);

                    if (reg != null) reg.Unregister(null);
                }
            }
        }
    }
}
