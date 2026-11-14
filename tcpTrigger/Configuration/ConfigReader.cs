using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace tcpTrigger
{
    // Reads tcpTrigger.xml into the Settings class.
    // Reads the current format, then the legacy format.
    internal static class ConfigReader
    {
        private const string AllPorts = "1-65535";

        // The XML node path currently being read from the configuration file.
        // Used to say which node caused an error when the file can't be parsed.
        private static string s_currentNode;

        // Loads all settings from the configuration file. Throws if the file is missing or can't be parsed.
        // A missing or unreadable file throws its own exception (e.g. FileNotFoundException), not FileLoadException.
        public static void Load()
        {
            Settings.Path = ConfigPaths.GetConfigPath();

            if (!File.Exists(Settings.Path))
            {
                throw new FileNotFoundException(
                    "A configuration file for tcpTrigger could not be found. Use the tcpTrigger GUI to generate the file."
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + $"First path checked: '{ConfigPaths.LocalPath}'{Environment.NewLine}"
                    + $"Second path checked: '{ConfigPaths.ProgramDataPath}'");
            }

            // Opened outside the try block below so file errors aren't reported as parse errors.
            using (FileStream stream = new FileStream(Settings.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                s_currentNode = string.Empty;
                try
                {
                    XmlDocument xd = new XmlDocument();
                    xd.Load(stream);

                    ReadDetection(xd);
                    ReadNotifications(xd);
                    ReadIgnoreRules(xd);

                    ReadExcludedInterfaces(xd);

                    // Values from version 1.3.5 and earlier, if present, override the values above.
                    ReadLegacyEnabledComponents(xd);
                    ReadLegacyMonitoredPorts(xd);
                    ReadLegacyEndpointIgnoreList(xd);
                    ReadLegacyLogAction(xd);
                    ReadLegacyExecuteCommandAction(xd);
                    ReadLegacyEmailAction(xd);
                }
                catch (Exception ex)
                {
                    throw new FileLoadException(
                        $"Unable to parse the configuration file '{Settings.Path}'."
                        + $"{Environment.NewLine}{Environment.NewLine}"
                        + $"Error parsing XML node: '{s_currentNode}'."
                        + $"{Environment.NewLine}{Environment.NewLine}"
                        + ex.Message,
                        ex);
                }
            }
        }

        // ----- Current format -----

        // tcpTrigger/detection
        private static void ReadDetection(XmlDocument xd)
        {
            Settings.IsDetectIcmpEnabled = ReadBool(xd, ConfigNames.DetectionIcmp + "/enabled", false);
            Settings.IsDetectTcpEnabled = ReadBool(xd, ConfigNames.DetectionTcp + "/enabled", false);
            Settings.IsDetectUdpEnabled = ReadBool(xd, ConfigNames.DetectionUdp + "/enabled", false);
        }

        // tcpTrigger/notifications
        private static void ReadNotifications(XmlDocument xd)
        {
            Settings.Notifications = new List<Notification>();
            foreach (XmlNode node in SelectNodes(xd, ConfigNames.Notifications))
            {
                // The service only uses enabled notifications.
                if (!ReadBool(node, "enabled", false))
                {
                    continue;
                }

                // Read the `type` attribute for each <notification> element and parse accordingly.
                switch (node.Attributes["type"]?.Value)
                {
                    case ConfigNames.NotificationTypeEmail:
                        Settings.Notifications.Add(new NotificationEmail
                        {
                            Name = ReadString(node, "name"),
                            SmtpServer = ReadString(node, "server"),
                            Port = ReadInt(node, "port", 25),
                            IsStartTlsEnabled = ReadBool(node, "startTls", false),
                            Username = ReadCredential(node, "username"),
                            Password = ReadCredential(node, "password"),
                            Recipients = ReadList(node, "recipientList/address"),
                            FromAddress = ReadString(node, "fromAddress"),
                            FromDisplayName = ReadString(node, "fromDisplayName"),
                            Subject = ReadString(node, "subject"),
                            Body = ReadString(node, "body"),
                            // A value of 0 or less is treated as 1 second.
                            RateLimitSeconds = Math.Max(1, ReadInt(node, "rateLimitSeconds", NotificationEmail.DefaultRateLimitSeconds)),
                            WaitSeconds = Math.Max(1, ReadInt(node, "waitSeconds", NotificationEmail.DefaultWaitSeconds)),
                        });
                        break;
                    case ConfigNames.NotificationTypeLogFile:
                        Settings.Notifications.Add(new NotificationLogFile
                        {
                            Name = ReadString(node, "name"),
                            Path = ReadString(node, "path"),
                        });
                        break;
                    case ConfigNames.NotificationTypeCommand:
                        Settings.Notifications.Add(new NotificationCommand
                        {
                            Name = ReadString(node, "name"),
                            Path = ReadString(node, "path"),
                            Arguments = ReadString(node, "arguments"),
                        });
                        break;
                    default:
                        break;
                }
            }
        }

        // tcpTrigger/ignoreRules
        // Each rule has a source list (IP addresses), a destination list (IP addresses), and a
        // service list (ports and protocols). Each list can be just "any".
        private static void ReadIgnoreRules(XmlDocument xd)
        {
            Settings.IgnoreRules = new List<IgnoreRule>();
            foreach (XmlNode node in SelectNodes(xd, ConfigNames.IgnoreRules))
            {
                // The service only uses enabled rules.
                if (!ReadBool(node, "enabled", false))
                {
                    continue;
                }

                Settings.IgnoreRules.Add(new IgnoreRule(
                    name: ReadString(node, "name"),
                    sources: ReadList(node, "sourceList/source"),
                    destinations: ReadList(node, "destinationList/destination"),
                    services: ReadList(node, "serviceList/service")));
            }
        }

        // tcpTrigger/networkInterfaceExcludeList
        // Same in the current and legacy formats.
        private static void ReadExcludedInterfaces(XmlDocument xd)
        {
            Settings.ExcludedNetworkInterfaces = new HashSet<string>(ReadList(xd, ConfigNames.NetworkInterfaceExcludeListDeviceGuid));
        }

        // ----- Legacy format: version 1.3.5 and earlier -----
        // Missing values keep what was read from the current format.

        // tcpTrigger/enabledComponents
        private static void ReadLegacyEnabledComponents(XmlDocument xd)
        {
            Settings.IsDetectIcmpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsIcmp, Settings.IsDetectIcmpEnabled);
            Settings.IsDetectTcpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsTcp, Settings.IsDetectTcpEnabled);
            Settings.IsDetectUdpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsUdp, Settings.IsDetectUdpEnabled);
        }

        // tcpTrigger/monitoredPorts
        // Ports are only read for protocols that are enabled. Runs after ReadLegacyEnabledComponents.
        // An empty or missing include list means all ports.
        private static void ReadLegacyMonitoredPorts(XmlDocument xd)
        {
            if (Settings.IsDetectTcpEnabled)
            {
                Settings.TcpPortsToInclude = ReadString(xd, ConfigNames.MonitoredPortsTcpInclude);
                if (string.IsNullOrWhiteSpace(Settings.TcpPortsToInclude))
                {
                    Settings.TcpPortsToInclude = AllPorts;
                }
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
                if (string.IsNullOrWhiteSpace(Settings.UdpPortsToInclude))
                {
                    Settings.UdpPortsToInclude = AllPorts;
                }
                HashSet<ushort> includePorts = ParsePorts(Settings.UdpPortsToInclude);

                Settings.UdpPortsToExclude = ReadString(xd, ConfigNames.MonitoredPortsUdpExclude);
                HashSet<ushort> excludePorts = ParsePorts(Settings.UdpPortsToExclude);

                // Final set contains all included ports except those excluded.
                includePorts.ExceptWith(excludePorts);
                Settings.UdpPortsToMonitor = includePorts;
            }
        }

        // tcpTrigger/endpointIgnoreList
        // Each entry becomes an ignore rule. The `port` attribute should be a single port number,
        // the string "icmp", or missing. A missing `port` attribute means all traffic from that address.
        private static void ReadLegacyEndpointIgnoreList(XmlDocument xd)
        {
            foreach (XmlNode node in SelectNodes(xd, ConfigNames.EndpointIgnoreListIpAddress))
            {
                if (!string.IsNullOrEmpty(node.InnerText))
                {
                    string portAttr = node.Attributes["port"]?.Value;

                    Settings.IgnoreRules.Add(new IgnoreRule(
                        name: node.Attributes["comment"]?.Value,
                        sources: new List<string> { node.InnerText },
                        destinations: new List<string> { "any" },
                        services: GetLegacyServices(portAttr)));
                }
            }
        }

        // Version 1.3.5 and earlier applied a port to both TCP and UDP.
        private static List<string> GetLegacyServices(string port)
        {
            if (string.IsNullOrEmpty(port))
            {
                return new List<string> { "any" };
            }
            if (port.Equals("icmp", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string> { "icmp" };
            }
            return new List<string> { "tcp:" + port, "udp:" + port };
        }

        // tcpTrigger/enabledActions/log + tcpTrigger/actionSettings
        // Becomes a log file notification if the legacy <log> element is true.
        private static void ReadLegacyLogAction(XmlDocument xd)
        {
            if (ReadBool(xd, ConfigNames.EnabledActionsLog, false))
            {
                Settings.Notifications.Add(new NotificationLogFile
                {
                    Path = ReadString(xd, ConfigNames.ActionSettingsLogPath),
                });
            }
        }

        // tcpTrigger/enabledActions/executeCommand + tcpTrigger/actionSettings
        // Becomes a command notification if the legacy <executeCommand> element is true.
        private static void ReadLegacyExecuteCommandAction(XmlDocument xd)
        {
            if (ReadBool(xd, ConfigNames.EnabledActionsExecuteCommand, false))
            {
                Settings.Notifications.Add(new NotificationCommand
                {
                    Path = ReadString(xd, ConfigNames.ActionSettingsCommandPath),
                    Arguments = ReadString(xd, ConfigNames.ActionSettingsCommandArguments),
                });
            }
        }

        // tcpTrigger/enabledActions/emailNotification + tcpTrigger/email
        // Becomes an email notification if the legacy <emailNotification> element is true.
        private static void ReadLegacyEmailAction(XmlDocument xd)
        {
            if (ReadBool(xd, ConfigNames.EnabledActionsEmailNotification, false))
            {
                Settings.Notifications.Add(new NotificationEmail
                {
                    SmtpServer = ReadString(xd, ConfigNames.EmailServer),
                    Port = ReadInt(xd, ConfigNames.EmailPort, 25),
                    IsStartTlsEnabled = ReadBool(xd, ConfigNames.EmailIsTlsEnabled, false),
                    Username = ReadCredential(xd, ConfigNames.EmailUsername),
                    Password = ReadCredential(xd, ConfigNames.EmailPassword),
                    Recipients = ReadList(xd, ConfigNames.EmailRecipientListAddress),
                    FromAddress = ReadString(xd, ConfigNames.EmailSenderAddress),
                    FromDisplayName = ReadString(xd, ConfigNames.EmailSenderDisplayName),
                    Subject = ReadString(xd, ConfigNames.EmailMessageSubject),
                    Body = ReadString(xd, ConfigNames.EmailMessageBody),
                    // A value of 0 or less is treated as 1 second.
                    RateLimitSeconds = Math.Max(1, ReadInt(xd, ConfigNames.EmailOptionsRateLimitSeconds, NotificationEmail.DefaultRateLimitSeconds)),
                    WaitSeconds = Math.Max(1, ReadInt(xd, ConfigNames.EmailOptionsBufferSeconds, NotificationEmail.DefaultWaitSeconds)),
                });
            }
        }

        // ----- Read helpers -----

        // The read helpers below take the node to read from: the XmlDocument for an
        // absolute path ("/tcpTrigger/..."), or an element for a path relative to it ("enabled").

        // Returns the text of the XML node at path, or null if the node doesn't exist.
        private static string ReadString(XmlNode node, string path)
        {
            s_currentNode = GetNodePath(node, path);
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
            s_currentNode = GetNodePath(node, path);
            XmlNode credentialNode = node.SelectSingleNode(path);
            if (credentialNode == null || string.IsNullOrEmpty(credentialNode.InnerText))
            {
                return null;
            }

            return credentialNode.Attributes["encrypted"]?.InnerText == "true"
                ? StringCipher.Decrypt(credentialNode.InnerText)
                : credentialNode.InnerText;
        }

        // Returns the text of every XML node at path, skipping empty ones.
        private static List<string> ReadList(XmlNode node, string path)
        {
            List<string> values = new List<string>();
            foreach (XmlNode item in SelectNodes(node, path))
            {
                if (!string.IsNullOrEmpty(item.InnerText))
                {
                    values.Add(item.InnerText);
                }
            }
            return values;
        }

        // Returns every XML node at path. The list is empty if there are none.
        private static XmlNodeList SelectNodes(XmlNode node, string path)
        {
            s_currentNode = GetNodePath(node, path);
            return node.SelectNodes(path);
        }

        // Returns the full path of the node being read, for error messages.
        // For a relative path, adds the element's own path in front,
        // e.g. "/tcpTrigger/notificationRules/notification/enabled".
        private static string GetNodePath(XmlNode node, string path)
        {
            if (!(node is XmlElement))
            {
                return path;
            }

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
            HashSet<ushort> ports = new HashSet<ushort>();
            if (string.IsNullOrWhiteSpace(portList))
            {
                return ports;
            }

            foreach (string part in portList.Split(','))
            {
                string[] range = part.Split('-');
                ushort start = ushort.Parse(range[0]);
                ushort end = ushort.Parse(range[range.Length - 1]);
                if (start > end)
                {
                    throw new FormatException($"Port range '{part.Trim()}' has its start after its end.");
                }

                // Use an int counter (a ushort would wrap from 65535 back to 0).
                for (int port = start; port <= end; port++)
                {
                    ports.Add((ushort)port);
                }
            }
            return ports;
        }
    }
}