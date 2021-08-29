using Microsoft.Win32;
using System;
using System.IO;

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
                    FocusOnUpdate = ((int)key.GetValue(name: "FocusOnUpdate", defaultValue: 1)) != 0;
                    ExitToTray = ((int)key.GetValue(name: "ExitToTray", defaultValue: 0)) != 0;
                    MinimizeToTray = ((int)key.GetValue(name:  "MinimizeToTray", defaultValue: 0)) != 0;
                }

                LaunchAtLogon = File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\tcpTrigger Monitor.lnk");
            }
            catch
            {
                // Something went wrong while retrieving settings.
                // Do nothing, as default values will be used.
            }
        }
    }
}
