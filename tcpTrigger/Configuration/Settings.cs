using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tcpTrigger
{
    // The configuration settings, loaded from tcpTrigger.xml.
    internal static class Settings
    {
        internal const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

        // Full path of the configuration file that was loaded.
        public static string Path { get; internal set; }
        public static bool IsDetectTcpEnabled { get; internal set; }
        public static bool IsDetectUdpEnabled { get; internal set; }
        public static bool IsDetectIcmpEnabled { get; internal set; }

        // Legacy <monitoredPorts>, version 1.3.5 and earlier.
        public static HashSet<ushort> TcpPortsToMonitor { get; internal set; }
        public static string TcpPortsToInclude { get; internal set; }
        public static string TcpPortsToExclude { get; internal set; }
        public static HashSet<ushort> UdpPortsToMonitor { get; internal set; }
        public static string UdpPortsToInclude { get; internal set; }
        public static string UdpPortsToExclude { get; internal set; }

        public static HashSet<string> ExcludedNetworkInterfaces { get; internal set; } = new HashSet<string>();
        public static List<Notification> Notifications { get; internal set; } = new List<Notification>();
        public static List<IgnoreRule> IgnoreRules { get; internal set; } = new List<IgnoreRule>();

        // Loads all settings from the configuration file. Throws if the file is missing or can't be parsed.
        public static void Load()
        {
            ConfigReader.Load();
        }

        // Returns the settings as readable text for the event log.
        public static string DumpToString()
        {
            StringBuilder sb = new StringBuilder();

            // Append configuration path.
            sb.AppendLine("# Configuration file");
            sb.AppendLine($"'{Path}'");

            // Append network interface exclusions.
            if (ExcludedNetworkInterfaces.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# Excluded network interfaces");
                foreach (string guid in ExcludedNetworkInterfaces)
                {
                    sb.AppendLine("Do not monitor interface: " + guid);
                }
            }

            // Append monitoring configuration.
            sb.AppendLine();
            sb.AppendLine("# Detect");
            sb.AppendLine("Detect incoming ICMP: " + (IsDetectIcmpEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Detect incoming TCP: " + (IsDetectTcpEnabled ? "Enabled" : "Disabled"));
            if (IsDetectTcpEnabled)
            {
                sb.AppendLine($"Including TCP port(s): {TcpPortsToInclude}");
                sb.AppendLine($"Excluding TCP port(s): {TcpPortsToExclude}");
            }
            sb.AppendLine("Detect incoming UDP: " + (IsDetectUdpEnabled ? "Enabled" : "Disabled"));
            if (IsDetectUdpEnabled)
            {
                sb.AppendLine($"Including UDP port(s): {UdpPortsToInclude}");
                sb.AppendLine($"Excluding UDP port(s): {UdpPortsToExclude}");
            }

            // Append notifications.
            sb.AppendLine();
            sb.AppendLine("# Notify");
            if (Notifications.Count == 0)
            {
                sb.AppendLine("None");
            }
            foreach (Notification notification in Notifications)
            {
                sb.AppendLine(string.IsNullOrEmpty(notification.Name) ? "Unnamed notification" : notification.Name);
                switch (notification)
                {
                    case NotificationLogFile logFile:
                        sb.AppendLine("  Type: Log file");
                        sb.AppendLine($"  Path: {logFile.Path}");
                        break;
                    case NotificationCommand command:
                        sb.AppendLine("  Type: Command");
                        sb.AppendLine($"  Path: {command.Path}");
                        sb.AppendLine($"  Arguments: {command.Arguments}");
                        break;
                    case NotificationEmail email:
                        sb.AppendLine("  Type: Email");
                        sb.AppendLine($"  Server: {email.SmtpServer}:{email.Port}");
                        sb.AppendLine($"  STARTTLS: {(email.IsStartTlsEnabled ? "Enabled" : "Disabled")}");
                        sb.AppendLine("  Authentication: " + (string.IsNullOrWhiteSpace(email.Username) ? "Disabled" : "Enabled"));
                        sb.AppendLine("  Recipients: " + string.Join(", ", email.Recipients));
                        sb.AppendLine($"  From: {email.FromAddress}");
                        sb.AppendLine($"  From display name: {email.FromDisplayName}");
                        sb.AppendLine($"  Subject: {email.Subject}");
                        sb.AppendLine($"  Rate limit: {email.RateLimitSeconds} seconds");
                        sb.AppendLine($"  Wait: {email.WaitSeconds} seconds");
                        break;
                }
            }

            // Append ignore rules.
            if (IgnoreRules.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# Ignore");
                foreach (IgnoreRule rule in IgnoreRules)
                {
                    sb.AppendLine(string.IsNullOrEmpty(rule.Name) ? "Unnamed rule" : rule.Name);
                    sb.AppendLine($"  Sources: {ListEntries(rule.Sources)}");
                    sb.AppendLine($"  Destinations: {ListEntries(rule.Destinations)}");
                    sb.AppendLine($"  Services: {ListEntries(rule.Services)}");
                }
            }

            return sb.ToString();
        }

        // Lists the first 5 entries, then the number of entries not shown.
        private static string ListEntries(List<string> entries)
        {
            const int MaxShown = 5;
            string shown = string.Join(", ", entries.Take(MaxShown));
            return entries.Count > MaxShown ? $"{shown} (+{entries.Count - MaxShown} more)" : shown;
        }
    }
}