using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace tcpTrigger
{
    // Sends the emails for email notifications.
    internal static class EmailNotifier
    {
        // Sends the notification's email for a batch of detections. Errors are logged, not thrown.
        public static async Task SendAsync(NotificationEmail email, List<TcpTriggerInterface> interfaces, string connectionLog)
        {
            try
            {
                using (MailMessage message = new MailMessage())
                {
                    message.Subject = UserVariableExpansion.GetExpandedString(email.Subject, interfaces, connectionLog);
                    message.Body = UserVariableExpansion.GetExpandedString(email.Body ?? string.Empty, interfaces, connectionLog);
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

                    using (SmtpClient smtpClient = new SmtpClient())
                    {
                        smtpClient.Host = email.SmtpServer;
                        smtpClient.Port = email.Port;
                        smtpClient.EnableSsl = email.IsStartTlsEnabled;
                        if (!string.IsNullOrWhiteSpace(email.Username))
                        {
                            smtpClient.Credentials = new NetworkCredential(email.Username, email.Password);
                        }
                        await smtpClient.SendMailAsync(message);
                    }

                    int detectionCount = connectionLog.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length;
                    Logger.Write(
                        $"An email notification was sent through {email.SmtpServer}:{email.Port}."
                        + $"{Environment.NewLine}{Environment.NewLine}"
                        + $"Notification: {email.Name}{Environment.NewLine}"
                        + $"Recipients: {string.Join(", ", message.To)}{Environment.NewLine}"
                        + $"Detections: {detectionCount}",
                        Logger.EventCode.EmailSent);
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