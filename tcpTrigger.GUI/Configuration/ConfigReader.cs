using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Configuration
{
    // Reads tcpTrigger.xml into a ServiceConfig.
    // Reads the current format, then the legacy format. The GUI always saves in the
    // current format, so saving once migrates a legacy file.
    internal static class ConfigReader
    {
        // The XML node path currently being read from the configuration file.
        // Used to say which node caused an error when the file can't be parsed.
        private static string s_currentNode;

        // Loads all settings from the configuration file at path. Throws if the file is missing or can't be parsed.
        // A missing or unreadable file throws its own exception (e.g. FileNotFoundException), not FileLoadException.
        public static ServiceConfig Load(string path)
        {
            ServiceConfig config = new ServiceConfig();

            // Opened outside the try block below so file errors aren't reported as parse errors.
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                s_currentNode = string.Empty;
                try
                {
                    XmlDocument xd = new XmlDocument();
                    xd.Load(stream);

                    ReadDetection(xd, config);
                    ReadNotifications(xd, config);
                    ReadIgnoreRules(xd, config);

                    ReadExcludedInterfaces(xd, config);

                    // Values from version 1.3.5 and earlier, if present, override the values above.
                    ReadLegacyEnabledComponents(xd, config);
                    ReadLegacyMonitoredPorts(xd, config);
                    ReadLegacyEndpointIgnoreList(xd, config);
                    ReadLegacyLogAction(xd, config);
                    ReadLegacyExecuteCommandAction(xd, config);
                    ReadLegacyEmailAction(xd, config);
                }
                catch (Exception ex)
                {
                    throw new FileLoadException(
                        $"Unable to parse the configuration file '{path}'."
                        + $"{Environment.NewLine}{Environment.NewLine}"
                        + $"Error parsing XML node: '{s_currentNode}'."
                        + $"{Environment.NewLine}{Environment.NewLine}"
                        + ex.Message,
                        ex);
                }
            }

            return config;
        }

        // ----- Current format -----

        // tcpTrigger/detection
        private static void ReadDetection(XmlDocument xd, ServiceConfig config)
        {
            config.IsDetectIcmpEnabled = ReadBool(xd, ConfigNames.DetectionIcmp + "/enabled", false);
            config.IsDetectTcpEnabled = ReadBool(xd, ConfigNames.DetectionTcp + "/enabled", false);
            config.IsDetectUdpEnabled = ReadBool(xd, ConfigNames.DetectionUdp + "/enabled", false);
        }

        // tcpTrigger/notifications
        private static void ReadNotifications(XmlDocument xd, ServiceConfig config)
        {
            foreach (XmlNode node in SelectNodes(xd, ConfigNames.Notifications))
            {
                // Read the `type` attribute for each <notification> element and parse accordingly.
                switch (node.Attributes["type"]?.Value)
                {
                    case ConfigNames.NotificationTypeLogFile:
                        config.Notifications.Add(new NotificationLogFile
                        {
                            IsEnabled = ReadBool(node, "enabled", false),
                            Name = ReadString(node, "name"),
                            Path = ReadString(node, "path"),
                        });
                        break;
                    case ConfigNames.NotificationTypeCommand:
                        config.Notifications.Add(new NotificationCommand
                        {
                            IsEnabled = ReadBool(node, "enabled", false),
                            Name = ReadString(node, "name"),
                            Path = ReadString(node, "path"),
                            Arguments = ReadString(node, "arguments"),
                        });
                        break;
                    case ConfigNames.NotificationTypeEmail:
                        config.Notifications.Add(new NotificationEmail
                        {
                            IsEnabled = ReadBool(node, "enabled", false),
                            Name = ReadString(node, "name"),
                            SmtpServer = ReadString(node, "server"),
                            Port = ReadString(node, "port"),
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
                    default:
                        break;
                }
            }
        }

        // tcpTrigger/ignoreRules
        // Each rule has a source list (IP addresses), a destination list (IP addresses), and a
        // service list (ports and protocols). Each list can be just "any".
        private static void ReadIgnoreRules(XmlDocument xd, ServiceConfig config)
        {
            foreach (XmlNode node in SelectNodes(xd, ConfigNames.IgnoreRules))
            {
                IgnoreRule rule = new IgnoreRule
                {
                    IsEnabled = ReadBool(node, "enabled", false),
                    Name = ReadString(node, "name"),
                    Sources = ReadList(node, "sourceList/source"),
                    Destinations = ReadList(node, "destinationList/destination"),
                    Services = ReadList(node, "serviceList/service"),
                };
                if (DateTime.TryParse(ReadString(node, "created"), out DateTime created))
                {
                    rule.Created = created;
                }
                if (DateTime.TryParse(ReadString(node, "modified"), out DateTime modified))
                {
                    rule.Modified = modified;
                }

                config.IgnoreRules.Add(rule);
            }
        }

        // tcpTrigger/networkInterfaceExcludeList
        // Same in the current and legacy formats.
        private static void ReadExcludedInterfaces(XmlDocument xd, ServiceConfig config)
        {
            config.ExcludedNetworkInterfaces.UnionWith(ReadList(xd, ConfigNames.NetworkInterfaceExcludeListDeviceGuid));
        }

        // ----- Legacy format: version 1.3.5 and earlier -----
        // Missing values keep what was read from the current format.

        // tcpTrigger/enabledComponents
        private static void ReadLegacyEnabledComponents(XmlDocument xd, ServiceConfig config)
        {
            config.IsDetectIcmpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsIcmp, config.IsDetectIcmpEnabled);
            config.IsDetectTcpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsTcp, config.IsDetectTcpEnabled);
            config.IsDetectUdpEnabled = ReadBool(xd, ConfigNames.EnabledComponentsUdp, config.IsDetectUdpEnabled);
        }

        // tcpTrigger/monitoredPorts
        // Each port list is converted to an ignore rule. Runs after ReadLegacyEnabledComponents.
        private static void ReadLegacyMonitoredPorts(XmlDocument xd, ServiceConfig config)
        {
            if (config.IsDetectTcpEnabled)
            {
                string include = ReadString(xd, ConfigNames.MonitoredPortsTcpInclude);
                string exclude = ReadString(xd, ConfigNames.MonitoredPortsTcpExclude);
                AddConvertedRule(config, PortListConversion.ToIgnoreRule("tcp", include, exclude));
            }
            if (config.IsDetectUdpEnabled)
            {
                string include = ReadString(xd, ConfigNames.MonitoredPortsUdpInclude);
                string exclude = ReadString(xd, ConfigNames.MonitoredPortsUdpExclude);
                AddConvertedRule(config, PortListConversion.ToIgnoreRule("udp", include, exclude));
            }
        }

        // ToIgnoreRule returns null when the lists left out no ports.
        private static void AddConvertedRule(ServiceConfig config, IgnoreRule rule)
        {
            if (rule != null)
            {
                config.IgnoreRules.Add(rule);
                config.HasConvertedPortLists = true;
            }
        }

        // tcpTrigger/endpointIgnoreList
        // Each entry becomes an ignore rule. The `port` attribute should be a single port number,
        // the string "icmp", or missing. A missing `port` attribute means any destination.
        private static void ReadLegacyEndpointIgnoreList(XmlDocument xd, ServiceConfig config)
        {
            foreach (XmlNode node in SelectNodes(xd, ConfigNames.EndpointIgnoreListIpAddress))
            {
                if (!string.IsNullOrEmpty(node.InnerText))
                {
                    string portAttr = node.Attributes["port"]?.Value;

                    config.IgnoreRules.Add(new IgnoreRule
                    {
                        IsEnabled = true,
                        Name = node.Attributes["comment"]?.Value,
                        Sources = new List<string> { node.InnerText },
                        Services = new List<string> { string.IsNullOrEmpty(portAttr) ? "any" : portAttr },
                    });
                }
            }
        }

        // tcpTrigger/enabledActions/log + tcpTrigger/actionSettings
        // Only added if the legacy <log> element exists.
        private static void ReadLegacyLogAction(XmlDocument xd, ServiceConfig config)
        {
            if (ReadString(xd, ConfigNames.EnabledActionsLog) == null)
            {
                return;
            }

            config.Notifications.Add(new NotificationLogFile
            {
                IsEnabled = ReadBool(xd, ConfigNames.EnabledActionsLog, false),
                Path = ReadString(xd, ConfigNames.ActionSettingsLogPath),
            });
        }

        // tcpTrigger/enabledActions/executeCommand + tcpTrigger/actionSettings
        // Only added if the legacy <executeCommand> element exists.
        private static void ReadLegacyExecuteCommandAction(XmlDocument xd, ServiceConfig config)
        {
            if (ReadString(xd, ConfigNames.EnabledActionsExecuteCommand) == null)
            {
                return;
            }

            config.Notifications.Add(new NotificationCommand
            {
                IsEnabled = ReadBool(xd, ConfigNames.EnabledActionsExecuteCommand, false),
                Path = ReadString(xd, ConfigNames.ActionSettingsCommandPath),
                Arguments = ReadString(xd, ConfigNames.ActionSettingsCommandArguments),
            });
        }

        // tcpTrigger/enabledActions/emailNotification + tcpTrigger/email
        // Only added if the legacy <emailNotification> element exists.
        private static void ReadLegacyEmailAction(XmlDocument xd, ServiceConfig config)
        {
            if (ReadString(xd, ConfigNames.EnabledActionsEmailNotification) == null)
            {
                return;
            }

            config.Notifications.Add(new NotificationEmail
            {
                IsEnabled = ReadBool(xd, ConfigNames.EnabledActionsEmailNotification, false),
                SmtpServer = ReadString(xd, ConfigNames.EmailServer),
                Port = ReadString(xd, ConfigNames.EmailPort),
                IsStartTlsEnabled = ReadBool(xd, ConfigNames.EmailIsTlsEnabled, false),
                Username = ReadCredential(xd, ConfigNames.EmailUsername),
                Password = ReadCredential(xd, ConfigNames.EmailPassword),
                Recipients = ReadList(xd, ConfigNames.EmailRecipientListAddress),
                FromAddress = ReadString(xd, ConfigNames.EmailSenderAddress),
                FromDisplayName = ReadString(xd, ConfigNames.EmailSenderDisplayName),
                Subject = RenameLegacyVariables(ReadString(xd, ConfigNames.EmailMessageSubject)),
                Body = RenameLegacyVariables(ReadString(xd, ConfigNames.EmailMessageBody)),
                // A value of 0 or less is treated as 1 second.
                RateLimitSeconds = Math.Max(1, ReadInt(xd, ConfigNames.EmailOptionsRateLimitSeconds, NotificationEmail.DefaultRateLimitSeconds)),
                WaitSeconds = Math.Max(1, ReadInt(xd, ConfigNames.EmailOptionsBufferSeconds, NotificationEmail.DefaultWaitSeconds)),
            });
        }

        // Replaces variable names used by version 1.3.5 and earlier with their current names.
        private static string RenameLegacyVariables(string text)
        {
            return text?
                .Replace("{CONNECTION_LOG}", "{DETECTION_LOG}")
                .Replace("{INTERFACE_IP}", "{DESTINATION_IP}");
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
        // e.g. "/tcpTrigger/notifications/notification/enabled".
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
    }
}