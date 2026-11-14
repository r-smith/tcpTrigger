using System;
using System.Globalization;
using Microsoft.Win32;

namespace tcpTrigger.GUI.Services
{
    // Per-user GUI preferences, stored in HKCU\SOFTWARE\tcpTrigger.
    // Setting a property changes the current session only. The Set* methods also save to the registry.
    internal static class UserPreferences
    {
        private const string KeyPath = @"SOFTWARE\tcpTrigger";
        private const string FullKeyPath = @"HKEY_CURRENT_USER\" + KeyPath;

        // Used on first run, and for any preference that hasn't been saved yet.
        private const bool DefaultExitToTray = true;
        private const bool DefaultMinimizeToTray = false;
        private const bool DefaultFocusOnUpdate = true;
        private const bool DefaultDetailedView = false;

        public static bool ExitToTray { get; set; } = DefaultExitToTray;
        public static bool MinimizeToTray { get; set; } = DefaultMinimizeToTray;
        public static bool FocusOnUpdate { get; set; } = DefaultFocusOnUpdate;
        public static bool DetailedView { get; set; } = DefaultDetailedView;
        public static bool LaunchAtLogon { get; set; } = false;
        public static int EventLogMaxDays { get; set; } = 0;
        public static string EventLogFromDate { get; set; } = string.Empty;

        public static void Load()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    // The key doesn't exist until a preference is saved for the first time.
                    if (key != null)
                    {
                        FocusOnUpdate = ReadFlag(key, "FocusOnUpdate", DefaultFocusOnUpdate);
                        ExitToTray = ReadFlag(key, "ExitToTray", DefaultExitToTray);
                        MinimizeToTray = ReadFlag(key, "MinimizeToTray", DefaultMinimizeToTray);
                        DetailedView = ReadFlag(key, "DetailedView", DefaultDetailedView);
                        EventLogMaxDays = key.GetValue("EventLog_MaxDays") is int maxDays ? maxDays : 0;
                        EventLogFromDate = key.GetValue("EventLog_FromDate") as string ?? string.Empty;
                    }
                }
            }
            catch
            {
                // The registry couldn't be read. Defaults are used.
            }

            if (!DateTime.TryParseExact(EventLogFromDate, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                EventLogFromDate = string.Empty;
            }

            // Launch at sign-in is stored in the Run registry key, so it's read separately.
            StartupEntry.ReplaceOldShortcut();
            try
            {
                LaunchAtLogon = StartupEntry.Exists();
            }
            catch
            {
                // The Run key couldn't be read. Launch at sign-in shows as off.
            }
        }

        public static void SetExitToTray(bool value)
        {
            ExitToTray = value;
            WriteFlag("ExitToTray", value);
        }

        public static void SetMinimizeToTray(bool value)
        {
            MinimizeToTray = value;
            WriteFlag("MinimizeToTray", value);
        }

        public static void SetFocusOnUpdate(bool value)
        {
            FocusOnUpdate = value;
            WriteFlag("FocusOnUpdate", value);
        }

        public static void SetDetailedView(bool value)
        {
            DetailedView = value;
            WriteFlag("DetailedView", value);
        }

        // Returns defaultValue if the value is missing or isn't a DWORD.
        private static bool ReadFlag(RegistryKey key, string valueName, bool defaultValue)
        {
            return key.GetValue(valueName) is int value ? value != 0 : defaultValue;
        }

        private static void WriteFlag(string valueName, bool value)
        {
            try
            {
                Registry.SetValue(FullKeyPath, valueName, value ? 1 : 0, RegistryValueKind.DWord);
            }
            catch
            {
                // Failed to store setting in registry. Setting is still applied to current instance of application.
            }
        }
    }
}