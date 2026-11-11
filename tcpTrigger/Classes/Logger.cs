using System;
using System.Diagnostics;

namespace tcpTrigger
{
    // Writes to the tcpTrigger log in the Windows event log.
    internal static class Logger
    {
        // Event IDs used in the tcpTrigger event log.
        // 90s: service. 100s: configuration and network. 200s: detections.
        // 300s: warnings. 400s: errors.
        public enum EventCode
        {
            ServiceStarted = 90,
            ServiceStopped = 91,
            ServiceStoppedOnError = 92,
            ServiceShuttingDown = 93,
            ConfigurationApplied = 100,
            NetworkChangeDetected = 101,
            NetworkInterfaces = 102,
            ConfigurationReloaded = 103,
            LogRotated = 104,
            InterfaceMonitoringResumed = 105,
            EmailSent = 110,
            MatchedIcmp = 200,
            MatchedTcp = 201,
            MatchedUdp = 202,
            LogPathInaccessible = 300,
            LaunchAppPathMissing = 301,
            EmailSettingsIncomplete = 302,
            StartFailed = 400,
            ReloadFailed = 401,
            QueuedActionsDropped = 402,
            InterfaceBindFailed = 410,
            InterfaceMonitoringStopped = 411,
            NoInterfacesMonitored = 412,
            NetworkCheckFailed = 413,
            LogWriteFailed = 420,
            LogRotationFailed = 421,
            EmailSendFailed = 430,
            LaunchAppFailed = 440,
            PacketProcessingFailed = 450,
            ActionFailed = 451,
        }

        // The event log rejects messages longer than 31,839 bytes. Leave room for the message.
        private const int MaxMessageLength = 31000;

        public static void Write(string message, EventCode eventCode)
        {
            WriteEventLog(message, EventLogEntryType.Information, eventCode);
        }

        public static void WriteWarning(string message, EventCode eventCode)
        {
            WriteEventLog(message, EventLogEntryType.Warning, eventCode);
        }

        public static void WriteError(string message, EventCode eventCode)
        {
            WriteEventLog(message, EventLogEntryType.Error, eventCode);
        }

        private static void WriteEventLog(string message, EventLogEntryType type, EventCode eventCode)
        {
            if (message.Length > MaxMessageLength)
            {
                message = message.Substring(0, MaxMessageLength) + $"{Environment.NewLine}{Environment.NewLine}(Message truncated.)";
            }

            try
            {
                EventLog.WriteEntry("tcpTrigger", message, type, (int)eventCode);
            }
            catch
            {
                // The event log couldn't be written.
            }
        }
    }
}