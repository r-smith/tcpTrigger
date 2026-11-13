using System;
using System.IO;
using System.Xml;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Configuration
{
    // Writes a ServiceConfig to tcpTrigger.xml, always in the current format.
    internal static class ConfigWriter
    {
        // Writes to a temporary file first, then swaps it in for the real file. If anything
        // fails partway through, the existing configuration file is left untouched.
        // Throws UnauthorizedAccessException or IOException if the file can't be written.
        public static void Save(ServiceConfig config, string path)
        {
            string tempPath = path + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true }))
                    {
                        writer.WriteStartDocument();
                        writer.WriteStartElement("tcpTrigger");

                        WriteDetection(writer, config);
                        WriteNotifications(writer, config);
                        WriteIgnoreRules(writer, config);
                        WriteExcludedInterfaces(writer, config);

                        writer.WriteEndElement(); // End: tcpTrigger
                        writer.WriteEndDocument();
                    }

                    // Make sure the new file is fully written to disk before it replaces the old one.
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    // File.Replace keeps the existing file's attributes and permissions.
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch
            {
                DeleteIfPossible(tempPath);
                throw;
            }
        }

        private static void WriteDetection(XmlWriter writer, ServiceConfig config)
        {
            writer.WriteStartElement("detection");

            writer.WriteStartElement("icmp");
            writer.WriteElementString("enabled", XmlConvert.ToString(config.IsDetectIcmpEnabled));
            writer.WriteEndElement(); // End: icmp

            writer.WriteStartElement("tcp");
            writer.WriteElementString("enabled", XmlConvert.ToString(config.IsDetectTcpEnabled));
            writer.WriteEndElement(); // End: tcp

            writer.WriteStartElement("udp");
            writer.WriteElementString("enabled", XmlConvert.ToString(config.IsDetectUdpEnabled));
            writer.WriteEndElement(); // End: udp

            writer.WriteEndElement(); // End: detection
        }

        private static void WriteNotifications(XmlWriter writer, ServiceConfig config)
        {
            writer.WriteStartElement("notifications");
            foreach (Notification notification in config.Notifications)
            {
                switch (notification)
                {
                    case NotificationLogFile log:
                        WriteLogFileNotification(writer, log);
                        break;
                    case NotificationCommand command:
                        WriteCommandNotification(writer, command);
                        break;
                    case NotificationEmail email:
                        WriteEmailNotification(writer, email);
                        break;
                    default:
                        break;
                }
            }
            writer.WriteEndElement(); // End: notifications
        }

        private static void WriteLogFileNotification(XmlWriter writer, NotificationLogFile log)
        {
            writer.WriteStartElement("notification");
            writer.WriteAttributeString("type", ConfigNames.NotificationTypeLogFile);
            writer.WriteElementString("enabled", XmlConvert.ToString(log.IsEnabled));
            writer.WriteElementString("name", log.Name);
            writer.WriteElementString("path", log.Path);
            writer.WriteEndElement(); // End: notification
        }

        private static void WriteCommandNotification(XmlWriter writer, NotificationCommand app)
        {
            writer.WriteStartElement("notification");
            writer.WriteAttributeString("type", ConfigNames.NotificationTypeCommand);
            writer.WriteElementString("enabled", XmlConvert.ToString(app.IsEnabled));
            writer.WriteElementString("name", app.Name);
            writer.WriteElementString("path", app.Path);
            writer.WriteElementString("arguments", app.Arguments);
            writer.WriteEndElement(); // End: notification
        }

        private static void WriteEmailNotification(XmlWriter writer, NotificationEmail email)
        {
            writer.WriteStartElement("notification");
            writer.WriteAttributeString("type", ConfigNames.NotificationTypeEmail);
            writer.WriteElementString("enabled", XmlConvert.ToString(email.IsEnabled));
            writer.WriteElementString("name", email.Name);
            writer.WriteElementString("server", email.SmtpServer);
            writer.WriteElementString("port", email.Port);
            writer.WriteElementString("startTls", XmlConvert.ToString(email.IsStartTlsEnabled));
            WriteSecret(writer, "username", email.Username);
            WriteSecret(writer, "password", email.Password);

            writer.WriteStartElement("recipientList");
            foreach (string recipient in email.Recipients)
            {
                if (!string.IsNullOrWhiteSpace(recipient))
                {
                    writer.WriteElementString("address", recipient.Trim());
                }
            }
            writer.WriteEndElement(); // End: recipientList

            writer.WriteElementString("fromAddress", email.FromAddress);
            writer.WriteElementString("fromDisplayName", email.FromDisplayName);
            writer.WriteElementString("subject", email.Subject);
            writer.WriteElementString("body", email.Body);
            writer.WriteElementString("rateLimitSeconds", XmlConvert.ToString(email.RateLimitSeconds));
            writer.WriteElementString("waitSeconds", XmlConvert.ToString(email.WaitSeconds));
            writer.WriteEndElement(); // End: notification
        }

        private static void WriteIgnoreRules(XmlWriter writer, ServiceConfig config)
        {
            writer.WriteStartElement("ignoreRules");
            foreach (IgnoreRule rule in config.IgnoreRules)
            {
                writer.WriteStartElement("rule");
                writer.WriteElementString("enabled", XmlConvert.ToString(rule.IsEnabled));
                writer.WriteElementString("name", rule.Name);
                writer.WriteElementString("created", ToIso8601(rule.Created));
                writer.WriteElementString("modified", ToIso8601(rule.Modified));

                writer.WriteStartElement("sourceList");
                foreach (string source in rule.Sources)
                {
                    writer.WriteElementString("source", source);
                }
                writer.WriteEndElement(); // End: sourceList

                writer.WriteStartElement("destinationList");
                foreach (string destination in rule.Destinations)
                {
                    writer.WriteElementString("destination", destination);
                }
                writer.WriteEndElement(); // End: destinationList

                writer.WriteStartElement("serviceList");
                foreach (string service in rule.Services)
                {
                    writer.WriteElementString("service", service);
                }
                writer.WriteEndElement(); // End: serviceList

                writer.WriteEndElement(); // End: rule
            }
            writer.WriteEndElement(); // End: ignoreRules
        }

        private static void WriteExcludedInterfaces(XmlWriter writer, ServiceConfig config)
        {
            writer.WriteStartElement("networkInterfaceExcludeList");
            foreach (string deviceGuid in config.ExcludedNetworkInterfaces)
            {
                writer.WriteElementString("deviceGuid", deviceGuid);
            }
            writer.WriteEndElement(); // End: networkInterfaceExcludeList
        }

        // Writes a value encrypted, or an empty element if there is no value.
        private static void WriteSecret(XmlWriter writer, string elementName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                writer.WriteElementString(elementName, null);
                return;
            }

            writer.WriteStartElement(elementName);
            writer.WriteAttributeString("encrypted", "true");
            writer.WriteString(StringCipher.Encrypt(value));
            writer.WriteEndElement();
        }

        // Cleanup after a failed save. A leftover temp file is harmless, so errors are ignored.
        private static void DeleteIfPossible(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Ignore.
            }
        }

        // Unset timestamps (DateTime.MinValue) are written as an empty string.
        private static string ToIso8601(DateTime dateTime)
        {
            return dateTime == DateTime.MinValue
                ? string.Empty
                : dateTime.ToUniversalTime().ToString("u").Replace(" ", "T");
        }
    }
}