using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Windows;
using System.Xml;

namespace tcpTrigger.Editor
{
    public partial class MainWindow : Window
    {
        private void ReadSettings()
        {
            string settingsPath = GetSettingsPath();

            if (!File.Exists(settingsPath))
            {
                // No settings file found. This is expected on new installations.
                // Load defaults and abort reading settings.
                LoadDefaults();
                return;
            }

            // currentNode is updated with the XML node path for every element that is read
            // from the settings file. This aids in debugging and provides more useful
            // error messages when something goes wrong.
            string currentNode = string.Empty;
            try
            {
                var xd = new XmlDocument();
                xd.Load(settingsPath);

                XmlNode xn;
                XmlNodeList nl;
                string encryptedValue;
                // tcpTrigger/enabledComponents
                currentNode = SettingsNode.enabledComponents_tcp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { MonitorTcpOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledComponents_udp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { MonitorUdpOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledComponents_icmp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { MonitorIcmpOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledComponents_rogueDhcp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { MonitorDhcpOption.IsChecked = bool.Parse(xn.InnerText); }

                // tcpTrigger/monitoredPorts
                currentNode = SettingsNode.monitoredPorts_tcp_include;
                TcpIncludePorts.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                _tcpInclude = TcpIncludePorts.Text;
                if (_tcpInclude.Equals("1-65535")) { TcpAllPortsOption.IsChecked = true; }
                currentNode = SettingsNode.monitoredPorts_tcp_exclude;
                TcpExcludePorts.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = SettingsNode.monitoredPorts_udp_include;
                UdpIncludePorts.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                _udpInclude = UdpIncludePorts.Text;
                if (_udpInclude.Equals("1-65535")) { UdpAllPortsOption.IsChecked = true; }
                currentNode = SettingsNode.monitoredPorts_udp_exclude;
                UdpExcludePorts.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/dhcpServerIgnoreList
                currentNode = SettingsNode.dhcpServerIgnoreList_ipAddress;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                List<string> ignoredDhcpServers = new List<string>();
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        ignoredDhcpServers.Add(nl[i].InnerText);
                }
                DhcpServers.Text = string.Join(", ", ignoredDhcpServers.ToArray());

                // tcpTrigger/endpointIgnoreList
                currentNode = SettingsNode.endpointIgnoreList_ipAddress;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText)
                        && IPAddress.TryParse(nl[i].InnerText, out _))
                    {
                        {
                            WhitelistItems.Add(new WhitelistItem()
                            {
                                IP = nl[i].InnerText,
                                Port = nl[i].Attributes["port"]?.Value,
                                Comment = nl[i].Attributes["comment"]?.Value
                            });
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
                if (xn != null) { LogOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledActions_windowsEventLog;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { EventLogOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledActions_emailNotification;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { SendEmailOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = SettingsNode.enabledActions_executeCommand;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { LaunchAppOption.IsChecked = bool.Parse(xn.InnerText); }

                // tcpTrigger/actionSettings
                currentNode = SettingsNode.actionsSettings_logPath;
                LogPath.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = SettingsNode.actionsSettings_command_path;
                ApplicationPath.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = SettingsNode.actionsSettings_command_arguments;
                ApplicationArguments.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/email
                currentNode = SettingsNode.email_server;
                EmailServer.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = SettingsNode.email_port;
                EmailPort.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                currentNode = SettingsNode.email_isAuthRequired;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsSmtpAuthenticationRequired.IsChecked = bool.Parse(xn.InnerText); }

                // Decrypt username.
                currentNode = SettingsNode.email_username;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null)
                {
                    if (xn.Attributes["encrypted"]?.InnerText == "true")
                    {
                        encryptedValue = xn.InnerText;
                        if (!string.IsNullOrEmpty(encryptedValue))
                            SmtpUsername.Text = StringCipher.Decrypt(encryptedValue);
                        encryptedValue = null;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(xn.InnerText))
                            SmtpUsername.Text = xn.InnerText;
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
                            SmtpPassword.Password = StringCipher.Decrypt(encryptedValue);
                        encryptedValue = null;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(xn.InnerText))
                            SmtpPassword.Password = xn.InnerText;
                    }
                }

                currentNode = SettingsNode.email_recipientList_address;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                List<string> recipients = new List<string>();
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        recipients.Add(nl[i].InnerText);
                }
                // Join recipients list to single string.
                EmailRecipient.Text = string.Join(", ", recipients.ToArray());

                currentNode = SettingsNode.email_sender_address;
                EmailSender.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = SettingsNode.email_sender_displayName;
                EmailSenderFriendly.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/email/message
                currentNode = SettingsNode.email_message_subject;
                EmailSubject.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = SettingsNode.email_message_body;
                EmailBody.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/email/options
                currentNode = SettingsNode.email_options_rateLimitSeconds;
                RateLimitSeconds.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                if (RateLimitSeconds.Text.Length > 0 && int.TryParse(RateLimitSeconds.Text, out int rateLimit) && rateLimit > 0)
                    RateLimitOption.IsChecked = true;
                else
                    RateLimitSeconds.Text = "";
                currentNode = SettingsNode.email_options_bufferSeconds;
                BufferSeconds.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                if (BufferSeconds.Text.Length > 0 && int.TryParse(BufferSeconds.Text, out int bufferSeconds) && bufferSeconds > 0)
                    BufferOption.IsChecked = true;
                else
                    BufferSeconds.Text = "";
            }
            catch (UnauthorizedAccessException)
            {
                ShowMessageBox(
                    message: "You don't have access to read your settings file."
                             + Environment.NewLine + "Try relaunching this application as an administrator."
                             + Environment.NewLine + Environment.NewLine
                             + "Settings location: " + settingsPath,
                    title: "Failed to open your settings",
                    type: DialogWindow.Type.Error);
            }
            catch (Exception ex)
            {
                ShowMessageBox(
                    message: $"Your settings location is '{settingsPath}'.{Environment.NewLine}{Environment.NewLine}"
                             + (currentNode.Length > 0 ? $"Failed parsing XML node '{currentNode}'.{Environment.NewLine}" : "")
                             + ex.Message,
                    title: "Failed to read your settings",
                    type: DialogWindow.Type.Error);
            }
        }
    }
}