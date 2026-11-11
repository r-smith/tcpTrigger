using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace tcpTrigger
{
    // Sends notification emails using the email settings.
    internal static class EmailNotifier
    {
        // Sends a notification email for the interface. Errors are logged, not thrown.
        public static async Task SendAsync(TcpTriggerInterface ipInterface, string connectionLog)
        {
            try
            {
                using (MailMessage message = new MailMessage())
                {
                    message.Subject = UserVariableExpansion.GetExpandedString(Settings.EmailSubject, ipInterface, connectionLog);
                    message.Body = UserVariableExpansion.GetExpandedString(Settings.EmailBody ?? string.Empty, ipInterface, connectionLog);
                    message.From = string.IsNullOrWhiteSpace(Settings.EmailFromDisplayName)
                        ? new MailAddress(Settings.EmailFromAddress)
                        : new MailAddress(Settings.EmailFromAddress, Settings.EmailFromDisplayName);
                    for (int i = 0; i < Settings.EmailRecipients.Count; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(Settings.EmailRecipients[i]))
                        {
                            message.To.Add(Settings.EmailRecipients[i].Trim());
                        }
                    }

                    using (SmtpClient smtpClient = new SmtpClient())
                    {
                        smtpClient.Host = Settings.EmailSmtpServer;
                        smtpClient.Port = Settings.EmailPort;
                        smtpClient.EnableSsl = Settings.IsEmailStartTlsEnabled;
                        if (!string.IsNullOrWhiteSpace(Settings.EmailUsername))
                        {
                            smtpClient.Credentials = new NetworkCredential(Settings.EmailUsername, Settings.EmailPassword);
                        }
                        await smtpClient.SendMailAsync(message);
                    }

                    int detectionCount = connectionLog.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length;
                    Logger.Write(
                        $"An email notification was sent through {Settings.EmailSmtpServer}:{Settings.EmailPort}."
                        + $"{Environment.NewLine}{Environment.NewLine}"
                        + $"Recipients: {string.Join(", ", message.To)}{Environment.NewLine}"
                        + $"Detections: {detectionCount}",
                        Logger.EventCode.EmailSent);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    $"tcpTrigger failed to send the email notification through {Settings.EmailSmtpServer}:{Settings.EmailPort}."
                    + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}"
                    + (ex.InnerException != null ? $"{Environment.NewLine}{ex.InnerException.Message}" : string.Empty),
                    Logger.EventCode.EmailSendFailed);
            }
        }
    }
}