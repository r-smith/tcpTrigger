namespace tcpTrigger
{
    // Posts a message to a webhook for each batch of detections.
    internal class NotificationWebhook : BatchedNotification
    {
        public const int DefaultRateLimitSeconds = 300;
        public const int DefaultWaitSeconds = 30;

        public NotificationWebhook()
        {
            Name = "Send webhook";
            RateLimitSeconds = DefaultRateLimitSeconds;
            WaitSeconds = DefaultWaitSeconds;
        }

        public WebhookPlatform Platform { get; set; }
        public string Url { get; set; }

        // Used by every platform except Custom. The title is optional.
        public string Title { get; set; }
        public string Message { get; set; }

        // Used by the Custom platform: the JSON request body.
        public string Body { get; set; }
    }
}
