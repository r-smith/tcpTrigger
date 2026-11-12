using System;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using tcpTrigger.GUI.Configuration;
using tcpTrigger.GUI.Interop;
using tcpTrigger.GUI.Services;

namespace tcpTrigger.GUI
{
    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            // Arguments starting with "--" are administrator tasks. The GUI launches an
            // elevated copy of itself to run one, and that copy exits without showing a window.
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1].StartsWith("--"))
            {
                Environment.Exit(RunAdministratorTask(args));
            }

            // Allow one instance per user.
            using (Mutex mutex = new Mutex(true, GetMutexName(), out bool createdNew))
            {
                if (createdNew)
                {
                    App app = new App();
                    app.InitializeComponent();
                    app.Run();
                    mutex.ReleaseMutex();
                }
                else
                {
                    // Application already running. Ask the running instance to show its window.
                    WindowInterop.BroadcastShowMainWindow();
                }
            }
        }

        // Returns one of the CommandLineArgs exit codes.
        private static int RunAdministratorTask(string[] args)
        {
            if (!IsElevated())
            {
                return CommandLineArgs.ExitNotElevated;
            }

            switch (args[1])
            {
                case CommandLineArgs.StartService:
                    return ToExitCode(TcpTriggerService.Start());
                case CommandLineArgs.StopService:
                    return ToExitCode(TcpTriggerService.Stop());
                case CommandLineArgs.SaveConfig:
                    return args.Length > 2 ? SaveConfigFrom(args[2]) : CommandLineArgs.ExitFailure;
                default:
                    return CommandLineArgs.ExitUnknownCommand;
            }
        }

        // True if this process is running with administrator rights. An administrator
        // account that hasn't been elevated by UAC returns false.
        private static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        // Runs in the elevated copy. Reads the config file the GUI wrote and saves it to the
        // real location, then tells the service to reload it. Re-reading it means only a valid
        // tcpTrigger config can be written, and the destination is never taken from the command line.
        // Returns ExitSuccess, ExitFailure if the file couldn't be saved, or ExitReloadFailed
        // if it was saved but the service failed to reload it.
        private static int SaveConfigFrom(string sourcePath)
        {
            try
            {
                ServiceConfig config = ConfigReader.Load(sourcePath);
                ConfigWriter.Save(config, ConfigPaths.GetConfigPath());
            }
            catch
            {
                return CommandLineArgs.ExitFailure;
            }

            return TcpTriggerService.Reload() ? CommandLineArgs.ExitSuccess : CommandLineArgs.ExitReloadFailed;
        }

        private static int ToExitCode(bool succeeded)
        {
            return succeeded ? CommandLineArgs.ExitSuccess : CommandLineArgs.ExitFailure;
        }

        private static string GetMutexName()
        {
            string sanitizedUsername = new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray());
            sanitizedUsername = sanitizedUsername.Substring(0, Math.Min(100, sanitizedUsername.Length));
            return "tcpTrigger:" + sanitizedUsername + ":{65DF4C9E-2994-455A-9A79-6E0B8A8A670E}";
        }
    }
}