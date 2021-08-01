using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml;

namespace tcpTrigger
{
    internal class Configuration
    {
        public bool IsMonitorTcpEnabled { get; private set; }
        public bool IsMonitorIcmpEnabled { get; private set; }
        public bool IsMonitorPoisonEnabled { get; private set; }
        public bool IsMonitorDhcpEnabled { get; private set; }
        public int[] TcpPortsToMonitor { get; private set; }
        public string TcpPortsToMonitorAsString { get; private set; }
        public bool IsEventLogEnabled { get; private set; }
        public bool IsEmailNotificationEnabled { get; private set; }
        public bool IsExternalAppEnabled { get; private set; }
        public int ActionRateLimitMinutes { get; private set; }
        public string TriggeredApplicationPath { get; private set; }
        public string TriggeredApplicationArguments { get; private set; }
        public HashSet<string> ExcludedNetworkInterfaces { get; private set; } = new HashSet<string>();
        public HashSet<IPAddress> IgnoredDhcpServers { get; private set; } = new HashSet<IPAddress>();
        public HashSet<IPAddress> IgnoredEndpoints { get; private set; } = new HashSet<IPAddress>();
        public string EmailServer { get; private set; }
        public int EmailServerPort { get; private set; }
        public bool IsEmailAuthRequired { get; private set; }
        public string EmailUsername { get; private set; }
        public string EmailPassword { get; private set; }
        public List<string> EmailRecipients { get; private set; } = new List<string>();
        public string EmailSender { get; private set; }
        public string EmailSenderDisplayName { get; private set; }
        public string EmailSubject { get; private set; }
        public string MessageBodyPing { get; private set; }
        public string MessageBodyTcpConnect { get; private set; }
        public string MessageBodyNamePoison { get; private set; }
        public string MessageBodyRogueDhcp { get; private set; }

        private string GetConfigurationPath()
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

        public bool Load()
        {
            string configuratonPath = GetConfigurationPath();
            
            if (string.IsNullOrEmpty(configuratonPath))
            {
                // No configuration was found. Log a warning in the Windows event log and
                // return true (successful load) to allow the tcpTrigger service to start.
                // In this state, the tcpTrigger service will start and run with everything disabled.
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Could not locate a configuration file for the tcpTrigger service."
                    + $" The service will start and run with everything disabled.{Environment.NewLine}{Environment.NewLine}"
                    + $" First path checked: {AppDomain.CurrentDomain.BaseDirectory + "tcpTrigger.xml"}{Environment.NewLine}"
                    + $" Second path checked: {Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\tcpTrigger.xml"}",
                    EventLogEntryType.Warning,
                    400);
                return true;
            }

            // currentNode is updated with the XML node path for every element that is read
            // from the configuration file. This aids in debugging and provides more useful
            // error messages when something goes wrong.
            string currentNode = string.Empty;
            try
            {
                var xd = new XmlDocument();
                xd.Load(configuratonPath);

                XmlNode xn;
                XmlNodeList nl;
                string encryptedValue;
                // tcpTrigger/enabledComponents
                currentNode = ConfigurationNode.enabledComponents_tcp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsMonitorTcpEnabled = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledComponents_icmp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsMonitorIcmpEnabled = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledComponents_namePoison;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsMonitorPoisonEnabled = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledComponents_rogueDhcp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsMonitorDhcpEnabled = bool.Parse(xn.InnerText); }

                if (IsMonitorTcpEnabled)
                {
                    // tcpTrigger/monitoredPorts
                    currentNode = ConfigurationNode.monitoredPorts_tcp_include;
                    TcpPortsToMonitorAsString = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                    TcpPortsToMonitor = (from part in TcpPortsToMonitorAsString.Split(',')
                                         let range = part.Split('-')
                                         let start = int.Parse(range[0])
                                         let end = int.Parse(range[range.Length - 1])
                                         from i in Enumerable.Range(start, end - start + 1)
                                         orderby i
                                         select i).Distinct().ToArray();
                }
                if (IsMonitorDhcpEnabled)
                {
                    // tcpTrigger/dhcpServerIgnoreList
                    currentNode = ConfigurationNode.dhcpServerIgnoreList_ipAddress;
                    nl = xd.DocumentElement.SelectNodes(currentNode);
                    for (int i = 0; i < nl.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(nl[i].InnerText))
                            IgnoredDhcpServers.Add(IPAddress.Parse(nl[i].InnerText));
                    }
                }

                // tcpTrigger/endpointIgnoreList
                currentNode = ConfigurationNode.endpointIgnoreList_ipAddress;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        IgnoredEndpoints.Add(IPAddress.Parse(nl[i].InnerText));
                }

                // tcpTrigger/networkInterfaceExcludeList
                currentNode = ConfigurationNode.networkInterfaceExcludeList_deviceGuid;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        ExcludedNetworkInterfaces.Add(nl[i].InnerText);
                }

                // tcpTrigger/enabledActions
                currentNode = ConfigurationNode.enabledActions_windowsEventLog;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsEventLogEnabled = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledActions_emailNotification;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsEmailNotificationEnabled = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledActions_executeCommand;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsExternalAppEnabled = bool.Parse(xn.InnerText); }

                // tcpTrigger/actionSettings
                currentNode = ConfigurationNode.actionsSettings_rateLimitMinutes;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null && !string.IsNullOrEmpty(xn.InnerText)) { ActionRateLimitMinutes = int.Parse(xn.InnerText); }
                else { ActionRateLimitMinutes = 0; }

                currentNode = ConfigurationNode.actionsSettings_command_path;
                TriggeredApplicationPath = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = ConfigurationNode.actionsSettings_command_arguments;
                TriggeredApplicationArguments = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/emailConfiguration
                currentNode = ConfigurationNode.emailConfiguration_server;
                EmailServer = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                currentNode = ConfigurationNode.emailConfiguration_port;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null && !string.IsNullOrEmpty(xn.InnerText)) { EmailServerPort = int.Parse(xn.InnerText); }
                else { EmailServerPort = 25; }

                currentNode = ConfigurationNode.emailConfiguration_isAuthRequired;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsEmailAuthRequired = bool.Parse(xn.InnerText); }

                // Decrypt username.
                currentNode = ConfigurationNode.emailConfiguration_username;
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
                currentNode = ConfigurationNode.emailConfiguration_password;
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

                currentNode = ConfigurationNode.emailConfiguration_recipientList_address;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        EmailRecipients.Add(nl[i].InnerText);
                }

                currentNode = ConfigurationNode.emailConfiguration_sender_address;
                EmailSender = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = ConfigurationNode.emailConfiguration_sender_displayName;
                EmailSenderDisplayName = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/customMessage
                currentNode = ConfigurationNode.customMessage;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (nl[i].Attributes["type"]?.InnerText == "tcp")
                    {
                        MessageBodyTcpConnect = nl[i].SelectSingleNode("body")?.InnerText;
                    }
                    if (nl[i].Attributes["type"]?.InnerText == "icmp")
                    {
                        MessageBodyPing = nl[i].SelectSingleNode("body")?.InnerText;
                    }
                    if (nl[i].Attributes["type"]?.InnerText == "namePoison")
                    {
                        MessageBodyNamePoison = nl[i].SelectSingleNode("body")?.InnerText;
                    }
                    if (nl[i].Attributes["type"]?.InnerText == "rogueDhcp")
                    {
                        MessageBodyRogueDhcp = nl[i].SelectSingleNode("body")?.InnerText;
                    }
                }

                //bool.TryParse(ConfigurationManager.AppSettings["DoNotMonitorVMwareVirtualHostAdapters"], out DoNotMonitorVMwareVirtualHostAdapters);
                // Get subject!

                return true;
            }

            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Failed to start the tcpTrigger service.{Environment.NewLine}{Environment.NewLine}"
                    + $"Unable to parse the configuration file '{configuratonPath}'.{Environment.NewLine}"
                    + $"Error parsing XML node: {currentNode}{Environment.NewLine}{Environment.NewLine}"
                    + ex.Message,
                    EventLogEntryType.Error,
                    400);
                return false;
            }
        }
    }

    internal class ConfigurationNode
    {
        // XML node paths for the tcpTrigger configuration file.
        public const string enabledComponents_tcp = "/tcpTrigger/enabledComponents/tcp";
        public const string enabledComponents_icmp = "/tcpTrigger/enabledComponents/icmp";
        public const string enabledComponents_namePoison = "/tcpTrigger/enabledComponents/namePoison";
        public const string enabledComponents_rogueDhcp = "/tcpTrigger/enabledComponents/rogueDhcp";
        public const string monitoredPorts_tcp_include = "/tcpTrigger/monitoredPorts/tcp/include";
        public const string dhcpServerIgnoreList_ipAddress = "/tcpTrigger/dhcpServerIgnoreList/ipAddress";
        public const string endpointIgnoreList_ipAddress = "/tcpTrigger/endpointIgnoreList/ipAddress";
        public const string networkInterfaceExcludeList_deviceGuid = "/tcpTrigger/networkInterfaceExcludeList/deviceGuid";
        public const string enabledActions_windowsEventLog = "/tcpTrigger/enabledActions/windowsEventLog";
        public const string enabledActions_emailNotification = "/tcpTrigger/enabledActions/emailNotification";
        public const string enabledActions_popupNotification = "/tcpTrigger/enabledActions/popupNotification";
        public const string enabledActions_executeCommand = "/tcpTrigger/enabledActions/executeCommand";
        public const string actionsSettings_rateLimitMinutes = "/tcpTrigger/actionSettings/rateLimitMinutes";
        public const string actionsSettings_command_path = "/tcpTrigger/actionSettings/command/path";
        public const string actionsSettings_command_arguments = "/tcpTrigger/actionSettings/command/arguments";
        public const string emailConfiguration_server = "/tcpTrigger/emailConfiguration/server";
        public const string emailConfiguration_port = "/tcpTrigger/emailConfiguration/port";
        public const string emailConfiguration_isAuthRequired = "/tcpTrigger/emailConfiguration/isAuthRequired";
        public const string emailConfiguration_username = "/tcpTrigger/emailConfiguration/username";
        public const string emailConfiguration_password = "/tcpTrigger/emailConfiguration/password";
        public const string emailConfiguration_recipientList_address = "/tcpTrigger/emailConfiguration/recipientList/address";
        public const string emailConfiguration_sender_address = "/tcpTrigger/emailConfiguration/sender/address";
        public const string emailConfiguration_sender_displayName = "/tcpTrigger/emailConfiguration/sender/displayName";
        public const string customMessage = "/tcpTrigger/customMessage";
    }
}
