using System.Collections.Generic;

namespace tcpTrigger
{
    // Sends an email for detections. Detections are collected for the wait time and sent together,
    // at most once per rate limit.
    internal class NotificationEmail : Notification
    {
        public const int DefaultRateLimitSeconds = 300;
        public const int DefaultWaitSeconds = 30;

        public NotificationEmail()
        {
            Name = "Send email";
            RateLimitSeconds = DefaultRateLimitSeconds;
            WaitSeconds = DefaultWaitSeconds;
        }

        public string SmtpServer { get; set; }
        public int Port { get; set; }
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