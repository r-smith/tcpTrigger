using System;
using System.ServiceProcess;

namespace tcpTrigger.GUI.Services
{
    // Queries and controls the tcpTrigger Windows service.
    internal static class TcpTriggerService
    {
        public const string ServiceName = "tcpTrigger";

        // Custom service command that tells the service to reload its configuration file.
        // Must match TcpTriggerService.ReloadConfigurationCommand in the tcpTrigger service.
        private const int ReloadConfigurationCommand = 128;

        private static readonly TimeSpan s_serviceTimeout = TimeSpan.FromSeconds(15);

        // How long to watch for the service stopping after a reload, which means the reload failed.
        private static readonly TimeSpan s_reloadFailureWait = TimeSpan.FromSeconds(3);

        // Throws InvalidOperationException if the service isn't installed.
        public static ServiceControllerStatus GetStatus()
        {
            using (ServiceController service = new ServiceController(ServiceName))
            {
                return service.Status;
            }
        }

        // Start and Stop require administrator rights.
        // Each returns true if the service ends up in the requested state.
        public static bool Start()
        {
            try
            {
                using (ServiceController service = new ServiceController(ServiceName))
                {
                    if (service.Status == ServiceControllerStatus.Running)
                    {
                        return true;
                    }
                    else if (service.Status == ServiceControllerStatus.Stopped)
                    {
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running, s_serviceTimeout);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool Stop()
        {
            try
            {
                using (ServiceController service = new ServiceController(ServiceName))
                {
                    if (service.Status == ServiceControllerStatus.Stopped)
                    {
                        return true;
                    }
                    else if (service.Status == ServiceControllerStatus.Running)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, s_serviceTimeout);
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        // Tells a running service to reload its configuration file. Blocks for a few seconds
        // while watching for a failed reload, so call it from a background thread.
        // Returns true if the service reloaded, or if it isn't running or isn't installed, since
        // there's nothing to reload. Returns false if the command couldn't be sent, or if the
        // service stopped, which it does when the new configuration can't be loaded.
        public static bool Reload()
        {
            try
            {
                using (ServiceController service = new ServiceController(ServiceName))
                {
                    ServiceControllerStatus status;
                    try
                    {
                        status = service.Status;
                    }
                    catch (InvalidOperationException)
                    {
                        // The service isn't installed.
                        return true;
                    }

                    if (status != ServiceControllerStatus.Running)
                    {
                        // A stopped service reads the configuration when it starts.
                        return true;
                    }

                    service.ExecuteCommand(ReloadConfigurationCommand);

                    try
                    {
                        service.WaitForStatus(ServiceControllerStatus.Stopped, s_reloadFailureWait);
                        return false;
                    }
                    catch (System.ServiceProcess.TimeoutException)
                    {
                        // Still running, so the reload succeeded.
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}