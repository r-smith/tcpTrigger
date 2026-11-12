namespace tcpTrigger.GUI.Models
{
    public class NotificationCommand : Notification
    {
        public NotificationCommand()
        {
            Type = NotificationType.Command;
            Name = "Run command";
        }

        public string Path { get; set; }
        public string Arguments { get; set; }
    }
}
