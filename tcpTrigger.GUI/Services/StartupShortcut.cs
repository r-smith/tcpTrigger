using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using tcpTrigger.GUI.Interop;

namespace tcpTrigger.GUI.Services
{
    // The shortcut in the user's Startup folder that launches the GUI at logon.
    internal static class StartupShortcut
    {
        private const string FileName = "tcpTrigger.lnk";
        private const string Description = "GUI for tcpTrigger";

        private static string ShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), FileName);

        public static bool Exists()
        {
            return File.Exists(ShortcutPath);
        }

        public static void Create()
        {
            IShellLink link = (IShellLink)new ShellLink();
            link.SetDescription(Description);
            link.SetPath(Assembly.GetExecutingAssembly().Location);
            link.SetArguments(CommandLineArgs.Minimized);

            IPersistFile file = (IPersistFile)link;
            file.Save(ShortcutPath, false);
        }

        public static void Delete()
        {
            if (File.Exists(ShortcutPath))
            {
                File.Delete(ShortcutPath);
            }
        }
    }
}