using System.Collections.Generic;
using System.Net;
using System.Text;

namespace tcpTrigger
{
    // The configuration settings, loaded from tcpTrigger.xml.
    public static class Settings
    {
        internal const int IgnoreAll = 0;
        internal const int IgnoreIcmp = -1;

        // Full path of the configuration file that was loaded.
        public static string Path { get; internal set; }
        public static bool IsDetectTcpEnabled { get; internal set; }
        public static bool IsDetectUdpEnabled { get; internal set; }
        public static bool IsDetectIcmpEnabled { get; internal set; }
        public static HashSet<ushort> TcpPortsToMonitor { get; internal set; }
        public static string TcpPortsToInclude { get; internal set; }
        public static string TcpPortsToExclude { get; internal set; }
        public static HashSet<ushort> UdpPortsToMonitor { get; internal set; }
        public static string UdpPortsToInclude { get; internal set; }
        public static string UdpPortsToExclude { get; internal set; }
        public static bool IsLogEnabled { get; set; }
        public static bool IsEventLogEnabled { get; internal set; }
        public static bool IsEmailNotificationEnabled { get; internal set; }
        public static bool IsExternalAppEnabled { get; internal set; }
        public static int EmailRateLimitSeconds { get; internal set; }
        public static int EmailBufferSeconds { get; internal set; } = 30;
        public static string LogPath { get; internal set; }
        public static string ExternalAppPath { get; internal set; }
        public static string ExternalAppArguments { get; internal set; }
        public static HashSet<string> ExcludedNetworkInterfaces { get; internal set; } = new HashSet<string>();
        public static Dictionary<IPAddress, HashSet<int>> IgnoredEndpoints { get; internal set; } = new Dictionary<IPAddress, HashSet<int>>();
        public static string TimestampFormat { get; private set; } = "yyyy-MM-dd HH:mm:ss";
        public static string EmailSmtpServer { get; internal set; }
        public static int EmailPort { get; internal set; }
        public static bool IsEmailStartTlsEnabled { get; internal set; }
        public static bool IsEmailAuthRequired { get; internal set; }
        public static string EmailUsername { get; internal set; }
        public static string EmailPassword { get; internal set; }
        public static List<string> EmailRecipients { get; internal set; } = new List<string>();
        public static string EmailFromAddress { get; internal set; }
        public static string EmailFromDisplayName { get; internal set; }
        public static string EmailSubject { get; internal set; }
        public static string EmailBody { get; internal set; }

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
            sb.AppendLine("# Rules");
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

            // Append endpoint ignore list.
            if (IgnoredEndpoints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# Ignored endpoints");
                foreach (KeyValuePair<IPAddress, HashSet<int>> pair in IgnoredEndpoints)
                {
                    foreach (int port in pair.Value)
                    {
                        sb.Append($"Ignore IP: {pair.Key}");
                        if (port == IgnoreIcmp)
                            sb.AppendLine(":icmp");
                        else if (port == IgnoreAll)
                            sb.AppendLine();
                        else
                            sb.AppendLine($":{port}");
                    }
                }
            }

            // Append enabled actions and settings.
            sb.AppendLine();
            sb.AppendLine("# Actions");
            sb.AppendLine("Write to text log: " + (IsLogEnabled ? "Enabled" : "Disabled"));
            if (IsLogEnabled)
                sb.AppendLine($"Log path: '{LogPath}'");
            sb.AppendLine("Write to Windows event log: " + (IsEventLogEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Email notifications: " + (IsEmailNotificationEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Launch external application: " + (IsExternalAppEnabled ? "Enabled" : "Disabled"));
            if (IsExternalAppEnabled)
            {
                sb.AppendLine($"Launch app path: '{ExternalAppPath}'");
                sb.AppendLine($"Launch app args: '{ExternalAppArguments}'");
            }

            // Append email settings.
            if (IsEmailNotificationEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("# Email");
                sb.AppendLine($"Server: {EmailSmtpServer}");
                sb.AppendLine($"Port: {EmailPort}");
                sb.AppendLine($"SSL / TLS: {(IsEmailStartTlsEnabled ? "Enabled" : "Disabled")}");
                sb.AppendLine("Use authentication? " + (IsEmailAuthRequired ? "Yes" : "No"));
                sb.AppendLine("Recipient(s): " + string.Join(", ", EmailRecipients.ToArray()));
                sb.AppendLine($"Sender address: {EmailFromAddress}");
                sb.AppendLine($"Sender display name: {EmailFromDisplayName}");
                sb.AppendLine($"Message subject: {EmailSubject}");
                sb.AppendLine($"Rate limit: {EmailRateLimitSeconds} seconds");
                sb.AppendLine($"Buffer: {EmailBufferSeconds} seconds");
            }

            return sb.ToString();
        }
    }
}