using System.IO;

namespace tcpTrigger.GUI.Validation
{
    // Validation for file path fields.
    internal static class PathInput
    {
        // True if the path is well formed and ends in a file name. The file doesn't have to exist.
        public static bool IsValidFilePath(string path)
        {
            try
            {
                Path.GetFullPath(path);
                return !string.IsNullOrWhiteSpace(Path.GetFileName(path));
            }
            catch
            {
                return false;
            }
        }
    }
}