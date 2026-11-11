using System.Diagnostics;

namespace tcpTrigger
{
    // Writes to the tcpTrigger log in the Windows event log.
    internal static class Logger
    {
        // Event IDs used in the tcpTrigger event log.
        public enum EventCode
        {
            ServiceStarted = 90,
            ServiceStopped = 91,
            ConfigurationApplied = 100,
            NetworkChangeDetected = 101,
            NetworkInterfaces = 102,
            ConfigurationReloaded = 103,
            MatchedIcmp = 200,
            MatchedTcp = 201,
            MatchedUdp = 202,
            Error = 400,
        }

        public static void Write(string message, EventCode eventCode)
        {
            WriteEventLog(message, EventLogEntryType.Information, eventCode);
        }

        public static void WriteError(string message, EventCode eventCode)
        {
            WriteEventLog(message, EventLogEntryType.Error, eventCode);
        }

        private static void WriteEventLog(string message, EventLogEntryType type, EventCode eventCode)
        {
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