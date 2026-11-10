using System;
using System.IO;

namespace tcpTrigger
{
    // Locates the tcpTrigger.xml configuration file.
    internal static class ConfigPaths
    {
        public const string FileName = "tcpTrigger.xml";

        // In the executable's directory. Used if it exists.
        public static string LocalPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

        // In ProgramData. Where a typical installation stores the configuration.
        public static string ProgramDataPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "tcpTrigger",
                FileName);

        // Returns LocalPath if that file exists, otherwise ProgramDataPath.
        // The returned file may not exist.
        public static string GetConfigPath()
        {
            return File.Exists(LocalPath) ? LocalPath : ProgramDataPath;
        }
    }
}