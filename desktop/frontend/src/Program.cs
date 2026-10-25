using System;
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
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已经开着：叫醒它把窗口提到前面，本进程直接退出
                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(ActivateEventName))
                        {
                            ev.Set();
                        }
                    }
                    catch (Exception) { /* 打不开就静默退出 */ }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName))
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
