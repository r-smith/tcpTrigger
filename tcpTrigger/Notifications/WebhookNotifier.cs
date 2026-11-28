using System;
using System.Collections.Generic;
using System.Net;

namespace tcpTrigger
{
    // Sends the messages for webhook notifications.
    internal static class WebhookNotifier
    {
        // Sends the notification's message for a batch of detections. Errors are logged, not thrown.
        public static void Send(NotificationWebhook webhook, IPAddress sourceIp, List<PacketHeader> detections)
        {
            try
            {
                SendMessage(webhook, sourceIp, detections);

                Logger.Write(
                    "A webhook notification was sent."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"Notification: {webhook.Name}{Environment.NewLine}"
                    + $"Platform: {GetPlatformName(webhook.Platform)}{Environment.NewLine}"
                    + $"Server: {GetHost(webhook.Url)}{Environment.NewLine}"
                    + $"Source: {sourceIp}{Environment.NewLine}"
                    + $"Detections: {detections.Count}",
                    Logger.EventCode.WebhookSent);
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    "tcpTrigger failed to send the webhook notification."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"Notification: {webhook.Name}{Environment.NewLine}"
                    + $"Platform: {GetPlatformName(webhook.Platform)}{Environment.NewLine}"
                    + $"Server: {GetHost(webhook.Url)}"
                    + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    Logger.EventCode.WebhookSendFailed);
            }
        }

        // Returns the platform's name, for the event log.
        public static string GetPlatformName(WebhookPlatform platform)
        {
            switch (platform)
            {
                case WebhookPlatform.MicrosoftTeams:
                    return "Microsoft Teams";
                case WebhookPlatform.Slack:
                    return "Slack";
                case WebhookPlatform.Discord:
                    return "Discord";
                case WebhookPlatform.GoogleChat:
                    return "Google Chat";
                case WebhookPlatform.Ntfy:
                    return "ntfy";
                default:
                    return "Custom";
            }
        }

        // Returns the server in the webhook URL, for the event log. The rest of the URL isn't logged, since it
        // usually has the webhook's secret token.
        public static string GetHost(string url)
        {
            return Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri uri) ? uri.Host : "(not a valid URL)";
        }

        // Fills in the variables and sends the message in the platform's format.
        private static void SendMessage(NotificationWebhook webhook, IPAddress sourceIp, List<PacketHeader> detections)
        {
            if (webhook.Platform == WebhookPlatform.Custom)
            {
                // The values are escaped, so they're always valid inside the body's JSON strings.
                string body = UserVariableExpansion.GetExpandedString(webhook.Body ?? string.Empty, sourceIp, detections, escapeJson: true);
                WebhookSender.SendCustom(webhook.Url, body);
                return;
            }

            string title = UserVariableExpansion.GetExpandedString(webhook.Title ?? string.Empty, sourceIp, detections);
            string message = UserVariableExpansion.GetExpandedString(webhook.Message ?? string.Empty, sourceIp, detections);
            switch (webhook.Platform)
            {
                case WebhookPlatform.MicrosoftTeams:
                    WebhookSender.SendTeams(webhook.Url, title, message);
                    break;
                case WebhookPlatform.Slack:
                    WebhookSender.SendSlack(webhook.Url, title, message);
                    break;
                case WebhookPlatform.Discord:
                    WebhookSender.SendDiscord(webhook.Url, title, message);
                    break;
                case WebhookPlatform.GoogleChat:
                    WebhookSender.SendGoogleChat(webhook.Url, title, message);
                    break;
                case WebhookPlatform.Ntfy:
                    WebhookSender.SendNtfy(webhook.Url, title, message);
                    break;
            }
        }
    }
}
