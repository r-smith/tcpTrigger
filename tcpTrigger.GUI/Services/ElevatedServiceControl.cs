using System;
using System.Threading.Tasks;

namespace tcpTrigger.GUI.Services
{
    // Starts and stops the tcpTrigger service through an elevated copy of this application.
    // Each method returns null if it succeeded or the user declined the UAC prompt. Otherwise
    // it returns why it failed, worded to follow "Unable to start the tcpTrigger service."
    internal static class ElevatedServiceControl
    {
        public static Task<string> StartAsync()
        {
            return RunAsync(CommandLineArgs.StartService);
        }

        public static Task<string> StopAsync()
        {
            return RunAsync(CommandLineArgs.StopService);
        }

        private static async Task<string> RunAsync(string argument)
        {
            try
            {
                ElevatedResult result = await Task.Run(() => ElevatedCommand.Run(argument));
                switch (result)
                {
                    case ElevatedResult.Succeeded:
                    case ElevatedResult.Cancelled:
                        return null;
                    case ElevatedResult.TimedOut:
                        return "The service did not respond in time. Check its status again in a moment.";
                    case ElevatedResult.NotElevated:
                        return "Windows did not give the command administrator rights.";
                    case ElevatedResult.UnknownCommand:
                        return "The service command was not recognized.";
                    default:
                        return "Check the Windows event log for details.";
                }
            }
            catch (Exception ex)
            {
                // The elevated copy couldn't be started.
                return ex.Message;
            }
        }
    }
}