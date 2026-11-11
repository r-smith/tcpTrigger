using System;
using System.ServiceProcess;

namespace tcpTrigger
{
    internal static class Program
    {
        // The main entry point.
        // Runs as a Windows service, or in a console window when started directly.
        static void Main()
        {
            TcpTriggerService service = new TcpTriggerService();

            if (Environment.UserInteractive)
            {
                service.RunInConsole();
            }
            else
            {
                ServiceBase.Run(service);
            }
        }
    }
}