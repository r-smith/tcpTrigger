namespace tcpTrigger.GUI.Models
{
    public class NotificationLogFile : Notification
    {
        public NotificationLogFile()
        {
            Type = NotificationType.LogFile;
            Name = "Write to log file";
        }

        public string Path { get; set; }
    }
}