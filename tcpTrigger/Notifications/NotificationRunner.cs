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
        public static void Run(PacketHeader packetHeader)
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
                    case BatchedNotification batched:
                        AddToBatch(packetHeader, batched);
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

        // Adds the detection to the notification's batch for its source IP. SendDueBatches sends it later.
        private static void AddToBatch(PacketHeader packetHeader, BatchedNotification notification)
        {
            if (!notification.Batches.TryGetValue(packetHeader.SourceIP, out DetectionBatch batch))
            {
                batch = new DetectionBatch();
                notification.Batches.Add(packetHeader.SourceIP, batch);
            }

            if (batch.Detections.Count == 0)
            {
                batch.FirstDetection = DateTime.UtcNow;
            }
            batch.Detections.Add(packetHeader);
        }

        // True if any batched notification still has batches to send or a rate limit to wait out.
        public static bool HasBatches =>
            Settings.Notifications.OfType<BatchedNotification>().Any(notification => notification.Batches.Count > 0);

        // Sends each batch that's due, and removes sources that have nothing left to send.
        // A source's batch is due once the wait time has passed since its first detection
        // and the rate limit has passed since its last batch was sent.
        public static void SendDueBatches()
        {
            DateTime now = DateTime.UtcNow;
            foreach (BatchedNotification notification in Settings.Notifications.OfType<BatchedNotification>())
            {
                foreach (KeyValuePair<IPAddress, DetectionBatch> pair in notification.Batches.ToList())
                {
                    DetectionBatch batch = pair.Value;
                    bool isWaitOver = now >= batch.FirstDetection.AddSeconds(notification.WaitSeconds);
                    bool isRateLimitOver = now >= batch.LastSent.AddSeconds(notification.RateLimitSeconds);

                    if (batch.Detections.Count > 0 && isWaitOver && isRateLimitOver)
                    {
                        SendBatch(notification, pair.Key, batch);
                    }
                    else if (batch.Detections.Count == 0 && isRateLimitOver)
                    {
                        notification.Batches.Remove(pair.Key);
                    }
                }
            }
        }

        // Sends every batch that's waiting, due or not, so stopping or reloading doesn't lose them.
        // Returns the sends, so the caller can wait for them.
        public static Task[] SendAllBatches()
        {
            List<Task> sends = new List<Task>();
            foreach (BatchedNotification notification in Settings.Notifications.OfType<BatchedNotification>())
            {
                foreach (KeyValuePair<IPAddress, DetectionBatch> pair in notification.Batches)
                {
                    if (pair.Value.Detections.Count > 0)
                    {
                        sends.Add(SendBatch(notification, pair.Key, pair.Value));
                    }
                }
            }
            return sends.ToArray();
        }

        // Starts sending the batch on another thread, then empties the batch. Each sender logs its own errors.
        private static Task SendBatch(BatchedNotification notification, IPAddress sourceIp, DetectionBatch batch)
        {
            // Copy the detections first, since the batch is emptied below while it's still being sent.
            List<PacketHeader> detections = batch.Detections.ToList();

            // Send on a thread-pool thread, so a slow DNS lookup or server doesn't hold up the detection queue.
            Task send;
            switch (notification)
            {
                case NotificationEmail email:
                    send = Task.Run(() => EmailNotifier.Send(email, sourceIp, detections));
                    break;
                default:
                    send = Task.CompletedTask;
                    break;
            }

            batch.Detections.Clear();
            batch.LastSent = DateTime.UtcNow;
            return send;
        }
    }
}
