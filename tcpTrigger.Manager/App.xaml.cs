using System;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Windows;

namespace tcpTrigger.Manager
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
            const string _serviceName = "tcpTrigger";
            const int _timeoutSeconds = 15;
            const int EXIT_SUCCESS = 0;
            const int EXIT_FAILURE = 1;

            // Custom service command that tells the service to reload its configuration file.
            // Must match TcpTriggerService.ReloadConfigurationCommand in the tcpTrigger service.
            const int _reloadConfigurationCommand = 128;

            // How long to watch for the service stopping after a reload, which means the reload failed.
            const int _reloadWaitSeconds = 3;

            // Process command line arguments.
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length == 2 && args[1].Equals("--restart-service"))
            {
                // Restart service then exit.
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
            else if (args.Length == 2 && args[1].Equals("--apply-settings"))
            {
                // Apply new settings then exit. A running service reloads its configuration;
                // a stopped service is started.
                try
                {
                    using (ServiceController sc = new ServiceController(_serviceName))
                    {
                        if (sc.Status == ServiceControllerStatus.Running)
                        {
                            // Service is running. Tell it to reload its configuration.
                            sc.ExecuteCommand(_reloadConfigurationCommand);

                            // The service reloads in the background and stops if the reload fails.
                            // Wait briefly to see if it stops.
                            try
                            {
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(_reloadWaitSeconds));

                                // The service stopped, so the reload failed. Shutdown app with failure exit code.
                                Environment.Exit(EXIT_FAILURE);
                            }
                            catch (System.ServiceProcess.TimeoutException)
                            {
                                // The service is still running, so the reload succeeded.
                            }
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
                    // Encountered a problem while applying settings. Shutdown app with failure exit code.
                    Environment.Exit(EXIT_FAILURE);
                }
            }
            else if (args.Length == 2 && args[1].Equals("--stop-service"))
            {
                // Stop the service then exit.
                try
                {
                    using (ServiceController sc = new ServiceController(_serviceName))
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(_timeoutSeconds));
                        Environment.Exit(EXIT_SUCCESS);
                    }
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
            public static readonly int WM_SHOWME = RegisterWindowMessage("WM_SHOW_TCPTRIGGER_MANAGER");
            [DllImport("user32")]
            public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
            [DllImport("user32")]
            public static extern int RegisterWindowMessage(string message);
        }
    }
}
