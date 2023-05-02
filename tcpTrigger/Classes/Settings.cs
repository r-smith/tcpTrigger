using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;

namespace tcpTrigger
{
    public static class Settings
    {
        internal const int IgnoreAll = 0;
        internal const int IgnoreIcmp = -1;

        public static ReaderWriterLockSlim FileLock { get; private set; } = new ReaderWriterLockSlim();
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

                XmlNode xn;
                XmlNodeList nl;
                string encryptedValue;
                // tcpTrigger/enabledComponents
                currentNode = SettingsNode.enabledComponents_tcp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsMonitorTcpEnabled = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledComponents_udp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsMonitorUdpEnabled = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledComponents_icmp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsMonitorIcmpEnabled = bool.Parse(xn.InnerText); }

                if (IsMonitorTcpEnabled)
                {
                    // tcpTrigger/monitoredPorts
                    List<ushort> includePorts = new List<ushort>();
                    List<ushort> excludePorts = new List<ushort>();
                    // Included ports.
                    currentNode = SettingsNode.monitoredPorts_tcp_include;
                    TcpPortsToIncludeAsString = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                    if (!string.IsNullOrEmpty(TcpPortsToIncludeAsString))
                    {
                        includePorts = (from part in TcpPortsToIncludeAsString.Split(',')
                                        let range = part.Split('-')
                                        let start = ushort.Parse(range[0])
                                        let end = ushort.Parse(range[range.Length - 1])
                                        from i in Enumerable.Range(start, end - start + 1)
                                        orderby i
                                        select (ushort)i).Distinct().ToList();
                    }
                    // Excluded ports.
                    currentNode = SettingsNode.monitoredPorts_tcp_exclude;
                    TcpPortsToExcludeAsString = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                    if (!string.IsNullOrEmpty(TcpPortsToExcludeAsString))
                    {
                        excludePorts = (from part in TcpPortsToExcludeAsString.Split(',')
                                        let range = part.Split('-')
                                        let start = ushort.Parse(range[0])
                                        let end = ushort.Parse(range[range.Length - 1])
                                        from i in Enumerable.Range(start, end - start + 1)
                                        orderby i
                                        select (ushort)i).Distinct().ToList();
                    }
                    // Final HashSet will contain all included ports except those excluded.
                    TcpPortsToMonitor = new HashSet<ushort>(includePorts.Except(excludePorts));
                }
                if (IsMonitorUdpEnabled)
                {
                    // tcpTrigger/monitoredPorts
                    List<ushort> includePorts = new List<ushort>();
                    List<ushort> excludePorts = new List<ushort>();
                    // Included ports.
                    currentNode = SettingsNode.monitoredPorts_udp_include;
                    UdpPortsToIncludeAsString = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                    if (!string.IsNullOrEmpty(UdpPortsToIncludeAsString))
                    {
                        includePorts = (from part in UdpPortsToIncludeAsString.Split(',')
                                        let range = part.Split('-')
                                        let start = ushort.Parse(range[0])
                                        let end = ushort.Parse(range[range.Length - 1])
                                        from i in Enumerable.Range(start, end - start + 1)
                                        orderby i
                                        select (ushort)i).Distinct().ToList();
                    }
                    // Excluded ports.
                    currentNode = SettingsNode.monitoredPorts_udp_exclude;
                    UdpPortsToExcludeAsString = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                    if (!string.IsNullOrEmpty(UdpPortsToExcludeAsString))
                    {
                        excludePorts = (from part in UdpPortsToExcludeAsString.Split(',')
                                        let range = part.Split('-')
                                        let start = ushort.Parse(range[0])
                                        let end = ushort.Parse(range[range.Length - 1])
                                        from i in Enumerable.Range(start, end - start + 1)
                                        orderby i
                                        select (ushort)i).Distinct().ToList();
                    }
                    // Final HashSet will contain all included ports except those excluded.
                    UdpPortsToMonitor = new HashSet<ushort>(includePorts.Except(excludePorts));
                }

                // tcpTrigger/endpointIgnoreList
                currentNode = SettingsNode.endpointIgnoreList_ipAddress;
                nl = xd.DocumentElement.SelectNodes(currentNode);
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
                            if (!IgnoredEndpoints.ContainsKey(ip))
                            {
                                IgnoredEndpoints.Add(ip, new HashSet<int>() { port } );
                            }
                            else
                            {
                                IgnoredEndpoints[ip].Add(port);
                            }
                        }
                    }
                }

                // tcpTrigger/networkInterfaceExcludeList
                currentNode = SettingsNode.networkInterfaceExcludeList_deviceGuid;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        ExcludedNetworkInterfaces.Add(nl[i].InnerText);
                }

                // tcpTrigger/enabledActions
                currentNode = SettingsNode.enabledActions_log;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsLogEnabled = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledActions_windowsEventLog;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsEventLogEnabled = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledActions_emailNotification;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsEmailNotificationEnabled = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledActions_executeCommand;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsExternalAppEnabled = bool.Parse(xn.InnerText); }

                // tcpTrigger/actionSettings
                currentNode = SettingsNode.actionsSettings_logPath;
                LogPath = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = SettingsNode.actionsSettings_command_path;
                ExternalAppPath = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = SettingsNode.actionsSettings_command_arguments;
                ExternalAppArguments = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/email
                currentNode = SettingsNode.email_server;
                EmailServer = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                currentNode = SettingsNode.email_port;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null && !string.IsNullOrEmpty(xn.InnerText)) { EmailServerPort = int.Parse(xn.InnerText); }
                else { EmailServerPort = 25; }

                currentNode = SettingsNode.email_isTlsEnabled;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsEmailTlsEnabled = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.email_isAuthRequired;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsEmailAuthRequired = bool.Parse(xn.InnerText); }

                // Decrypt username.
                currentNode = SettingsNode.email_username;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null)
                {
                    if (xn.Attributes["encrypted"]?.InnerText == "true")
                    {
                        encryptedValue = xn.InnerText;
                        if (!string.IsNullOrEmpty(encryptedValue))
                            EmailUsername = StringCipher.Decrypt(encryptedValue);
                        encryptedValue = null;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(xn.InnerText))
                            EmailUsername = xn.InnerText;
                    }
                }

                // Decrypt password.
                currentNode = SettingsNode.email_password;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null)
                {
                    if (xn.Attributes["encrypted"]?.InnerText == "true")
                    {
                        encryptedValue = xn.InnerText;
                        if (!string.IsNullOrEmpty(encryptedValue))
                            EmailPassword = StringCipher.Decrypt(encryptedValue);
                        encryptedValue = null;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(xn.InnerText))
                            EmailPassword = xn.InnerText;
                    }
                }

                currentNode = SettingsNode.email_recipientList_address;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        EmailRecipients.Add(nl[i].InnerText);
                }

                currentNode = SettingsNode.email_sender_address;
                EmailSender = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = SettingsNode.email_sender_displayName;
                EmailSenderDisplayName = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/email/message
                currentNode = SettingsNode.email_message_subject;
                EmailSubject = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = SettingsNode.email_message_body;
                EmailBody = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/email/options
                currentNode = SettingsNode.email_options_rateLimitSeconds;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null && !string.IsNullOrEmpty(xn.InnerText)) { EmailRateLimitSeconds = int.Parse(xn.InnerText); }
                else { EmailRateLimitSeconds = 300; }
                if (EmailRateLimitSeconds <= 0) { EmailRateLimitSeconds = 1; }
                currentNode = SettingsNode.email_options_bufferSeconds;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null && !string.IsNullOrEmpty(xn.InnerText)) { EmailBufferSeconds = int.Parse(xn.InnerText); }
                else { EmailBufferSeconds = 30; }
                if (EmailBufferSeconds <= 0) { EmailBufferSeconds = 1; }
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
