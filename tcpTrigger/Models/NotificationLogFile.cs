namespace tcpTrigger
{
    // Writes each detection to a log file.
    internal class NotificationLogFile : Notification
    {
        public NotificationLogFile()
        {
            Name = "Write to log file";
        }

        public string Path { get; set; }

        // Set when the log file can't be rotated. No more detections are written to it.
        public bool HasFailed { get; set; }
    }
}