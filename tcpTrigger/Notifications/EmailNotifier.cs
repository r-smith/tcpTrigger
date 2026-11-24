using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Mail;

namespace tcpTrigger
{
    // Sends the emails for email notifications.
    internal static class EmailNotifier
    {
        // Sends the notification's email for a batch of detections. Errors are logged, not thrown.
        public static void Send(NotificationEmail email, IPAddress sourceIp, List<PacketHeader> detections)
        {
            // Where the email is sent through, for the log.
            string route = email.SendMethod == EmailSendMethod.Microsoft365
                ? "Microsoft 365"
                : $"{email.SmtpServer}:{email.Port}";

            try
            {
                using (MailMessage message = CreateMessage(email))
                {
                    message.Subject = UserVariableExpansion.GetExpandedString(email.Subject, sourceIp, detections);
                    message.Body = UserVariableExpansion.GetExpandedString(email.Body ?? string.Empty, sourceIp, detections);
                    List<string> rejected = SendMessage(email, message);

                    Logger.Write(
                        $"An email notification was sent through {route}."
                        + $"{Environment.NewLine}{Environment.NewLine}"
                        + $"Notification: {email.Name}{Environment.NewLine}"
                        + $"Source: {sourceIp}{Environment.NewLine}"
                        + $"Recipients: {string.Join(", ", message.To)}{Environment.NewLine}"
                        + $"Detections: {detections.Count}",
                        Logger.EventCode.EmailSent);

                    // Warn about any recipients the server rejected. The email was still sent to the others.
                    if (rejected.Count > 0)
                    {
                        Logger.WriteWarning(
                            "The mail server rejected some recipients of an email notification. The email was sent to the others."
                            + $"{Environment.NewLine}{Environment.NewLine}"
                            + $"Notification: {email.Name}{Environment.NewLine}"
                            + $"Rejected recipients:{Environment.NewLine}"
                            + string.Join(Environment.NewLine, rejected),
                            Logger.EventCode.EmailRecipientsRejected);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    $"tcpTrigger failed to send the email notification through {route}."
                    + $"{Environment.NewLine}{Environment.NewLine}Notification: {email.Name}"
                    + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}"
                    + (ex.InnerException != null ? $"{Environment.NewLine}{ex.InnerException.Message}" : string.Empty),
                    Logger.EventCode.EmailSendFailed);
            }
        }

        // Sends a reminder that the notification's Microsoft 365 client secret is about to expire, through the
        // notification itself. timeLeft says how soon: "in 7 days", "tomorrow", or "today". expires is the date it
        // expires. Errors are logged, not thrown.
        public static void SendSecretReminder(NotificationEmail email, string timeLeft, DateTime expires)
        {
            try
            {
                using (MailMessage message = CreateMessage(email))
                {
                    message.Subject = $"tcpTrigger: Microsoft 365 client secret expiring {timeLeft}";
                    message.Body =
                        $"The Microsoft 365 client secret for the tcpTrigger notification '{email.Name}' on {Environment.MachineName} is expiring {timeLeft}."
                        + $"{Environment.NewLine}{Environment.NewLine}"
                        + "Create a new client secret for the app registration in Microsoft Entra. Then update the notification "
                        + "in tcpTrigger with the new secret and its expiry date."
                        + $"{Environment.NewLine}{Environment.NewLine}"
                        + $"Expiration date: {expires.ToString(ConfigNames.DateFormat, CultureInfo.InvariantCulture)}{Environment.NewLine}"
                        + $"Tenant ID: {email.TenantId}{Environment.NewLine}"
                        + $"Client ID: {email.ClientId}";
                    SendMessage(email, message);

                    Logger.Write(
                        "A client secret reminder email was sent through Microsoft 365."
                        + $"{Environment.NewLine}{Environment.NewLine}"
                        + $"Notification: {email.Name}{Environment.NewLine}"
                        + $"Recipients: {string.Join(", ", message.To)}",
                        Logger.EventCode.EmailSent);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    "tcpTrigger failed to send the client secret reminder email through Microsoft 365."
                    + $"{Environment.NewLine}{Environment.NewLine}Notification: {email.Name}"
                    + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    Logger.EventCode.EmailSendFailed);
            }
        }

        // Returns a message from the notification's from address to its recipients, without a subject or body.
        private static MailMessage CreateMessage(NotificationEmail email)
        {
            MailMessage message = new MailMessage
            {
                From = string.IsNullOrWhiteSpace(email.FromDisplayName)
                    ? new MailAddress(email.FromAddress)
                    : new MailAddress(email.FromAddress, email.FromDisplayName)
            };
            foreach (string recipient in email.Recipients)
            {
                if (!string.IsNullOrWhiteSpace(recipient))
                {
                    message.To.Add(recipient.Trim());
                }
            }
            return message;
        }

        // Sends the message with the notification's send method. Returns any rejected recipients.
        // Microsoft 365 doesn't report rejected recipients when sending. The mailbox gets a bounce email later instead.
        private static List<string> SendMessage(NotificationEmail email, MailMessage message)
        {
            if (email.SendMethod == EmailSendMethod.Microsoft365)
            {
                Microsoft365Sender.Send(email.TenantId, email.ClientId, email.ClientSecret, message);
                return new List<string>();
            }
            return SmtpSender.Send(email.SmtpServer, email.Port, email.Security, email.Username, email.Password, message);
        }
    }
}
