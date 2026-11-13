using System;
using System.Threading;
using System.Windows.Forms;

namespace MonitorFader
{
    internal static class Program
    {
        private const string MutexName = @"Local\MonitorFader_SingleInstance_Mutex";
        private const string ShowEventName = @"Local\MonitorFader_ShowSettings_Event";

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Signal existing instance to bring settings window to front
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(ShowEventName))
                        {
                            ev.Set();
                        }
                    }
                    catch
                    {
                        // Ignore if event isn't open
                    }
                    return;
                }

                NativeMethods.EnablePerMonitorDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool startMinimized = false;
                if (args != null)
                {
                    foreach (string arg in args)
                    {
                        if (string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase))
                            startMinimized = true;
                    }
                }

                AppSettings settings = AppSettings.Load();
                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                using (var form = new SettingsForm(settings, startMinimized))
                {
                    var watcherThread = new Thread(() =>
                    {
                        while (!form.IsDisposed)
                        {
                            if (showEvent.WaitOne(500))
                            {
                                if (!form.IsDisposed)
                                {
                                    try
                                    {
                                        form.BeginInvoke(new Action(() => form.ShowFromTray()));
                                    }
                                    catch
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    });
                    watcherThread.IsBackground = true;
                    watcherThread.Start();

                    Application.Run(form);
                }
            }
        }
    }
}
