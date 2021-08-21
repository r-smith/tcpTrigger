using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Xml;

namespace tcpTrigger
{
    public static class Settings
    {
        public static ReaderWriterLockSlim FileLock { get; private set; } = new ReaderWriterLockSlim();
        public static string Path { get; private set; }
        public static bool IsMonitorTcpEnabled { get; private set; }
        public static bool IsMonitorUdpEnabled { get; private set; }
        public static bool IsMonitorIcmpEnabled { get; private set; }
        public static bool IsMonitorDhcpEnabled { get; private set; }
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
        public static int EmailBufferSeconds { get; private set; } = 15;
        public static string LogPath { get; private set; }
        public static string ExternalAppPath { get; private set; }
        public static string ExternalAppArguments { get; private set; }
        public static HashSet<string> ExcludedNetworkInterfaces { get; private set; } = new HashSet<string>();
        public static HashSet<IPAddress> IgnoredDhcpServers { get; private set; } = new HashSet<IPAddress>();
        public static HashSet<IPAddress> IgnoredEndpoints { get; private set; } = new HashSet<IPAddress>();
        public static string TimestampFormat { get; private set; } = "yyyy-MM-dd HH:mm:ss";
        public static string EmailServer { get; private set; }
        public static int EmailServerPort { get; private set; }
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

        public static bool Load()
        {
            Path = GetSettingsPath();
            
            if (string.IsNullOrEmpty(Path))
            {
                // No configuration was found. Log a warning in the Windows event log and
                // return true (successful load) to allow the tcpTrigger service to start.
                // In this state, the tcpTrigger service will start and run with everything disabled.
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Failed to start the tcpTrigger service. A settings file is required, but one could not be located. Use 'tcpTrigger Manager.exe' to generate the file."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"First path checked: '{AppDomain.CurrentDomain.BaseDirectory + "tcpTrigger.xml"}'{Environment.NewLine}"
                    + $"Second path checked: '{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\tcpTrigger.xml'"}",
                    EventLogEntryType.Error,
                    400);
                return false;
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

                currentNode = SettingsNode.enabledComponents_rogueDhcp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsMonitorDhcpEnabled = bool.Parse(xn.InnerText); }

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
                if (IsMonitorDhcpEnabled)
                {
                    // tcpTrigger/dhcpServerIgnoreList
                    currentNode = SettingsNode.dhcpServerIgnoreList_ipAddress;
                    nl = xd.DocumentElement.SelectNodes(currentNode);
                    for (int i = 0; i < nl.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(nl[i].InnerText))
                            IgnoredDhcpServers.Add(IPAddress.Parse(nl[i].InnerText));
                    }
                }

                // tcpTrigger/endpointIgnoreList
                currentNode = SettingsNode.endpointIgnoreList_ipAddress;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        IgnoredEndpoints.Add(IPAddress.Parse(nl[i].InnerText));
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
                else { EmailRateLimitSeconds = 180; }
                if (EmailRateLimitSeconds <= 0) { EmailRateLimitSeconds = 1; }
                currentNode = SettingsNode.email_options_bufferSeconds;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null && !string.IsNullOrEmpty(xn.InnerText)) { EmailBufferSeconds = int.Parse(xn.InnerText); }
                else { EmailBufferSeconds = 15; }
                if (EmailBufferSeconds <= 0) { EmailBufferSeconds = 1; }

                return true;
            }

            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Failed to start the tcpTrigger service. Unable to parse the configuration file '{Path}'."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"Error parsing XML node: '{currentNode}'.{Environment.NewLine}{Environment.NewLine}"
                    + ex.Message,
                    EventLogEntryType.Error,
                    400);
                return false;
            }
        }
    }

    internal class SettingsNode
    {
        // XML node paths for the tcpTrigger configuration file.
        public const string enabledComponents_tcp = "/tcpTrigger/enabledComponents/tcp";
        public const string enabledComponents_udp = "/tcpTrigger/enabledComponents/udp";
        public const string enabledComponents_icmp = "/tcpTrigger/enabledComponents/icmp";
        public const string enabledComponents_rogueDhcp = "/tcpTrigger/enabledComponents/rogueDhcp";
        public const string monitoredPorts_tcp_include = "/tcpTrigger/monitoredPorts/tcp/include";
        public const string monitoredPorts_tcp_exclude = "/tcpTrigger/monitoredPorts/tcp/exclude";
        public const string monitoredPorts_udp_include = "/tcpTrigger/monitoredPorts/udp/include";
        public const string monitoredPorts_udp_exclude = "/tcpTrigger/monitoredPorts/udp/exclude";
        public const string dhcpServerIgnoreList_ipAddress = "/tcpTrigger/dhcpServerIgnoreList/ipAddress";
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
