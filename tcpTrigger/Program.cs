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
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new tcpTrigger()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}
