using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace tcpTrigger
{
    internal static class EmailNotifier
    {
        public static async Task Send(TcpTriggerInterface ipInterface, string connectionLog)
        {
            if (Settings.EmailRecipients.Count == 0)
            {
                Logger.WriteError(
                    "Email event triggered, but no recipient address is configured.",
                    Logger.EventCode.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(Settings.EmailSender))
            {
                Logger.WriteError(
                    "Email event triggered, but no sender address is configured.",
                    Logger.EventCode.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(Settings.EmailServer))
            {
                Logger.WriteError(
                    "Email event triggered, but no mail server is configured.",
                    Logger.EventCode.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(Settings.EmailSubject))
            {
                Logger.WriteError(
                    "Email event triggered, but no message subject is configured.",
                    Logger.EventCode.Error);
                return;
            }

            try
            {
                using (MailMessage message = new MailMessage())
                {
                    message.Subject = UserVariableExpansion.GetExpandedString(Settings.EmailSubject, ipInterface, connectionLog);
                    message.Body = UserVariableExpansion.GetExpandedString(Settings.EmailBody ?? string.Empty, ipInterface, connectionLog);
                    message.From = string.IsNullOrWhiteSpace(Settings.EmailSenderDisplayName)
                        ? new MailAddress(Settings.EmailSender)
                        : new MailAddress(Settings.EmailSender, Settings.EmailSenderDisplayName);
                    for (int i = 0; i < Settings.EmailRecipients.Count; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(Settings.EmailRecipients[i]))
                        {
                            message.To.Add(Settings.EmailRecipients[i].Trim());
                        }
                    }

                    using (SmtpClient smtpClient = new SmtpClient())
                    {
                        smtpClient.Host = Settings.EmailServer;
                        smtpClient.Port = Settings.EmailServerPort;
                        smtpClient.EnableSsl = Settings.IsEmailTlsEnabled;
                        if (Settings.IsEmailAuthRequired)
                        {
                            smtpClient.Credentials = new NetworkCredential(Settings.EmailUsername, Settings.EmailPassword);
                        }
                        await smtpClient.SendMailAsync(message);
                    }
                }
            }

            catch (Exception ex)
            {
                Logger.WriteError(
                    $"Email action triggered, but the message failed to send.{Environment.NewLine}{ex.Message}{Environment.NewLine}{ex.InnerException?.Message}",
                    Logger.EventCode.Error);
                return;
            }
        }
    }
}
