namespace tcpTrigger.GUI
{
    // Command line arguments the application understands.
    // The service and config arguments are used when the GUI relaunches itself
    // elevated (see ElevatedCommand). That copy does the one task and exits with
    // one of the exit codes below instead of showing a window.
    internal static class CommandLineArgs
    {
        public const string StartService = "--start-service";
        public const string StopService = "--stop-service";
        public const string SaveConfig = "--save-config"; // Followed by the path of a config file to copy from.
        public const string Minimized = "--minimized";

        public const int ExitSuccess = 0;
        public const int ExitFailure = 1;
        public const int ExitNotElevated = 2;
        public const int ExitUnknownCommand = 3;
        public const int ExitReloadFailed = 4; // SaveConfig saved the file, but the service failed to reload it.
    }
}