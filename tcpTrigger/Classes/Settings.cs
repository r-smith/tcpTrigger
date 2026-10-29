using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;

namespace tcpTrigger
{
    public static class Settings
    {
        internal const int IgnoreAll = 0;
        internal const int IgnoreIcmp = -1;

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

            // currentNode is updated with the XML node path for every element that is read
            // from the configuration file. This aids in debugging and provides more useful
            // error messages when something goes wrong.
            string currentNode = string.Empty;
            try
            {
                XmlDocument xd = new XmlDocument();
                xd.Load(Path);

                // tcpTrigger/enabledComponents
                IsMonitorTcpEnabled = ReadBool(xd, SettingsNode.enabledComponents_tcp, false);
                IsMonitorUdpEnabled = ReadBool(xd, SettingsNode.enabledComponents_udp, false);
                IsMonitorIcmpEnabled = ReadBool(xd, SettingsNode.enabledComponents_icmp, false);

                if (IsMonitorTcpEnabled)
                {
                    // tcpTrigger/monitoredPorts
                    TcpPortsToIncludeAsString = ReadString(xd, SettingsNode.monitoredPorts_tcp_include);
                    HashSet<ushort> includePorts = ParsePorts(TcpPortsToIncludeAsString);

                    TcpPortsToExcludeAsString = ReadString(xd, SettingsNode.monitoredPorts_tcp_exclude);
                    HashSet<ushort> excludePorts = ParsePorts(TcpPortsToExcludeAsString);

                    // Final set contains all included ports except those excluded.
                    includePorts.ExceptWith(excludePorts);
                    TcpPortsToMonitor = includePorts;
                }
                if (IsMonitorUdpEnabled)
                {
                    // tcpTrigger/monitoredPorts
                    UdpPortsToIncludeAsString = ReadString(xd, SettingsNode.monitoredPorts_udp_include);
                    HashSet<ushort> includePorts = ParsePorts(UdpPortsToIncludeAsString);

                    UdpPortsToExcludeAsString = ReadString(xd, SettingsNode.monitoredPorts_udp_exclude);
                    HashSet<ushort> excludePorts = ParsePorts(UdpPortsToExcludeAsString);

                    // Final set contains all included ports except those excluded.
                    includePorts.ExceptWith(excludePorts);
                    UdpPortsToMonitor = includePorts;
                }

                // tcpTrigger/endpointIgnoreList
                _currentNode = SettingsNode.endpointIgnoreList_ipAddress;
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
                            if (portAttr.ToLower().Equals("icmp"))
                            {
                                port = IgnoreIcmp;
                            }
                            else if (!int.TryParse(portAttr, out port) || port < 1 || port > 65535)
                            {
                                port = IgnoreAll;
                            }
                        }
                        if (IPAddress.TryParse(nl[i].InnerText, out IPAddress ip))
                        {
                            if (!ignoredEndpoints.ContainsKey(ip))
                            {
                                ignoredEndpoints.Add(ip, new HashSet<int>() { port } );
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
                ExcludedNetworkInterfaces = new HashSet<string>(ReadList(xd, SettingsNode.networkInterfaceExcludeList_deviceGuid));

                // tcpTrigger/enabledActions
                IsLogEnabled = ReadBool(xd, SettingsNode.enabledActions_log, false);
                IsEventLogEnabled = ReadBool(xd, SettingsNode.enabledActions_windowsEventLog, false);
                IsEmailNotificationEnabled = ReadBool(xd, SettingsNode.enabledActions_emailNotification, false);
                IsExternalAppEnabled = ReadBool(xd, SettingsNode.enabledActions_executeCommand, false);

                // tcpTrigger/actionSettings
                LogPath = ReadString(xd, SettingsNode.actionsSettings_logPath);
                ExternalAppPath = ReadString(xd, SettingsNode.actionsSettings_command_path);
                ExternalAppArguments = ReadString(xd, SettingsNode.actionsSettings_command_arguments);

                // tcpTrigger/email
                EmailServer = ReadString(xd, SettingsNode.email_server);
                EmailServerPort = ReadInt(xd, SettingsNode.email_port, 25);
                IsEmailTlsEnabled = ReadBool(xd, SettingsNode.email_isTlsEnabled, false);
                IsEmailAuthRequired = ReadBool(xd, SettingsNode.email_isAuthRequired, false);
                EmailUsername = ReadCredential(xd, SettingsNode.email_username);
                EmailPassword = ReadCredential(xd, SettingsNode.email_password);
                EmailRecipients = ReadList(xd, SettingsNode.email_recipientList_address);
                EmailSender = ReadString(xd, SettingsNode.email_sender_address);
                EmailSenderDisplayName = ReadString(xd, SettingsNode.email_sender_displayName);

                // tcpTrigger/email/message
                EmailSubject = ReadString(xd, SettingsNode.email_message_subject);
                EmailBody = ReadString(xd, SettingsNode.email_message_body);

                // tcpTrigger/email/options
                EmailRateLimitSeconds = Math.Max(1, ReadInt(xd, SettingsNode.email_options_rateLimitSeconds, 300));
                EmailBufferSeconds = Math.Max(1, ReadInt(xd, SettingsNode.email_options_bufferSeconds, 30));
            }

            catch (Exception ex)
            {
                // Error: Failed to parse XML configuration.
                throw new FileLoadException(
                    $"Unable to parse the configuration file '{Path}'."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"Error parsing XML node: '{currentNode}'."
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
                sb.AppendLine($"Rate limit: " + (EmailRateLimitSeconds > 0 ? EmailRateLimitSeconds.ToString() + " seconds" : "Disabled"));
                sb.AppendLine($"Buffer: " + (EmailBufferSeconds > 0 ? EmailBufferSeconds.ToString() + " seconds" : "Disabled"));
            }

            return sb.ToString();
        }
    }

    internal class SettingsNode
    {
        // XML node paths for the tcpTrigger configuration file.
        public const string enabledComponents_tcp = "/tcpTrigger/enabledComponents/tcp";
        public const string enabledComponents_udp = "/tcpTrigger/enabledComponents/udp";
        public const string enabledComponents_icmp = "/tcpTrigger/enabledComponents/icmp";
        public const string monitoredPorts_tcp_include = "/tcpTrigger/monitoredPorts/tcp/include";
        public const string monitoredPorts_tcp_exclude = "/tcpTrigger/monitoredPorts/tcp/exclude";
        public const string monitoredPorts_udp_include = "/tcpTrigger/monitoredPorts/udp/include";
        public const string monitoredPorts_udp_exclude = "/tcpTrigger/monitoredPorts/udp/exclude";
        public const string endpointIgnoreList_ipAddress = "/tcpTrigger/endpointIgnoreList/ipAddress";
        public const string networkInterfaceExcludeList_deviceGuid = "/tcpTrigger/networkInterfaceExcludeList/deviceGuid";
        public const string enabledActions_log = "/tcpTrigger/enabledActions/log";
        public const string enabledActions_windowsEventLog = "/tcpTrigger/enabledActions/windowsEventLog";
        public const string enabledActions_emailNotification = "/tcpTrigger/enabledActions/emailNotification";
        public const string enabledActions_popupNotification = "/tcpTrigger/enabledActions/popupNotification";
        public const string enabledActions_executeCommand = "/tcpTrigger/enabledActions/executeCommand";
        public const string actionsSettings_logPath = "/tcpTrigger/actionSettings/logPath";
        public const string actionsSettings_command_path = "/tcpTrigger/actionSettings/command/path";
        public const string actionsSettings_command_arguments = "/tcpTrigger/actionSettings/command/arguments";
        public const string email_server = "/tcpTrigger/email/server";
        public const string email_port = "/tcpTrigger/email/port";
        public const string email_isTlsEnabled = "/tcpTrigger/email/isTlsEnabled";
        public const string email_isAuthRequired = "/tcpTrigger/email/isAuthRequired";
        public const string email_username = "/tcpTrigger/email/username";
        public const string email_password = "/tcpTrigger/email/password";
        public const string email_recipientList_address = "/tcpTrigger/email/recipientList/address";
        public const string email_sender_address = "/tcpTrigger/email/sender/address";
        public const string email_sender_displayName = "/tcpTrigger/email/sender/displayName";
        public const string email_message_subject = "/tcpTrigger/email/message/subject";
        public const string email_message_body = "/tcpTrigger/email/message/body";
        public const string email_options_rateLimitSeconds = "/tcpTrigger/email/options/rateLimitSeconds";
        public const string email_options_bufferSeconds = "/tcpTrigger/email/options/bufferSeconds";
    }
}
