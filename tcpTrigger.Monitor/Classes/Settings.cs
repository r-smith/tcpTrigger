using Microsoft.Win32;
using System;
using System.Globalization;
using System.IO;

namespace tcpTrigger.Monitor
{
    public static class Settings
    {
        public static bool ExitToTray { get; set; } = false;
        public static bool MinimizeToTray { get; set; } = false;
        public static bool FocusOnUpdate { get; set; } = true;
        public static bool LaunchAtLogon { get; set; } = false;
        public static int EventLog_MaxDays { get; set; } = 0;
        public static string EventLog_FromDate { get; set; } = string.Empty;

        public static void Load()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\tcpTrigger"))
                {
                    FocusOnUpdate = ((int)key.GetValue(name: "FocusOnUpdate", defaultValue: 1)) != 0;
                    ExitToTray = ((int)key.GetValue(name: "ExitToTray", defaultValue: 0)) != 0;
                    MinimizeToTray = ((int)key.GetValue(name:  "MinimizeToTray", defaultValue: 0)) != 0;
                    EventLog_MaxDays = (int)key.GetValue(name: "EventLog_MaxDays", defaultValue: 0);
                    EventLog_FromDate = (string)key.GetValue(name: "EventLog_FromDate", defaultValue: string.Empty);
                }

                if (!DateTime.TryParseExact(EventLog_FromDate, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    EventLog_FromDate = string.Empty;
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
