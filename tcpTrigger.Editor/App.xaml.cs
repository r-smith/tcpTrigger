using System;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Windows;

namespace tcpTrigger.Editor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly Mutex mutex = new Mutex(true, "{9BBAF0C1-1D8A-4EED-BEAC-EFF782CCE025}");
        [STAThread]
        public static void Main()
        {
            // Process command line arguments.
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length == 2 && args[1].Equals("--restart-service"))
            {
                // Restart service then exit.
                const string _serviceName = "tcpTrigger";
                const int _timeoutSeconds = 15;
                const int EXIT_SUCCESS = 0;
                const int EXIT_FAILURE = 1;
                try
                {
                    using (ServiceController sc = new ServiceController(_serviceName))
                    {
                        if (sc.Status == ServiceControllerStatus.Running)
                        {
                            // Service is running. Stop then start it.
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(_timeoutSeconds));
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(_timeoutSeconds));
                        }
                        else if (sc.Status == ServiceControllerStatus.Stopped)
                        {
                            // Service is stopped. Start it.
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(_timeoutSeconds));
                        }
                        else
                        {
                            // Service is in some other state. Shutdown app with failure exit code.
                            Environment.Exit(EXIT_FAILURE);
                        }
                    }
                    // Success. Shutdown app.
                    Environment.Exit(EXIT_SUCCESS);
                }
                catch
                {
                    // Encountered a problem while restarting service. Shutdown app with failure exit code.
                    Environment.Exit(EXIT_FAILURE);
                }
            }

            // Check if another instance of this application is already running.
            if (mutex.WaitOne(TimeSpan.Zero, true))
            {
                // Launch application.
                App app = new App();
                app.InitializeComponent();
                app.Run();
                mutex.ReleaseMutex();
            }
            else
            {
                // Application already running.
                // Send window message to notify currently running instance.
                NativeMethods.PostMessage(
                    (IntPtr)NativeMethods.HWND_BROADCAST,
                    NativeMethods.WM_SHOWME,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
        }

        internal static class NativeMethods
        {
            public const int HWND_BROADCAST = 0xffff;
            public static readonly int WM_SHOWME = RegisterWindowMessage("WM_SHOWME");
            [DllImport("user32")]
            public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
            [DllImport("user32")]
            public static extern int RegisterWindowMessage(string message);
        }
    }
}
