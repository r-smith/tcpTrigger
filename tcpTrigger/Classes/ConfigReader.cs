using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Xml;

namespace tcpTrigger
{
    // Reads tcpTrigger.xml into the Settings class.
    internal static class ConfigReader
    {
        // The XML node path currently being read from the configuration file.
        // Used to say which node caused an error when the file can't be parsed.
        private static string _currentNode;

        // Loads all settings from the configuration file. Throws if the file is missing or can't be parsed.
        public static void Load()
        {
            Settings.Path = ConfigPaths.GetConfigPath();

            if (!File.Exists(Settings.Path))
            {
                // Error: A configuration file could not be found.
                throw new FileNotFoundException(
                    "A configuration file for tcpTrigger could not be found. Use 'tcpTrigger Manager.exe' to generate the file."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"First path checked: '{ConfigPaths.LocalPath}'{Environment.NewLine}"
                    + $"Second path checked: '{ConfigPaths.ProgramDataPath}'"
                    );
            }

            _currentNode = string.Empty;
            try
            {
                XmlDocument xd = new XmlDocument();
                xd.Load(Settings.Path);

                // tcpTrigger/enabledComponents
                Settings.IsMonitorTcpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsTcp, false);
                Settings.IsMonitorUdpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsUdp, false);
                Settings.IsMonitorIcmpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsIcmp, false);

                if (Settings.IsMonitorTcpEnabled)
                {
                    // tcpTrigger/monitoredPorts
                    Settings.TcpPortsToIncludeAsString = ReadString(xd, ConfigNames.MonitoredPortsTcpInclude);
                    HashSet<ushort> includePorts = ParsePorts(Settings.TcpPortsToIncludeAsString);

                    Settings.TcpPortsToExcludeAsString = ReadString(xd, ConfigNames.MonitoredPortsTcpExclude);
                    HashSet<ushort> excludePorts = ParsePorts(Settings.TcpPortsToExcludeAsString);

                    // Final set contains all included ports except those excluded.
                    includePorts.ExceptWith(excludePorts);
                    Settings.TcpPortsToMonitor = includePorts;
                }
                if (Settings.IsMonitorUdpEnabled)
                {
                    // tcpTrigger/monitoredPorts
                    Settings.UdpPortsToIncludeAsString = ReadString(xd, ConfigNames.MonitoredPortsUdpInclude);
                    HashSet<ushort> includePorts = ParsePorts(Settings.UdpPortsToIncludeAsString);

                    Settings.UdpPortsToExcludeAsString = ReadString(xd, ConfigNames.MonitoredPortsUdpExclude);
                    HashSet<ushort> excludePorts = ParsePorts(Settings.UdpPortsToExcludeAsString);

                    // Final set contains all included ports except those excluded.
                    includePorts.ExceptWith(excludePorts);
                    Settings.UdpPortsToMonitor = includePorts;
                }

                // tcpTrigger/endpointIgnoreList
                var ignoredEndpoints = new Dictionary<IPAddress, HashSet<int>>();
                XmlNodeList nl = SelectNodes(xd, ConfigNames.EndpointIgnoreListIpAddress);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                    {
                        string portAttr = nl[i].Attributes["port"]?.Value;
                        int port = Settings.IgnoreAll;

                        if (!string.IsNullOrEmpty(portAttr))
                        {
                            if (string.Equals(portAttr, "icmp", StringComparison.OrdinalIgnoreCase))
                            {
                                port = Settings.IgnoreIcmp;
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
                Settings.IgnoredEndpoints = ignoredEndpoints;

                // tcpTrigger/networkInterfaceExcludeList
                Settings.ExcludedNetworkInterfaces = new HashSet<string>(ReadList(xd, ConfigNames.NetworkInterfaceExcludeListDeviceGuid));

                // tcpTrigger/enabledActions
                Settings.IsLogEnabled = ReadBool(xd, ConfigNames.EnabledActionsLog, false);
                Settings.IsEventLogEnabled = ReadBool(xd, ConfigNames.EnabledActionsWindowsEventLog, false);
                Settings.IsEmailNotificationEnabled = ReadBool(xd, ConfigNames.EnabledActionsEmailNotification, false);
                Settings.IsExternalAppEnabled = ReadBool(xd, ConfigNames.EnabledActionsExecuteCommand, false);

                // tcpTrigger/actionSettings
                Settings.LogPath = ReadString(xd, ConfigNames.ActionSettingsLogPath);
                Settings.ExternalAppPath = ReadString(xd, ConfigNames.ActionSettingsCommandPath);
                Settings.ExternalAppArguments = ReadString(xd, ConfigNames.ActionSettingsCommandArguments);

                // tcpTrigger/email
                Settings.EmailServer = ReadString(xd, ConfigNames.EmailServer);
                Settings.EmailServerPort = ReadInt(xd, ConfigNames.EmailPort, 25);
                Settings.IsEmailTlsEnabled = ReadBool(xd, ConfigNames.EmailIsTlsEnabled, false);
                Settings.IsEmailAuthRequired = ReadBool(xd, ConfigNames.EmailIsAuthRequired, false);
                Settings.EmailUsername = ReadCredential(xd, ConfigNames.EmailUsername);
                Settings.EmailPassword = ReadCredential(xd, ConfigNames.EmailPassword);
                Settings.EmailRecipients = ReadList(xd, ConfigNames.EmailRecipientListAddress);
                Settings.EmailSender = ReadString(xd, ConfigNames.EmailSenderAddress);
                Settings.EmailSenderDisplayName = ReadString(xd, ConfigNames.EmailSenderDisplayName);

                // tcpTrigger/email/message
                Settings.EmailSubject = ReadString(xd, ConfigNames.EmailMessageSubject);
                Settings.EmailBody = ReadString(xd, ConfigNames.EmailMessageBody);

                // tcpTrigger/email/options
                Settings.EmailRateLimitSeconds = Math.Max(1, ReadInt(xd, ConfigNames.EmailOptionsRateLimitSeconds, 300));
                Settings.EmailBufferSeconds = Math.Max(1, ReadInt(xd, ConfigNames.EmailOptionsBufferSeconds, 30));
            }

            catch (Exception ex)
            {
                // Error: Failed to parse XML configuration.
                throw new FileLoadException(
                    $"Unable to parse the configuration file '{Settings.Path}'."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"Error parsing XML node: '{_currentNode}'."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + ex.Message
                    );
            }
        }

        // The read helpers below take the node to read from: the XmlDocument for an
        // absolute path ("/tcpTrigger/..."), or an element for a path relative to it ("enabled").

        // Returns the text of the XML node at path, or null if the node doesn't exist.
        private static string ReadString(XmlNode node, string path)
        {
            _currentNode = GetNodePath(node, path);
            return node.SelectSingleNode(path)?.InnerText;
        }

        // Returns the node at path as a bool, or defaultValue if the node is missing or empty.
        private static bool ReadBool(XmlNode node, string path, bool defaultValue)
        {
            string text = ReadString(node, path);
            return string.IsNullOrWhiteSpace(text) ? defaultValue : bool.Parse(text);
        }

        // Returns the node at path as an int, or defaultValue if the node is missing or empty.
        private static int ReadInt(XmlNode node, string path, int defaultValue)
        {
            string text = ReadString(node, path);
            return string.IsNullOrWhiteSpace(text) ? defaultValue : int.Parse(text);
        }

        // Returns the text of the node at path, decrypting it if the node has encrypted="true".
        // Returns null if the node is missing or empty.
        private static string ReadCredential(XmlNode node, string path)
        {
            _currentNode = GetNodePath(node, path);
            XmlNode credentialNode = node.SelectSingleNode(path);
            if (credentialNode == null || string.IsNullOrEmpty(credentialNode.InnerText))
                return null;

            return credentialNode.Attributes["encrypted"]?.InnerText == "true"
                ? StringCipher.Decrypt(credentialNode.InnerText)
                : credentialNode.InnerText;
        }

        // Returns the text of every XML node at path, skipping empty ones.
        private static List<string> ReadList(XmlNode node, string path)
        {
            var values = new List<string>();
            foreach (XmlNode item in SelectNodes(node, path))
            {
                if (!string.IsNullOrEmpty(item.InnerText))
                    values.Add(item.InnerText);
            }
            return values;
        }

        // Returns every XML node at path. The list is empty if there are none.
        private static XmlNodeList SelectNodes(XmlNode node, string path)
        {
            _currentNode = GetNodePath(node, path);
            return node.SelectNodes(path);
        }

        // Returns the full path of the node being read, for error messages.
        // For a relative path, adds the element's own path in front,
        // e.g. "/tcpTrigger/notificationRules/notification/enabled".
        private static string GetNodePath(XmlNode node, string path)
        {
            if (!(node is XmlElement))
                return path;

            string elementPath = string.Empty;
            for (XmlNode n = node; n is XmlElement; n = n.ParentNode)
            {
                elementPath = "/" + n.Name + elementPath;
            }
            return elementPath + "/" + path;
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
    }
}