using Microsoft.Win32;

namespace tcpTrigger.Monitor
{
    public static class Settings
    {
        public static bool ExitToTray { get; set; } = false;
        public static bool MinimizeToTray { get; set; } = false;
        public static bool FocusOnUpdate { get; set; } = true;
        public static bool LaunchAtLogon { get; set; } = false;

        public static void Load()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\tcpTrigger"))
                {
                    FocusOnUpdate = ((int)key.GetValue("FocusOnUpdate", 1)) != 0;
                    ExitToTray = ((int)key.GetValue("ExitToTray", 0)) != 0;
                    MinimizeToTray = ((int)key.GetValue("MinimizeToTray", 0)) != 0;
                }
            }
            catch
            {
                // Something went wrong when reading settings from registry.
                // Do nothing, as default values will be used.
            }
        }
    }
}
