using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace tcpTrigger.GUI.Services
{
    internal enum ElevatedResult
    {
        Succeeded,
        Failed,
        Cancelled,
        TimedOut,
        NotElevated,    // The copy started, but Windows didn't give it administrator rights.
        UnknownCommand, // The copy didn't recognize the argument.
        ReloadFailed    // The configuration was saved, but the service failed to reload it.
    }

    // Runs a command that needs administrator rights by launching an elevated copy of
    // this application with one of the CommandLineArgs arguments. Program.Main handles
    // the argument in the elevated copy and exits without showing a window.
    internal static class ElevatedCommand
    {
        // Win32 error code returned when the user declines the UAC prompt.
        private const int ErrorCancelled = 1223;

        // The elevated copy gives up on its own well before this (service operations
        // time out after 15 seconds), so this only needs to be longer than that.
        private const int TimeoutMilliseconds = 45 * 1000;

        // Blocks until the elevated copy exits, so call it from a background thread.
        // Throws Win32Exception if the elevated copy couldn't be started for any
        // reason other than the user declining the UAC prompt.
        public static ElevatedResult Run(string arguments)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                Verb = "runas",
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = Assembly.GetExecutingAssembly().Location,
                Arguments = arguments
            };

            try
            {
                using (Process process = Process.Start(processInfo))
                {
                    if (!process.WaitForExit(TimeoutMilliseconds))
                    {
                        // Stop waiting, but don't try to kill it: this process isn't elevated, so it
                        // can't reliably terminate the elevated copy. The copy exits on its own.
                        return ElevatedResult.TimedOut;
                    }

                    switch (process.ExitCode)
                    {
                        case CommandLineArgs.ExitSuccess:
                            return ElevatedResult.Succeeded;
                        case CommandLineArgs.ExitNotElevated:
                            return ElevatedResult.NotElevated;
                        case CommandLineArgs.ExitUnknownCommand:
                            return ElevatedResult.UnknownCommand;
                        case CommandLineArgs.ExitReloadFailed:
                            return ElevatedResult.ReloadFailed;
                        default:
                            return ElevatedResult.Failed;
                    }
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return ElevatedResult.Cancelled;
            }
        }
    }
}