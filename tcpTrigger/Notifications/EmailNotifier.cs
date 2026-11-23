using System;
using System.Collections.Generic;
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
            try
            {
                using (MailMessage message = new MailMessage())
                {
                    message.Subject = UserVariableExpansion.GetExpandedString(email.Subject, sourceIp, detections);
                    message.Body = UserVariableExpansion.GetExpandedString(email.Body ?? string.Empty, sourceIp, detections);
                    message.From = string.IsNullOrWhiteSpace(email.FromDisplayName)
                        ? new MailAddress(email.FromAddress)
                        : new MailAddress(email.FromAddress, email.FromDisplayName);
                    foreach (string recipient in email.Recipients)
                    {
                        if (!string.IsNullOrWhiteSpace(recipient))
                        {
                            message.To.Add(recipient.Trim());
                        }
                    }

                    List<string> rejected = SmtpSender.Send(
                        email.SmtpServer, email.Port, email.Security, email.Username, email.Password, message);

                    Logger.Write(
                        $"An email notification was sent through {email.SmtpServer}:{email.Port}."
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
                    $"tcpTrigger failed to send the email notification through {email.SmtpServer}:{email.Port}."
                    + $"{Environment.NewLine}{Environment.NewLine}Notification: {email.Name}"
                    + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}"
                    + (ex.InnerException != null ? $"{Environment.NewLine}{ex.InnerException.Message}" : string.Empty),
                    Logger.EventCode.EmailSendFailed);
            }
        }
    }
}
