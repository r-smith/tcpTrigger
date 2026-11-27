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
                string title = UserVariableExpansion.GetExpandedString(webhook.Title ?? string.Empty, sourceIp, detections);
                string message = UserVariableExpansion.GetExpandedString(webhook.Message ?? string.Empty, sourceIp, detections);
                SendMessage(webhook, title, message);

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

        private static void SendMessage(NotificationWebhook webhook, string title, string message)
        {
            switch (webhook.Platform)
            {
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
                default:
                    throw new NotSupportedException($"Sending to {GetPlatformName(webhook.Platform)} isn't supported yet.");
            }
        }
    }
}
