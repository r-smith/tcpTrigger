﻿using System;
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
                    outputFile.WriteLine(GetLogText(packetHeader));
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
                case PacketMatch.PingRequest:
                    logText = $"ICMP ping request from {packetHeader.SourceIP}";
                    break;
                case PacketMatch.RogueDhcp:
                    logText = $"Unrecognized DHCP server at {packetHeader.DhcpServerAddress}";
                    break;
                default:
                    logText = string.Empty;
                    break;
            }
            return
                DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToString("HH:mm:ss")
                + " [" + packetHeader.DestinationIP + "] " + logText;
        }

        private void WriteEventLog(PacketHeader packetHeader)
        {
            switch (packetHeader.MatchType)
            {
                case PacketMatch.PingRequest:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"ICMP ping request detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}" +
                        $"Destination IP: {packetHeader.DestinationIP}",
                        EventLogEntryType.Information,
                        200);
                    break;
                case PacketMatch.TcpConnect:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"TCP connection detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}" +
                        $"Destination IP: {packetHeader.DestinationIP}{Environment.NewLine}" +
                        $"Destination port: {packetHeader.DestinationPort}{Environment.NewLine}{Environment.NewLine}" +
                        $"TCP flags: {packetHeader.TcpFlagsAsString}",
                        EventLogEntryType.Information,
                        201);
                    break;
                case PacketMatch.UdpCommunication:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"UDP communication detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}" +
                        $"Destination IP: {packetHeader.DestinationIP}{Environment.NewLine}" +
                        $"Destination port: {packetHeader.DestinationPort}{Environment.NewLine}{Environment.NewLine}",
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
                        203);
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

        private void TriggerEmail(PacketHeader packetHeader, TcpTriggerInterface ipInterface)
        {
            // Record matched connection to log buffer and enable timer, which handles sending the notification email.
            ipInterface.Mutex.WaitOne();
            ipInterface.EmailLogBuffer += GetLogText(packetHeader) + Environment.NewLine;
            ipInterface.EmailSendTimer.Enabled = true;
            ipInterface.Mutex.ReleaseMutex();
        }

        public static void SendEmail(TcpTriggerInterface ipInterface)
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

            using (MailMessage message = new MailMessage())
            {
                try
                {
                    SmtpClient smtpClient = new SmtpClient();
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
                    message.Subject = UserVariableExpansion.GetExpandedString(Settings.EmailSubject, ipInterface);
                    message.Body = UserVariableExpansion.GetExpandedString(Settings.EmailBody, ipInterface);

                    //Send the email.
                    smtpClient.Send(message);
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Email notifcation sent.",
                        EventLogEntryType.Information,
                        105);
                }
                catch (Exception ex)
                {
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Email action triggered, but the message failed to send.{Environment.NewLine}{ex.Message}{Environment.NewLine}{ex.InnerException.Message}",
                        EventLogEntryType.Warning,
                        403);
                    return;
                }
            }
        }
    }
}