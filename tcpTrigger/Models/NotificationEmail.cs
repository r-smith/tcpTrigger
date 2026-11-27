using System;
using System.Collections.Generic;

namespace tcpTrigger
{
    // Sends an email for each batch of detections.
    internal class NotificationEmail : BatchedNotification
    {
        public const int DefaultRateLimitSeconds = 300;
        public const int DefaultWaitSeconds = 30;

        public NotificationEmail()
        {
            Name = "Send email";
            RateLimitSeconds = DefaultRateLimitSeconds;
            WaitSeconds = DefaultWaitSeconds;
        }

        public EmailSendMethod SendMethod { get; set; }

        // Used when sending with SMTP.
        public string SmtpServer { get; set; }
        public int Port { get; set; }
        public EmailSecurity Security { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        // Used when sending with Microsoft 365.
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public DateTime? ClientSecretExpires { get; set; }

        public List<string> Recipients { get; set; } = new List<string>();
        public string FromAddress { get; set; }
        public string FromDisplayName { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }
}
