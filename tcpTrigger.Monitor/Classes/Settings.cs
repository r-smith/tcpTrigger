namespace tcpTrigger.Monitor
{
    public static class Settings
    {
        public static bool ExitToTray { get; private set; } = false;
        public static bool MinimizeToTray { get; private set; } = false;
        public static bool FocusOnUpdate { get; private set; } = true;
        public static bool LaunchAtLogon { get; private set; } = false;
    }
}
