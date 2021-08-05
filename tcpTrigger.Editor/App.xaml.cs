using System;
using System.ServiceProcess;
using System.Windows;

namespace tcpTrigger.Editor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (e.Args.Length == 1 && e.Args[0].Equals("--restart-service"))
            {
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
                            Current.Shutdown(EXIT_FAILURE);
                        }
                    }
                    // Success. Shutdown app.
                    Current.Shutdown(EXIT_SUCCESS);
                }
                catch
                {
                    // Encountered a problem while restarting service. Shutdown app with failure exit code.
                    Current.Shutdown(EXIT_FAILURE);
                }
            }
        }
    }
}
