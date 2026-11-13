using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace tcpTrigger
{
    // Writes each detection to the event log and runs its notifications.
    internal static class NotificationRunner
    {
        // Ensures only one thread at a time rotates or writes to the log file.
        private static readonly object s_logFileLock = new object();

        // Writes the detection to the event log, then runs each notification.
        public static void Run(PacketHeader packetHeader, TcpTriggerInterface ipInterface)
        {
            WriteEventLog(packetHeader);

            foreach (Notification notification in Settings.Notifications)
            {
                switch (notification)
                {
                    case NotificationLogFile logFile:
                        WriteLog(packetHeader, logFile);
                        break;
                    case NotificationCommand command:
                        RunCommand(packetHeader, command);
                        break;
                    case NotificationEmail email:
                        AddToEmailBatch(packetHeader, ipInterface, email);
                        break;
                }
            }
        }

        // Appends the detection to the notification's log file, rotating if needed.
        private static void WriteLog(PacketHeader packetHeader, NotificationLogFile logFile)
        {
            // Log files have a defined maximum file size and will rotate one time if reached.
            const int MaxFileSizeInBytes = 15 * 1024 * 1024;

            if (logFile.HasFailed)
            {
                return;
            }

            lock (s_logFileLock)
            {
                if (File.Exists(logFile.Path))
                {
                    try
                    {
                        long currentFileSize = new FileInfo(logFile.Path).Length;
                        if (currentFileSize >= MaxFileSizeInBytes)
                        {
                            // Log reached max size.
                            // Rotate by copying current log to .1 suffix, overwriting if exists.
                            File.Copy(logFile.Path, logFile.Path + ".1", true);
                            // Clear contents of original log.
                            using (FileStream fileStream = File.Open(logFile.Path, FileMode.Open))
                            {
                                fileStream.SetLength(0);
                            }

                            Logger.Write(
                                $"The log file '{logFile.Path}' reached its {MaxFileSizeInBytes / 1024 / 1024} MB limit. "
                                + $"Its contents were moved to '{logFile.Path}.1', replacing any previous copy.",
                                Logger.EventCode.LogRotated);
                        }
                    }
                    catch (Exception ex)
                    {
                        logFile.HasFailed = true;
                        Logger.WriteError(
                            $"Logging to the file '{logFile.Path}' has been disabled because it "
                            + $"reached its {MaxFileSizeInBytes / 1024 / 1024} MB limit and failed to rotate."
                            + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                            Logger.EventCode.LogRotationFailed);
                        return;
                    }
                }

                try
                {
                    using (StreamWriter outputFile = new StreamWriter(logFile.Path, true))
                    {
                        outputFile.WriteLine(JsonLogLine.Build(packetHeader));
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        $"tcpTrigger failed to write a detection to the log file '{logFile.Path}'."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        Logger.EventCode.LogWriteFailed);
                }
            }
        }

        // Returns a one-line description of a detection. Used in email notifications.
        private static string GetLogText(PacketHeader packetHeader)
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
                    logText = $"ICMP {packetHeader.IcmpTypeName} request from {packetHeader.SourceIP}";
                    break;
                default:
                    logText = string.Empty;
                    break;
            }
            return $"{packetHeader.Timestamp.ToString(Settings.TimestampFormat)} [{packetHeader.DestinationIP}] {logText}";
        }

        // Writes the detection's details to the event log.
        private static void WriteEventLog(PacketHeader packetHeader)
        {
            switch (packetHeader.MatchType)
            {
                case PacketMatch.IcmpRequest:
                    Logger.Write(
                        $"ICMP {packetHeader.IcmpTypeName} request from {packetHeader.SourceIP}.{Environment.NewLine}{Environment.NewLine}"
                        + $"Protocol: ICMP{Environment.NewLine}"
                        + $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}"
                        + $"Destination IP: {packetHeader.DestinationIP}{Environment.NewLine}{Environment.NewLine}"
                        + $"TTL: {packetHeader.Ttl}{Environment.NewLine}"
                        + $"ICMP type: {packetHeader.IcmpTypeName}",
                        Logger.EventCode.DetectedIcmp);
                    break;
                case PacketMatch.TcpConnect:
                    Logger.Write(
                        $"TCP connection attempt to port {packetHeader.DestinationPort} from {packetHeader.SourceIP}.{Environment.NewLine}{Environment.NewLine}"
                        + $"Protocol: TCP{Environment.NewLine}"
                        + $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}"
                        + $"Source port: {packetHeader.SourcePort}{Environment.NewLine}"
                        + $"Destination IP: {packetHeader.DestinationIP}{Environment.NewLine}"
                        + $"Destination port: {packetHeader.DestinationPort}{Environment.NewLine}{Environment.NewLine}"
                        + $"TTL: {packetHeader.Ttl}{Environment.NewLine}"
                        + $"TCP flags: {packetHeader.TcpFlagNames}",
                        Logger.EventCode.DetectedTcp);
                    break;
                case PacketMatch.UdpCommunication:
                    Logger.Write(
                        $"UDP packet to port {packetHeader.DestinationPort} from {packetHeader.SourceIP}.{Environment.NewLine}{Environment.NewLine}"
                        + $"Protocol: UDP{Environment.NewLine}"
                        + $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}"
                        + $"Source port: {packetHeader.SourcePort}{Environment.NewLine}"
                        + $"Destination IP: {packetHeader.DestinationIP}{Environment.NewLine}"
                        + $"Destination port: {packetHeader.DestinationPort}{Environment.NewLine}{Environment.NewLine}"
                        + $"TTL: {packetHeader.Ttl}",
                        Logger.EventCode.DetectedUdp);
                    break;
            }
        }

        // Runs the command, with any variables in its arguments replaced.
        private static void RunCommand(PacketHeader packetHeader, NotificationCommand command)
        {
            try
            {
                Process.Start(
                    command.Path,
                    UserVariableExpansion.GetExpandedString(command.Arguments ?? string.Empty, packetHeader));
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    $"tcpTrigger failed to run the command '{command.Path}' for a detection."
                    + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    Logger.EventCode.CommandFailed);
            }
        }

        // Adds the detection to the email notification's batch for its source IP. SendDueEmails sends it later.
        private static void AddToEmailBatch(PacketHeader packetHeader, TcpTriggerInterface ipInterface, NotificationEmail email)
        {
            if (!email.Batches.TryGetValue(packetHeader.SourceIP, out EmailBatch batch))
            {
                batch = new EmailBatch();
                email.Batches.Add(packetHeader.SourceIP, batch);
            }

            if (batch.Detections.Count == 0)
            {
                batch.FirstDetection = DateTime.UtcNow;
            }
            batch.Detections.Add(GetLogText(packetHeader));
            if (!batch.Interfaces.Any(existing => existing.IP.Equals(ipInterface.IP)))
            {
                batch.Interfaces.Add(ipInterface);
            }
        }

        // Sends each email that's due, and removes sources that have nothing left to send.
        // A source's email is due once the wait time has passed since its first detection
        // and the rate limit has passed since its last email.
        public static void SendDueEmails()
        {
            DateTime now = DateTime.UtcNow;
            foreach (NotificationEmail email in Settings.Notifications.OfType<NotificationEmail>())
            {
                foreach (KeyValuePair<IPAddress, EmailBatch> pair in email.Batches.ToList())
                {
                    EmailBatch batch = pair.Value;
                    bool isWaitOver = now >= batch.FirstDetection.AddSeconds(email.WaitSeconds);
                    bool isRateLimitOver = now >= batch.LastSent.AddSeconds(email.RateLimitSeconds);

                    if (batch.Detections.Count > 0 && isWaitOver && isRateLimitOver)
                    {
                        SendBatch(email, pair.Key, batch);
                    }
                    else if (batch.Detections.Count == 0 && isRateLimitOver)
                    {
                        email.Batches.Remove(pair.Key);
                    }
                }
            }
        }

        // Sends every email that's waiting, due or not, so stopping or reloading doesn't lose them.
        // Returns the sends, so the caller can wait for them.
        public static Task[] SendAllEmails()
        {
            List<Task> sends = new List<Task>();
            foreach (NotificationEmail email in Settings.Notifications.OfType<NotificationEmail>())
            {
                foreach (KeyValuePair<IPAddress, EmailBatch> pair in email.Batches)
                {
                    if (pair.Value.Detections.Count > 0)
                    {
                        sends.Add(SendBatch(email, pair.Key, pair.Value));
                    }
                }
            }
            return sends.ToArray();
        }

        // Starts sending the batch's email on another thread, then empties the batch.
        // EmailNotifier.SendAsync logs its own errors.
        private static Task SendBatch(NotificationEmail email, IPAddress sourceIp, EmailBatch batch)
        {
            // Copy the batch first, since it's emptied below while the email is still being sent.
            List<TcpTriggerInterface> interfaces = batch.Interfaces.ToList();
            List<string> detections = batch.Detections.ToList();

            // Send on a thread-pool thread, so a slow DNS lookup or mail server doesn't hold up the detection queue.
            Task send = Task.Run(() => EmailNotifier.SendAsync(email, sourceIp, interfaces, detections));

            batch.Detections.Clear();
            batch.Interfaces.Clear();
            batch.LastSent = DateTime.UtcNow;
            return send;
        }
    }
}