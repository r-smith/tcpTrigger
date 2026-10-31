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

                ReadExcludedInterfaces(xd);

                ReadLegacyEnabledComponents(xd);
                ReadLegacyMonitoredPorts(xd);
                ReadLegacyEndpointIgnoreList(xd);
                ReadLegacyLogAction(xd);
                ReadLegacyExecuteCommandAction(xd);
                ReadLegacyEmailAction(xd);
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

        // tcpTrigger/networkInterfaceExcludeList
        // Same in the current and legacy formats.
        private static void ReadExcludedInterfaces(XmlDocument xd)
        {
            Settings.ExcludedNetworkInterfaces = new HashSet<string>(ReadList(xd, ConfigNames.NetworkInterfaceExcludeListDeviceGuid));
        }

        // ----- Legacy format -----

        // tcpTrigger/enabledComponents
        private static void ReadLegacyEnabledComponents(XmlDocument xd)
        {
            Settings.IsDetectIcmpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsIcmp, false);
            Settings.IsDetectTcpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsTcp, false);
            Settings.IsDetectUdpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsUdp, false);
        }

        // tcpTrigger/monitoredPorts
        // Ports are only read for protocols that are enabled.
        private static void ReadLegacyMonitoredPorts(XmlDocument xd)
        {
            if (Settings.IsDetectTcpEnabled)
            {
                Settings.TcpPortsToInclude = ReadString(xd, ConfigNames.MonitoredPortsTcpInclude);
                HashSet<ushort> includePorts = ParsePorts(Settings.TcpPortsToInclude);

                Settings.TcpPortsToExclude = ReadString(xd, ConfigNames.MonitoredPortsTcpExclude);
                HashSet<ushort> excludePorts = ParsePorts(Settings.TcpPortsToExclude);

                // Final set contains all included ports except those excluded.
                includePorts.ExceptWith(excludePorts);
                Settings.TcpPortsToMonitor = includePorts;
            }
            if (Settings.IsDetectUdpEnabled)
            {
                Settings.UdpPortsToInclude = ReadString(xd, ConfigNames.MonitoredPortsUdpInclude);
                HashSet<ushort> includePorts = ParsePorts(Settings.UdpPortsToInclude);

                Settings.UdpPortsToExclude = ReadString(xd, ConfigNames.MonitoredPortsUdpExclude);
                HashSet<ushort> excludePorts = ParsePorts(Settings.UdpPortsToExclude);

                // Final set contains all included ports except those excluded.
                includePorts.ExceptWith(excludePorts);
                Settings.UdpPortsToMonitor = includePorts;
            }
        }

        // tcpTrigger/endpointIgnoreList
        private static void ReadLegacyEndpointIgnoreList(XmlDocument xd)
        {
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
        }

        // tcpTrigger/enabledActions/log + tcpTrigger/actionSettings
        private static void ReadLegacyLogAction(XmlDocument xd)
        {
            Settings.IsLogEnabled = ReadBool(xd, ConfigNames.EnabledActionsLog, false);
            Settings.LogPath = ReadString(xd, ConfigNames.ActionSettingsLogPath);
        }

        // tcpTrigger/enabledActions/executeCommand + tcpTrigger/actionSettings
        private static void ReadLegacyExecuteCommandAction(XmlDocument xd)
        {
            Settings.IsExternalAppEnabled = ReadBool(xd, ConfigNames.EnabledActionsExecuteCommand, false);
            Settings.ExternalAppPath = ReadString(xd, ConfigNames.ActionSettingsCommandPath);
            Settings.ExternalAppArguments = ReadString(xd, ConfigNames.ActionSettingsCommandArguments);
        }

        // tcpTrigger/enabledActions/emailNotification + tcpTrigger/email
        private static void ReadLegacyEmailAction(XmlDocument xd)
        {
            Settings.IsEmailNotificationEnabled = ReadBool(xd, ConfigNames.EnabledActionsEmailNotification, false);
            Settings.EmailSmtpServer = ReadString(xd, ConfigNames.EmailServer);
            Settings.EmailPort = ReadInt(xd, ConfigNames.EmailPort, 25);
            Settings.IsEmailStartTlsEnabled = ReadBool(xd, ConfigNames.EmailIsTlsEnabled, false);
            Settings.EmailUsername = ReadCredential(xd, ConfigNames.EmailUsername);
            Settings.EmailPassword = ReadCredential(xd, ConfigNames.EmailPassword);
            Settings.EmailRecipients = ReadList(xd, ConfigNames.EmailRecipientListAddress);
            Settings.EmailFromAddress = ReadString(xd, ConfigNames.EmailSenderAddress);
            Settings.EmailFromDisplayName = ReadString(xd, ConfigNames.EmailSenderDisplayName);
            Settings.EmailSubject = ReadString(xd, ConfigNames.EmailMessageSubject);
            Settings.EmailBody = ReadString(xd, ConfigNames.EmailMessageBody);

            // A value of 0 or less is treated as 1 second.
            Settings.EmailRateLimitSeconds = Math.Max(1, ReadInt(xd, ConfigNames.EmailOptionsRateLimitSeconds, 300));
            Settings.EmailBufferSeconds = Math.Max(1, ReadInt(xd, ConfigNames.EmailOptionsBufferSeconds, 30));
        }

        // ----- Read helpers -----

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