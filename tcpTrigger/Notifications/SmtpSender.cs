using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace tcpTrigger
{
    // How the connection to the mail server is secured.
    public enum EmailSecurity
    {
        None,
        StartTls,
        Tls
    }

    // Sends email over SMTP. Implicit TLS is sent by this class.
    // No security and STARTTLS are sent by .NET's SmtpClient.
    internal static class SmtpSender
    {
        // How long to wait for the mail server on each read and write.
        private const int TimeoutMilliseconds = 60000;

        // How long the whole email may take, even if the server keeps sending data.
        private static readonly TimeSpan s_sendTimeout = TimeSpan.FromMinutes(3);

        // The most lines a reply can have. A normal reply has fewer than 20.
        private const int MaxReplyLines = 100;

        // Sends the message through the mail server. Only the message's To recipients are used, and authentication
        // is skipped if there's no username. The message is sent to every recipient the server accepts. Returns
        // any rejected recipients, each with the server's reply. Throws an exception if the server rejects any
        // other step, or every recipient.
        public static List<string> Send(string host, int port, EmailSecurity security, string username, string password, MailMessage message)
        {
            return security == EmailSecurity.Tls
                ? SendImplicitTls(host, port, username, password, message)
                : SendWithSmtpClient(host, port, security == EmailSecurity.StartTls, username, password, message);
        }

        // Sends over implicit TLS. Gives up if the whole send takes longer than s_sendTimeout.
        private static List<string> SendImplicitTls(string host, int port, string username, string password, MailMessage message)
        {
            using (TcpClient tcp = new TcpClient())
            {
                // Closes the connection if the whole send takes too long, which ends any step that's still waiting.
                bool isTimedOut = false;
                using (Timer deadline = new Timer(state => { isTimedOut = true; tcp.Close(); }, null, s_sendTimeout, Timeout.InfiniteTimeSpan))
                {
                    try
                    {
                        return SendOverTls(tcp, host, port, username, password, message);
                    }
                    catch (Exception) when (isTimedOut)
                    {
                        throw new TimeoutException($"The mail server didn't finish within {s_sendTimeout.TotalMinutes} minutes.");
                    }
                }
            }
        }

        // Sends the message over a TLS connection. Returns any rejected recipients.
        private static List<string> SendOverTls(TcpClient tcp, string host, int port, string username, string password, MailMessage message)
        {
            tcp.ReceiveTimeout = TimeoutMilliseconds;
            tcp.SendTimeout = TimeoutMilliseconds;
            tcp.Connect(host, port);

            using (SslStream ssl = new SslStream(tcp.GetStream()))
            using (StreamReader reader = new StreamReader(ssl, Encoding.ASCII))
            {
                // Checks the server's certificate against the host name, without a revocation check, like SmtpClient.
                // SslProtocols.None uses the TLS versions Windows allows.
                ssl.AuthenticateAsClient(host, null, SslProtocols.None, checkCertificateRevocation: false);

                ReadReply(reader, 220);
                SendLine(ssl, "EHLO " + Environment.MachineName);
                List<string> capabilities = ReadReply(reader, 250);

                if (!string.IsNullOrWhiteSpace(username))
                {
                    Authenticate(ssl, reader, capabilities, username, password ?? string.Empty);
                }

                SendLine(ssl, $"MAIL FROM:<{ValidateAddress(message.From)}>");
                ReadReply(reader, 250);

                // A rejected recipient doesn't stop the email, as long as the server accepts at least one.
                List<string> rejected = new List<string>();
                foreach (MailAddress recipient in message.To)
                {
                    SendLine(ssl, $"RCPT TO:<{ValidateAddress(recipient)}>");
                    List<string> reply = ReadReplyLines(reader);
                    if (!HasCode(reply, 250, 251))
                    {
                        rejected.Add($"{recipient.Address} ({string.Join(" ", reply)})");
                    }
                }
                if (rejected.Count > 0 && rejected.Count == message.To.Count)
                {
                    throw new SmtpException("The mail server rejected every recipient: " + string.Join(", ", rejected));
                }

                SendLine(ssl, "DATA");
                ReadReply(reader, 354);

                // A line with only a dot ends the message. No line of the message can start with a dot:
                // header lines start with a name or a space, and the body is base64.
                SendText(ssl, BuildMessage(message) + ".\r\n");
                ReadReply(reader, 250);

                // The message has been accepted, so a problem with QUIT doesn't matter.
                try
                {
                    SendLine(ssl, "QUIT");
                    ReadReply(reader, 221);
                }
                catch
                {
                }

                return rejected;
            }
        }

        // Sends with .NET's SmtpClient, with STARTTLS or without security. Returns any rejected recipients.
        private static List<string> SendWithSmtpClient(string host, int port, bool useStartTls, string username, string password, MailMessage message)
        {
            using (SmtpClient smtpClient = new SmtpClient(host, port))
            {
                smtpClient.EnableSsl = useStartTls;
                if (!string.IsNullOrWhiteSpace(username))
                {
                    smtpClient.Credentials = new NetworkCredential(username, password);
                }

                try
                {
                    smtpClient.Send(message);
                    return new List<string>();
                }
                catch (SmtpFailedRecipientException ex)
                {
                    // SmtpClient sends the message to the accepted recipients, then throws for the rejected ones.
                    // If every recipient was rejected, nothing was sent.
                    SmtpFailedRecipientException[] failures = ex is SmtpFailedRecipientsException several
                        ? several.InnerExceptions
                        : new[] { ex };
                    if (failures.Length >= message.To.Count)
                    {
                        throw;
                    }

                    List<string> rejected = new List<string>();
                    foreach (SmtpFailedRecipientException failure in failures)
                    {
                        rejected.Add($"{failure.FailedRecipient.Trim('<', '>')} ({failure.Message})");
                    }
                    return rejected;
                }
            }
        }

        // Returns the address, or throws an exception if it contains a line break.
        // A line break would let the address add extra SMTP commands or headers.
        private static string ValidateAddress(MailAddress address)
        {
            if (address.Address.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                throw new SmtpException("An email address contains a line break.");
            }
            return address.Address;
        }

        // Logs in with AUTH PLAIN if the server offers it, otherwise AUTH LOGIN.
        // Throws an exception if the server offers neither.
        private static void Authenticate(SslStream ssl, StreamReader reader, List<string> capabilities, string username, string password)
        {
            List<string> methods = GetAuthMethods(capabilities);
            if (methods.Contains("PLAIN"))
            {
                SendLine(ssl, "AUTH PLAIN " + ToBase64("\0" + username + "\0" + password));
                ReadReply(reader, 235);
            }
            else if (methods.Contains("LOGIN"))
            {
                SendLine(ssl, "AUTH LOGIN");
                ReadReply(reader, 334);
                SendLine(ssl, ToBase64(username));
                ReadReply(reader, 334);
                SendLine(ssl, ToBase64(password));
                ReadReply(reader, 235);
            }
            else
            {
                string offered = methods.Count > 0 ? string.Join(" ", methods) : "none";
                throw new SmtpException(
                    $"The mail server doesn't offer a supported login method. It offers: {offered}. SSL/TLS supports PLAIN and LOGIN.");
            }
        }

        // Returns the login methods in the EHLO reply's AUTH line, such as LOGIN and PLAIN in "250-AUTH LOGIN PLAIN".
        private static List<string> GetAuthMethods(List<string> ehloReply)
        {
            List<string> methods = new List<string>();
            foreach (string line in ehloReply)
            {
                // Some older servers write the line as "AUTH=LOGIN PLAIN".
                string text = line.Length > 4 ? line.Substring(4).ToUpperInvariant() : string.Empty;
                if (text.StartsWith("AUTH ") || text.StartsWith("AUTH="))
                {
                    methods.AddRange(text.Substring(5).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                }
            }
            return methods;
        }

        // Reads one reply, checks its code, and returns its lines.
        // Throws an exception if the code isn't one of the expected codes.
        private static List<string> ReadReply(StreamReader reader, params int[] expectedCodes)
        {
            List<string> lines = ReadReplyLines(reader);
            if (!HasCode(lines, expectedCodes))
            {
                throw new SmtpException("The mail server replied: " + string.Join(" ", lines));
            }
            return lines;
        }

        // Reads one reply and returns its lines, without checking its code. A reply can have several lines:
        // each line but the last has a dash after the code, such as "250-SIZE".
        private static List<string> ReadReplyLines(StreamReader reader)
        {
            List<string> lines = new List<string>();
            string line;
            do
            {
                line = reader.ReadLine() ?? throw new IOException("The mail server closed the connection.");
                if (lines.Count >= MaxReplyLines)
                {
                    throw new SmtpException("The mail server's reply was too long.");
                }
                lines.Add(line);
            }
            while (line.Length > 3 && line[3] == '-');

            return lines;
        }

        // Returns true if the reply's code is one of the expected codes. Every line of a reply has the same code,
        // so the last line is checked.
        private static bool HasCode(List<string> reply, params int[] expectedCodes)
        {
            string line = reply[reply.Count - 1];
            return line.Length >= 3 && int.TryParse(line.Substring(0, 3), out int code) && Array.IndexOf(expectedCodes, code) >= 0;
        }

        // Sends one SMTP command, followed by <CRLF>.
        private static void SendLine(SslStream ssl, string line)
        {
            SendText(ssl, line + "\r\n");
        }

        // Sends the text exactly as it is, without adding a <CRLF>.
        private static void SendText(SslStream ssl, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            ssl.Write(bytes, 0, bytes.Length);
            ssl.Flush();
        }

        // Returns the message's headers and body. Everything that isn't plain ASCII is encoded,
        // so the message only contains ASCII characters.
        private static string BuildMessage(MailMessage message)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            List<string> recipients = new List<string>();
            foreach (MailAddress recipient in message.To)
            {
                recipients.Add(FormatAddress(recipient));
            }

            // Lines in the body must end with CRLF.
            string body = (message.Body ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "\r\n");

            StringBuilder sb = new StringBuilder();
            sb.Append("From: ").Append(FormatAddress(message.From)).Append("\r\n");
            sb.Append("To: ").Append(string.Join(", ", recipients)).Append("\r\n");
            sb.Append("Subject: ").Append(EncodeHeaderText(message.Subject ?? string.Empty)).Append("\r\n");
            sb.Append("Date: ")
                .Append(now.ToString("ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture))
                .Append(now.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", string.Empty))
                .Append("\r\n");
            sb.Append($"Message-ID: <{Guid.NewGuid():N}@{Environment.MachineName}>\r\n");
            sb.Append("MIME-Version: 1.0\r\n");
            sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
            sb.Append("Content-Transfer-Encoding: base64\r\n");
            sb.Append("\r\n");
            sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(body), Base64FormattingOptions.InsertLineBreaks));
            sb.Append("\r\n");
            return sb.ToString();
        }

        // Returns the address for a header, with its display name if it has one: "Name" <user@example.com>.
        private static string FormatAddress(MailAddress address)
        {
            string bareAddress = ValidateAddress(address);
            if (string.IsNullOrEmpty(address.DisplayName))
            {
                return bareAddress;
            }

            string name = IsPlainAscii(address.DisplayName)
                ? "\"" + address.DisplayName.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
                : EncodeHeaderText(address.DisplayName);
            return $"{name} <{bareAddress}>";
        }

        // Returns text for a header. Plain ASCII is used as it is. Anything else is encoded as UTF-8 in base64,
        // split into words of at most 15 characters so each stays under the 75-character limit for encoded words.
        private static string EncodeHeaderText(string text)
        {
            // Line breaks would end the header, so they're replaced with spaces.
            text = text.Replace("\r", " ").Replace("\n", " ");
            if (IsPlainAscii(text))
            {
                return text;
            }

            StringBuilder sb = new StringBuilder();
            int start = 0;
            while (start < text.Length)
            {
                int length = Math.Min(15, text.Length - start);

                // Keep the two halves of a surrogate pair in the same word.
                if (char.IsHighSurrogate(text[start + length - 1]) && start + length < text.Length)
                {
                    length--;
                }

                if (sb.Length > 0)
                {
                    sb.Append("\r\n ");
                }
                sb.Append("=?utf-8?B?").Append(ToBase64(text.Substring(start, length))).Append("?=");
                start += length;
            }
            return sb.ToString();
        }

        private static bool IsPlainAscii(string text)
        {
            foreach (char c in text)
            {
                if (c < 0x20 || c > 0x7E)
                {
                    return false;
                }
            }
            return true;
        }

        private static string ToBase64(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }
    }
}
