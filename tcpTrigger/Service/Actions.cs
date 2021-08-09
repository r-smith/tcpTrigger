using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.ServiceProcess;
using System.Threading;

namespace tcpTrigger
{
    partial class tcpTrigger : ServiceBase
    {
        private void WriteLog(PacketHeader packetHeader)
        {
            // Log files have a defined maximum file size and will rotate one time if reached.
            const int _maxFileSizeInBytes = 15 * 1024 * 1024;

            // Check if log file already exists.
            if (File.Exists(Settings.LogPath))
            {
                try
                {
                    // Get current file size.
                    long currentFileSize = new FileInfo(Settings.LogPath).Length;
                    if (currentFileSize >= _maxFileSizeInBytes)
                    {
                        // Log reached max size.
                        // Rotate by copying current log to .1 suffix, overwriting if exists.
                        File.Copy(Settings.LogPath, Settings.LogPath + ".1", true);
                        // Clear contents of original log.
                        using (FileStream fileStream = File.Open(Settings.LogPath, FileMode.Open))
                        {
                            fileStream.SetLength(0);
                            fileStream.Close();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Settings.IsLogEnabled = false;
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Maximum log size {_maxFileSizeInBytes} bytes has been reached. Error rotating log file '{Settings.LogPath}'. Logging has been disabled.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        EventLogEntryType.Error,
                        401);
                }
            }

            try
            {
                using (StreamWriter outputFile = new StreamWriter(Settings.LogPath, true))
                {
                    string logText;
                    switch (packetHeader.MatchType)
                    {
                        case PacketMatch.TcpConnect:
                            logText = $"TCP connection to port {packetHeader.DestinationPort} from {packetHeader.SourceIP}";
                            break;
                        case PacketMatch.PingRequest:
                            logText = $"ICMP ping request from {packetHeader.SourceIP}";
                            break;
                        case PacketMatch.NamePoison:
                            logText = $"Name poison attempt from {packetHeader.SourceIP}";
                            break;
                        case PacketMatch.RogueDhcp:
                            logText = $"Unrecognized DHCP server at {packetHeader.DhcpServerAddress}";
                            break;
                        default:
                            logText = string.Empty;
                            break;
                    }
                    outputFile.WriteLine(
                        DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString()
                        + " [" + packetHeader.DestinationIP + "] " + logText);
                }
            }
            catch (Exception ex)
            {
                Settings.IsLogEnabled = false;
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error writing to log file '{Settings.LogPath}'. Logging has been disabled.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    EventLogEntryType.Error,
                    401);
            }
        }

        private void WriteEventLog(PacketHeader packetHeader)
        {
            switch (packetHeader.MatchType)
            {
                case PacketMatch.TcpConnect:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"TCP connection detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}" +
                        $"Destination IP: {packetHeader.DestinationIP}{Environment.NewLine}" +
                        $"Destination port: {packetHeader.DestinationPort}{Environment.NewLine}{Environment.NewLine}" +
                        $"TCP flags: {packetHeader.TcpFlagsAsString}",
                        EventLogEntryType.Information,
                        200);
                    break;
                case PacketMatch.PingRequest:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"ICMP ping request detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}" +
                        $"Destination IP: {packetHeader.DestinationIP}",
                        EventLogEntryType.Information,
                        201);
                    break;
                case PacketMatch.NamePoison:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Name poison attempt detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}",
                        EventLogEntryType.Information,
                        202);
                    break;
                case PacketMatch.RogueDhcp:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Unrecognized DHCP server detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"DHCP Server IP: {packetHeader.DhcpServerAddress}{Environment.NewLine}" +
                        $"Interface: {packetHeader.DestinationIP}",
                        EventLogEntryType.Information,
                        202);
                    break;
            }
        }

        private void LaunchApplication(PacketHeader packetHeader)
        {
            if (string.IsNullOrEmpty(Settings.ExternalAppPath) || !File.Exists(Settings.ExternalAppPath))
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"An external application has been triggered to launch, but the specified application path '{Settings.ExternalAppPath}' was not found. Update your tcpTrigger configuration to point to a valid executable.",
                    EventLogEntryType.Warning,
                    401);
                return;
            }

            try
            {
                Process.Start(
                    Settings.ExternalAppPath,
                    UserVariableExpansion.GetExpandedString(Settings.ExternalAppArguments, packetHeader));
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error launching external triggered application '{Settings.ExternalAppPath}'.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    EventLogEntryType.Error,
                    401);
            }
        }

        private void SendEmail(PacketHeader packetHeader)
        {
            if (Settings.EmailRecipients.Count == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no recipient address is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (Settings.EmailSender.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no sender address is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (Settings.EmailServer.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no mail server is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (Settings.EmailSubject.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no message subject is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }

            using (var message = new MailMessage())
            {

                try
                {
                    var smtpClient = new SmtpClient();
                    smtpClient.Host = Settings.EmailServer;
                    smtpClient.Port = Settings.EmailServerPort;
                    if (Settings.IsEmailAuthRequired)
                    {
                        smtpClient.Credentials = new NetworkCredential(Settings.EmailUsername, Settings.EmailPassword);
                    }
                    message.From = Settings.EmailSenderDisplayName.Length > 0 ?
                        new MailAddress(Settings.EmailSender, Settings.EmailSenderDisplayName)
                        : new MailAddress(Settings.EmailSender);
                    for (int i = 0; i < Settings.EmailRecipients.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(Settings.EmailRecipients[i]))
                        {
                            message.To.Add(Settings.EmailRecipients[i].Trim());
                        }
                    }
                    message.Subject = UserVariableExpansion.GetExpandedString(Settings.EmailSubject, packetHeader);
                    message.Body = UserVariableExpansion.GetExpandedString(GetMessageBody(packetHeader), packetHeader);

                    //Send the email.
                    smtpClient.Send(message);
                }
                catch (Exception ex)
                {
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Email action triggered, but the message failed to send.{Environment.NewLine}{ex.Message}",
                        EventLogEntryType.Warning,
                        403);
                    return;
                }
            }
        }

        private string GetMessageBody(PacketHeader packetHeader)
        {
            string messageBody;
            switch (packetHeader.MatchType)
            {
                case PacketMatch.PingRequest:
                    messageBody = Settings.MessageBodyPing;
                    break;
                case PacketMatch.TcpConnect:
                    messageBody = Settings.MessageBodyTcpConnect;
                    break;
                case PacketMatch.NamePoison:
                    messageBody = Settings.MessageBodyNamePoison;
                    break;
                case PacketMatch.RogueDhcp:
                    messageBody = Settings.MessageBodyRogueDhcp;
                    break;
                default:
                    messageBody = "Not defined.";
                    break;
            }

            return messageBody;
        }

        private void NamePoisionDetectionTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _isNamePoisonDetectionInProgress = true;
            _namePoisonTransactionIdResponse = int.MinValue;

            foreach (TcpTriggerInterface ipInterface in _tcpTriggerInterfaces)
            {
                // Generate three random hostnames between 7 and 15 characters to emulate a user opening Chrome.
                // Chromium source: https://cs.chromium.org/chromium/src/chrome/browser/intranet_redirect_detector.cc

                var randomHostnames = new string[3];
                var r = new Random();
                for (int i = 0; i < randomHostnames.Length; ++i)
                {
                    var hostname = string.Empty;
                    int numberOfCharacters = r.Next(7, 16);
                    for (int j = 0; j < numberOfCharacters; ++j)
                        hostname += (char)('a' + r.Next(0, 26));
                    randomHostnames[i] = hostname;
                }

                // Send LLMNR name queries.
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = 0; j < randomHostnames.Length; ++j)
                        PacketGenerator.SendLlmnrQuery(ipInterface, (ushort)(_netbiosTransactionId + j), randomHostnames[j]);
                    Thread.Sleep(750);
                }

                // Send NetBIOS name queries.
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = 0; j < randomHostnames.Length; ++j)
                        PacketGenerator.SendNetbiosQuery(ipInterface, (ushort)(_netbiosTransactionId + j), randomHostnames[j]);
                    Thread.Sleep(750);
                }
            }

            Thread.Sleep(2000);
            _netbiosTransactionId += 3;
            if (_netbiosTransactionId < 0x8000)
                _netbiosTransactionId += 0x8000;
            _isNamePoisonDetectionInProgress = true;
        }
    }
}