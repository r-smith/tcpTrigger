using System.Diagnostics;

namespace tcpTrigger
{
    public static class Logger
    {
        public enum EventCode
        {
            ServiceStarted = 90,
            ServiceStopped = 91,
            ConfigurationApplied = 100,
            NetworkChangeDetected = 101,
            Error = 400,
            MatchedIcmp = 200,
            MatchedTcp = 201,
            MatchedUdp = 202,
            MatchedDhcp = 203,
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
            EventLog.WriteEntry("tcpTrigger", message, type, (int)eventCode);
        }
    }
}
