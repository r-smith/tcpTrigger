using System;
using System.ServiceProcess;

namespace tcpTrigger
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            var service = new TcpTriggerService();

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
