using System.Collections.Generic;

namespace tcpTrigger.GUI.Models
{
    public class NotificationEmail : Notification
    {
        public const int DefaultRateLimitSeconds = 300;
        public const int DefaultWaitSeconds = 30;

        // Filled in for each new email notification.
        public const string DefaultSubject = "Alert: Suspicious network activity from {SOURCE_IP}";
        public const string DefaultBody =
            "The following activity from {SOURCE_IP} was detected:\r\n"
            + "\r\n"
            + "{CONNECTION_LOG}";

        public NotificationEmail()
        {
            Type = NotificationType.Email;
            Name = "Send email";
            RateLimitSeconds = DefaultRateLimitSeconds;
            WaitSeconds = DefaultWaitSeconds;
        }

        public string SmtpServer { get; set; }
        public string Port { get; set; }
        public bool IsStartTlsEnabled { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public List<string> Recipients { get; set; } = new List<string>();
        public string FromAddress { get; set; }
        public string FromDisplayName { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public int RateLimitSeconds { get; set; }
        public int WaitSeconds { get; set; }
    }
}