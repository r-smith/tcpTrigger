using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;

namespace tcpTrigger
{
    // The configuration settings, loaded from tcpTrigger.xml.
    public static class Settings
    {
        internal const int IgnoreAll = 0;
        internal const int IgnoreIcmp = -1;

        // Full path of the configuration file that was loaded.
        public static string Path { get; private set; }
        public static bool IsMonitorTcpEnabled { get; private set; }
        public static bool IsMonitorUdpEnabled { get; private set; }
        public static bool IsMonitorIcmpEnabled { get; private set; }
        public static HashSet<ushort> TcpPortsToMonitor { get; private set; }
        public static string TcpPortsToIncludeAsString { get; private set; }
        public static string TcpPortsToExcludeAsString { get; private set; }
        public static HashSet<ushort> UdpPortsToMonitor { get; private set; }
        public static string UdpPortsToIncludeAsString { get; private set; }
        public static string UdpPortsToExcludeAsString { get; private set; }
        public static bool IsLogEnabled { get; set; }
        public static bool IsEventLogEnabled { get; private set; }
        public static bool IsEmailNotificationEnabled { get; private set; }
        public static bool IsExternalAppEnabled { get; private set; }
        public static int EmailRateLimitSeconds { get; private set; }
        public static int EmailBufferSeconds { get; private set; } = 30;
        public static string LogPath { get; private set; }
        public static string ExternalAppPath { get; private set; }
        public static string ExternalAppArguments { get; private set; }
        public static HashSet<string> ExcludedNetworkInterfaces { get; private set; } = new HashSet<string>();
        public static Dictionary<IPAddress, HashSet<int>> IgnoredEndpoints { get; private set; } = new Dictionary<IPAddress, HashSet<int>>();
        public static string TimestampFormat { get; private set; } = "yyyy-MM-dd HH:mm:ss";
        public static string EmailServer { get; private set; }
        public static int EmailServerPort { get; private set; }
        public static bool IsEmailTlsEnabled { get; private set; }
        public static bool IsEmailAuthRequired { get; private set; }
        public static string EmailUsername { get; private set; }
        public static string EmailPassword { get; private set; }
        public static List<string> EmailRecipients { get; private set; } = new List<string>();
        public static string EmailSender { get; private set; }
        public static string EmailSenderDisplayName { get; private set; }
        public static string EmailSubject { get; private set; }
        public static string EmailBody { get; private set; }

        // The XML node path currently being read from the configuration file.
        // Used to say which node caused an error when the file can't be parsed.
        private static string _currentNode;

        // Returns the configuration file path: in the executable's path if present, otherwise in ProgramData.
        // Returns an empty string if neither exists.
        private static string GetSettingsPath()
        {
            // Locate the tcpTrigger.xml configuration file.
            // First check the current directory. If not found, check ProgramData.
            // A typical installation will have the configuration stored in ProgramData.
            const string fileName = "tcpTrigger.xml";
            if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + fileName))
                return AppDomain.CurrentDomain.BaseDirectory + fileName;
            else if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\" + fileName))
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\" + fileName;
            else
                return string.Empty;
        }

        // Loads all settings from the configuration file. Throws if the file is missing or can't be parsed.
        public static void Load()
        {
            Path = GetSettingsPath();

            if (string.IsNullOrEmpty(Path))
            {
                // Error: A configuration file could not be found.
                throw new FileNotFoundException(
                    "A configuration file for tcpTrigger could not be found. Use 'tcpTrigger Manager.exe' to generate the file."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"First path checked: '{AppDomain.CurrentDomain.BaseDirectory + "tcpTrigger.xml"}'{Environment.NewLine}"
                    + $"Second path checked: '{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\tcpTrigger.xml'"}"
                    );
            }

            _currentNode = string.Empty;
            try
            {
                XmlDocument xd = new XmlDocument();
                xd.Load(Path);

                // tcpTrigger/enabledComponents
                IsMonitorTcpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsTcp, false);
                IsMonitorUdpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsUdp, false);
                IsMonitorIcmpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsIcmp, false);

                if (IsMonitorTcpEnabled)
                {
                    // tcpTrigger/monitoredPorts
                    TcpPortsToIncludeAsString = ReadString(xd, ConfigNames.MonitoredPortsTcpInclude);
                    HashSet<ushort> includePorts = ParsePorts(TcpPortsToIncludeAsString);

                    TcpPortsToExcludeAsString = ReadString(xd, ConfigNames.MonitoredPortsTcpExclude);
                    HashSet<ushort> excludePorts = ParsePorts(TcpPortsToExcludeAsString);

                    // Final set contains all included ports except those excluded.
                    includePorts.ExceptWith(excludePorts);
                    TcpPortsToMonitor = includePorts;
                }
                if (IsMonitorUdpEnabled)
                {
                    // tcpTrigger/monitoredPorts
                    UdpPortsToIncludeAsString = ReadString(xd, ConfigNames.MonitoredPortsUdpInclude);
                    HashSet<ushort> includePorts = ParsePorts(UdpPortsToIncludeAsString);

                    UdpPortsToExcludeAsString = ReadString(xd, ConfigNames.MonitoredPortsUdpExclude);
                    HashSet<ushort> excludePorts = ParsePorts(UdpPortsToExcludeAsString);

                    // Final set contains all included ports except those excluded.
                    includePorts.ExceptWith(excludePorts);
                    UdpPortsToMonitor = includePorts;
                }

                // tcpTrigger/endpointIgnoreList
                _currentNode = ConfigNames.EndpointIgnoreListIpAddress;
                var ignoredEndpoints = new Dictionary<IPAddress, HashSet<int>>();
                XmlNodeList nl = xd.DocumentElement.SelectNodes(_currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                    {
                        string portAttr = nl[i].Attributes["port"]?.Value;
                        int port = IgnoreAll;

                        if (!string.IsNullOrEmpty(portAttr))
                        {
                            if (string.Equals(portAttr, "icmp", StringComparison.OrdinalIgnoreCase))
                            {
                                port = IgnoreIcmp;
                            }
                            else if (!int.TryParse(portAttr, out port) || port < 1 || port > 65535)
                            {
                                throw new FormatException(
                                    $"'{portAttr}' is not a valid port for ignored IP address '{nl[i].InnerText}'. "
                                    + "Use a port number from 1 to 65535, or 'icmp'.");
                            }
                        }
                        if (IPAddress.TryParse(nl[i].InnerText, out IPAddress ip))
                        {
                            if (!ignoredEndpoints.ContainsKey(ip))
                            {
                                ignoredEndpoints.Add(ip, new HashSet<int>() { port });
                            }
                            else
                            {
                                ignoredEndpoints[ip].Add(port);
                            }
                        }
                    }
                }
                IgnoredEndpoints = ignoredEndpoints;

                // tcpTrigger/networkInterfaceExcludeList
                ExcludedNetworkInterfaces = new HashSet<string>(ReadList(xd, ConfigNames.NetworkInterfaceExcludeListDeviceGuid));

                // tcpTrigger/enabledActions
                IsLogEnabled = ReadBool(xd, ConfigNames.EnabledActionsLog, false);
                IsEventLogEnabled = ReadBool(xd, ConfigNames.EnabledActionsWindowsEventLog, false);
                IsEmailNotificationEnabled = ReadBool(xd, ConfigNames.EnabledActionsEmailNotification, false);
                IsExternalAppEnabled = ReadBool(xd, ConfigNames.EnabledActionsExecuteCommand, false);

                // tcpTrigger/actionSettings
                LogPath = ReadString(xd, ConfigNames.ActionSettingsLogPath);
                ExternalAppPath = ReadString(xd, ConfigNames.ActionSettingsCommandPath);
                ExternalAppArguments = ReadString(xd, ConfigNames.ActionSettingsCommandArguments);

                // tcpTrigger/email
                EmailServer = ReadString(xd, ConfigNames.EmailServer);
                EmailServerPort = ReadInt(xd, ConfigNames.EmailPort, 25);
                IsEmailTlsEnabled = ReadBool(xd, ConfigNames.EmailIsTlsEnabled, false);
                IsEmailAuthRequired = ReadBool(xd, ConfigNames.EmailIsAuthRequired, false);
                EmailUsername = ReadCredential(xd, ConfigNames.EmailUsername);
                EmailPassword = ReadCredential(xd, ConfigNames.EmailPassword);
                EmailRecipients = ReadList(xd, ConfigNames.EmailRecipientListAddress);
                EmailSender = ReadString(xd, ConfigNames.EmailSenderAddress);
                EmailSenderDisplayName = ReadString(xd, ConfigNames.EmailSenderDisplayName);

                // tcpTrigger/email/message
                EmailSubject = ReadString(xd, ConfigNames.EmailMessageSubject);
                EmailBody = ReadString(xd, ConfigNames.EmailMessageBody);

                // tcpTrigger/email/options
                EmailRateLimitSeconds = Math.Max(1, ReadInt(xd, ConfigNames.EmailOptionsRateLimitSeconds, 300));
                EmailBufferSeconds = Math.Max(1, ReadInt(xd, ConfigNames.EmailOptionsBufferSeconds, 30));
            }

            catch (Exception ex)
            {
                // Error: Failed to parse XML configuration.
                throw new FileLoadException(
                    $"Unable to parse the configuration file '{Path}'."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"Error parsing XML node: '{_currentNode}'."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + ex.Message
                    );
            }
        }

        // Returns the text of the XML node at path, or null if the node doesn't exist.
        private static string ReadString(XmlDocument xd, string path)
        {
            _currentNode = path;
            return xd.DocumentElement.SelectSingleNode(path)?.InnerText;
        }

        // Returns the node at path as a bool, or defaultValue if the node is missing or empty.
        private static bool ReadBool(XmlDocument xd, string path, bool defaultValue)
        {
            string text = ReadString(xd, path);
            return string.IsNullOrWhiteSpace(text) ? defaultValue : bool.Parse(text);
        }

        // Returns the node at path as an int, or defaultValue if the node is missing or empty.
        private static int ReadInt(XmlDocument xd, string path, int defaultValue)
        {
            string text = ReadString(xd, path);
            return string.IsNullOrWhiteSpace(text) ? defaultValue : int.Parse(text);
        }

        // Returns the text of the node at path, decrypting it if the node has encrypted="true".
        // Returns null if the node is missing or empty.
        private static string ReadCredential(XmlDocument xd, string path)
        {
            _currentNode = path;
            XmlNode node = xd.DocumentElement.SelectSingleNode(path);
            if (node == null || string.IsNullOrEmpty(node.InnerText))
                return null;

            return node.Attributes["encrypted"]?.InnerText == "true"
                ? StringCipher.Decrypt(node.InnerText)
                : node.InnerText;
        }

        // Returns the text of every XML node at path, skipping empty ones.
        private static List<string> ReadList(XmlDocument xd, string path)
        {
            _currentNode = path;
            var values = new List<string>();
            foreach (XmlNode node in xd.DocumentElement.SelectNodes(path))
            {
                if (!string.IsNullOrEmpty(node.InnerText))
                    values.Add(node.InnerText);
            }
            return values;
        }

        // Parses a comma-separated list of ports and port ranges, such as "22,80,8000-8100".
        // Returns an empty set if the list is null or empty.
        private static HashSet<ushort> ParsePorts(string portList)
        {
            var ports = new HashSet<ushort>();
            if (string.IsNullOrWhiteSpace(portList))
                return ports;

            foreach (string part in portList.Split(','))
            {
                string[] range = part.Split('-');
                ushort start = ushort.Parse(range[0]);
                ushort end = ushort.Parse(range[range.Length - 1]);
                if (start > end)
                    throw new FormatException($"Port range '{part.Trim()}' has its start after its end.");

                // Use an int counter (a ushort would wrap from 65535 back to 0).
                for (int port = start; port <= end; port++)
                    ports.Add((ushort)port);
            }
            return ports;
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
            sb.AppendLine("Detect incoming ICMP: " + (IsMonitorIcmpEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Detect incoming TCP: " + (IsMonitorTcpEnabled ? "Enabled" : "Disabled"));
            if (IsMonitorTcpEnabled)
            {
                sb.AppendLine($"Including TCP port(s): {TcpPortsToIncludeAsString}");
                sb.AppendLine($"Excluding TCP port(s): {TcpPortsToExcludeAsString}");
            }
            sb.AppendLine("Detect incoming UDP: " + (IsMonitorUdpEnabled ? "Enabled" : "Disabled"));
            if (IsMonitorUdpEnabled)
            {
                sb.AppendLine($"Including UDP port(s): {UdpPortsToIncludeAsString}");
                sb.AppendLine($"Excluding UDP port(s): {UdpPortsToExcludeAsString}");
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
                sb.AppendLine($"Server: {EmailServer}");
                sb.AppendLine($"Port: {EmailServerPort}");
                sb.AppendLine($"SSL / TLS: {(IsEmailTlsEnabled ? "Enabled" : "Disabled")}");
                sb.AppendLine("Use authentication? " + (IsEmailAuthRequired ? "Yes" : "No"));
                sb.AppendLine("Recipient(s): " + string.Join(", ", EmailRecipients.ToArray()));
                sb.AppendLine($"Sender address: {EmailSender}");
                sb.AppendLine($"Sender display name: {EmailSenderDisplayName}");
                sb.AppendLine($"Message subject: {EmailSubject}");
                sb.AppendLine($"Rate limit: {EmailRateLimitSeconds} seconds");
                sb.AppendLine($"Buffer: {EmailBufferSeconds} seconds");
            }

            return sb.ToString();
        }
    }
}
