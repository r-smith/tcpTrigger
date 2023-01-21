using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.ServiceProcess;

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
                    Settings.FileLock.EnterWriteLock();

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
                    Logger.WriteError(
                        $"Maximum log size {_maxFileSizeInBytes} bytes has been reached. Error rotating log file '{Settings.LogPath}'. "
                        + $"Logging has been disabled.{ Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        Logger.EventCode.Error);
                }
                finally
                {
                    Settings.FileLock.ExitWriteLock();
                }
            }

            try
            {
                Settings.FileLock.EnterWriteLock();
                using (StreamWriter outputFile = new StreamWriter(Settings.LogPath, true))
                {
                    outputFile.WriteLine(GetLogText(packetHeader));
                }
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    $"Error writing to log file '{Settings.LogPath}'.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    Logger.EventCode.Error);
            }
            finally
            {
                Settings.FileLock.ExitWriteLock();
            }
        }

        private string GetLogText(PacketHeader packetHeader)
        {
            string logText;
            switch (packetHeader.MatchType)
            {
                case PacketMatch.TcpConnect:
                    logText = $"TCP connection to port {packetHeader.DestinationPort} from {packetHeader.SourceIP}";
                    break;
                case PacketMatch.UdpCommunication:
                    logText = $"UDP communication to port {packetHeader.DestinationPort} from {packetHeader.SourceIP}";
                    break;
                case PacketMatch.IcmpRequest:
                    logText = $"ICMP {Enum.GetName(typeof(IcmpTypeCode), packetHeader.IcmpType)} request from {packetHeader.SourceIP}";
                    break;
                case PacketMatch.RogueDhcp:
                    logText = $"Unrecognized DHCP server at {packetHeader.DhcpServerAddress}";
                    break;
                default:
                    logText = string.Empty;
                    break;
            }
            return
                DateTime.Now.ToString(Settings.TimestampFormat) + " [" + packetHeader.DestinationIP + "] " + logText;
        }

        private void WriteEventLog(PacketHeader packetHeader)
        {
            switch (packetHeader.MatchType)
            {
                case PacketMatch.IcmpRequest:
                    Logger.Write(
                        $"ICMP {Enum.GetName(typeof(IcmpTypeCode), packetHeader.IcmpType)} request detected.{Environment.NewLine}{Environment.NewLine}"
                        + $"Match type: ICMP{Environment.NewLine}"
                        + $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}"
                        + $"Destination IP: {packetHeader.DestinationIP}",
                        Logger.EventCode.MatchedIcmp);
                    break;
                case PacketMatch.TcpConnect:
                    Logger.Write(
                        $"TCP connection detected.{Environment.NewLine}{Environment.NewLine}"
                        + $"Match type: TCP{Environment.NewLine}"
                        + $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}"
                        + $"Source port: {packetHeader.SourcePort}{Environment.NewLine}"
                        + $"Destination IP: {packetHeader.DestinationIP}{Environment.NewLine}"
                        + $"Destination port: {packetHeader.DestinationPort}{Environment.NewLine}{Environment.NewLine}"
                        + $"TCP flags: {packetHeader.TcpFlagsAsString}",
                        Logger.EventCode.MatchedTcp);
                    break;
                case PacketMatch.UdpCommunication:
                    Logger.Write(
                        $"UDP connection detected.{Environment.NewLine}{Environment.NewLine}"
                        + $"Match type: UDP{Environment.NewLine}"
                        + $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}"
                        + $"Source port: {packetHeader.SourcePort}{Environment.NewLine}"
                        + $"Destination IP: {packetHeader.DestinationIP}{Environment.NewLine}"
                        + $"Destination port: {packetHeader.DestinationPort}",
                        Logger.EventCode.MatchedUdp);
                    break;
                case PacketMatch.RogueDhcp:
                    Logger.Write(
                        $"DHCP server detected.{Environment.NewLine}{Environment.NewLine}"
                        + $"Match type: DHCP{Environment.NewLine}"
                        + $"DHCP server IP: {packetHeader.DhcpServerAddress}{Environment.NewLine}"
                        + $"DHCP transaction ID: {packetHeader.DhcpTransactionId}{Environment.NewLine}"
                        + $"Interface IP: {packetHeader.DestinationIP}",
                        Logger.EventCode.MatchedDhcp);
                    break;
            }
        }

        private void LaunchApplication(PacketHeader packetHeader)
        {
            try
            {
                Process.Start(
                    Settings.ExternalAppPath,
                    UserVariableExpansion.GetExpandedString(Settings.ExternalAppArguments, packetHeader));
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    $"Error launching external application '{Settings.ExternalAppPath}'.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    Logger.EventCode.Error);
            }
        }

        private void TriggerEmail(PacketHeader packetHeader, TcpTriggerInterface ipInterface)
        {
            // Record matched connection to log buffer and enable timer, which handles sending the notification email.
            ipInterface.Mutex.WaitOne();
            ipInterface.EmailLogBuffer += GetLogText(packetHeader) + Environment.NewLine;
            ipInterface.EmailSendTimer.Enabled = true;
            ipInterface.Mutex.ReleaseMutex();
        }

        public static async void SendEmail(TcpTriggerInterface ipInterface)
        {
            if (Settings.EmailRecipients.Count == 0)
            {
                Logger.WriteError(
                    "Email event triggered, but no recipient address is configured.",
                    Logger.EventCode.Error);
                return;
            }
            if (Settings.EmailSender.Length == 0)
            {
                Logger.WriteError(
                    "Email event triggered, but no sender address is configured.",
                    Logger.EventCode.Error);
                return;
            }
            if (Settings.EmailServer.Length == 0)
            {
                Logger.WriteError(
                    "Email event triggered, but no mail server is configured.",
                    Logger.EventCode.Error);
                return;
            }
            if (Settings.EmailSubject.Length == 0)
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
                    message.Subject = UserVariableExpansion.GetExpandedString(Settings.EmailSubject, ipInterface);
                    message.Body = UserVariableExpansion.GetExpandedString(Settings.EmailBody, ipInterface);
                    message.From = Settings.EmailSenderDisplayName.Length > 0
                        ? new MailAddress(Settings.EmailSender, Settings.EmailSenderDisplayName)
                        : new MailAddress(Settings.EmailSender);
                    for (int i = 0; i < Settings.EmailRecipients.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(Settings.EmailRecipients[i]))
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
                    $"Email action triggered, but the message failed to send.{Environment.NewLine}{ex.Message}{Environment.NewLine}{ex.InnerException.Message}",
                    Logger.EventCode.Error);
                return;
            }
        }
    }
}