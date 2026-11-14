using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace tcpTrigger.GUI.Services
{
    // The value in the user's Run registry key that launches the GUI when they sign in.
    internal static class StartupEntry
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "tcpTrigger";

        // The Startup folder shortcut made by version 1.3.5 and earlier.
        private const string OldShortcutName = "tcpTrigger Monitor.lnk";

        private static string OldShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), OldShortcutName);

        public static bool Exists()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                return key?.GetValue(ValueName) != null;
            }
        }

        public static void Create()
        {
            string command = $"\"{Assembly.GetExecutingAssembly().Location}\" {CommandLineArgs.Minimized}";
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                key.SetValue(ValueName, command);
            }
        }

        public static void Delete()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
            {
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }

        // If the old shortcut is in the Startup folder, replaces it with the Run registry value,
        // so the user keeps launching at sign-in. Errors are ignored: the shortcut is left as it is.
        public static void ReplaceOldShortcut()
        {
            try
            {
                if (File.Exists(OldShortcutPath))
                {
                    Create();
                    File.Delete(OldShortcutPath);
                }
            }
            catch
            {
                // The registry or the Startup folder couldn't be changed.
            }
        }
    }
}