﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Xml;

namespace tcpTrigger.Manager
{
    public partial class MainWindow : Window
    {
        private bool WriteSettings()
        {
            string settingsPath = GetSettingsPath();
            const string t = "true";
            const string f = "false";
            
            // Ensure the base directory for the settings file exists before attempting to write.
            if (!Directory.Exists(Path.GetDirectoryName(settingsPath)))
            {
                ShowMessageBox(
                    message: $"The folder '{Path.GetDirectoryName(settingsPath)}' was not found.",
                    title: "Failed to save your settings",
                    type: DialogWindow.Type.Error);
                return false;
            }

            // Use options specified in GUI to write to settings file.
            try
            {
                // Convert port numbers to an ordered int array and remove duplicates.
                // Port numbers can be entered comma-separated and also specified using ranges with '-'.
                if (TcpAllPortsOption.IsChecked == false && TcpIncludePorts.Text.Length > 0)
                {
                    TcpIncludePorts.Text = FormatTcpPortRange(PortStringToArray(TcpIncludePorts.Text));
                }
                if (TcpExcludePorts.Text.Length > 0)
                {
                    TcpExcludePorts.Text = FormatTcpPortRange(PortStringToArray(TcpExcludePorts.Text));
                }
                if (UdpAllPortsOption.IsChecked == false && UdpIncludePorts.Text.Length > 0)
                {
                    UdpIncludePorts.Text = FormatTcpPortRange(PortStringToArray(UdpIncludePorts.Text));
                }
                if (UdpExcludePorts.Text.Length > 0)
                {
                    UdpExcludePorts.Text = FormatTcpPortRange(PortStringToArray(UdpExcludePorts.Text));
                }

                using (XmlWriter writer = XmlWriter.Create(settingsPath, new XmlWriterSettings() { Indent = true }))
                {
                    // Start tags.
                    writer.WriteStartDocument();
                    writer.WriteStartElement("tcpTrigger");

                    // Enabled components.
                    writer.WriteStartElement("enabledComponents");
                    writer.WriteElementString("tcp", MonitorTcpOption.IsChecked == true ? t : f);
                    writer.WriteElementString("udp", MonitorUdpOption.IsChecked == true ? t : f);
                    writer.WriteElementString("icmp", MonitorIcmpOption.IsChecked == true ? t : f);
                    writer.WriteEndElement();

                    // Monitored ports.
                    writer.WriteStartElement("monitoredPorts");
                    writer.WriteStartElement("tcp");
                    writer.WriteElementString("include", (TcpAllPortsOption.IsChecked == true) ? "1-65535" : TcpIncludePorts.Text);
                    writer.WriteElementString("exclude", TcpExcludePorts.Text);
                    writer.WriteEndElement();
                    writer.WriteStartElement("udp");
                    writer.WriteElementString("include", (UdpAllPortsOption.IsChecked == true) ? "1-65535" : UdpIncludePorts.Text);
                    writer.WriteElementString("exclude", UdpExcludePorts.Text);
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    // Ignored endpoints.
                    writer.WriteStartElement("endpointIgnoreList");
                    if (WhitelistItems.Count > 0)
                    {
                        List<WhitelistItem> sortedWhitelist = new List<WhitelistItem>();
                        foreach (WhitelistItem item in WhitelistItems)
                        {
                            // Skip entries that don't parse to an IP address.
                            if (string.IsNullOrWhiteSpace(item.IP)
                                || IPAddress.TryParse(item.IP, out _) == false)
                            {
                                continue;
                            }

                            string port = string.Empty;
                            // Check if this whitelist item has a port specified.
                            if (!string.IsNullOrEmpty(item.Port))
                            {
                                // Port is specified. Check if it parses to a number.
                                if (int.TryParse(item.Port, out int p))
                                {
                                    // Port parses to a number. If outside valid port range, set to empty string.
                                    port = p < 1 || p > 65535 ? string.Empty : item.Port;
                                }
                                else
                                {
                                    // Port does not parse to a number. If not equal to "icmp", set to empty string.
                                    port = !item.Port.ToLower().Equals("icmp") ? string.Empty : item.Port.ToLower();
                                }
                            }

                            sortedWhitelist.Add(new WhitelistItem()
                            {
                                IP = item.IP, Port = port, Comment = item.Comment
                            });
                        }

                        // Sort by IP, then by port.
                        sortedWhitelist = sortedWhitelist
                            .OrderBy(x => IPAddress.Parse(x.IP), new IPAddressComparer())
                            .ThenBy(p => p.Port, new PortComparer())
                            .ToList();

                        foreach (WhitelistItem item in sortedWhitelist)
                        {
                            writer.WriteStartElement("ipAddress");
                            if (!string.IsNullOrWhiteSpace(item.Comment))
                                writer.WriteAttributeString("comment", item.Comment);
                            if (!string.IsNullOrWhiteSpace(item.Port))
                                writer.WriteAttributeString("port", item.Port);
                            writer.WriteString(item.IP);
                            writer.WriteEndElement();
                        }
                    }
                    writer.WriteEndElement();

                    // Excluded network interfaces.
                    writer.WriteStartElement("networkInterfaceExcludeList");
                    for (int i = 0; i < AllNetworkInterfaces.Count; i++)
                    {
                        if (AllNetworkInterfaces[i].IsExcluded)
                        {
                            writer.WriteElementString("deviceGuid", AllNetworkInterfaces[i].Guid);
                        }
                    }
                    writer.WriteEndElement();

                    // Enabled actions.
                    writer.WriteStartElement("enabledActions");
                    writer.WriteElementString("log", LogOption.IsChecked == true ? t : f);
                    writer.WriteElementString("windowsEventLog", EventLogOption.IsChecked == true ? t : f);
                    writer.WriteElementString("emailNotification", SendEmailOption.IsChecked == true ? t : f);
                    writer.WriteElementString("executeCommand", LaunchAppOption.IsChecked == true ? t : f);
                    writer.WriteEndElement();

                    // Action settings.
                    writer.WriteStartElement("actionSettings");
                    writer.WriteElementString("logPath", LogPath.Text);
                    writer.WriteStartElement("command");
                    writer.WriteElementString("path", ApplicationPath.Text);
                    writer.WriteElementString("arguments", ApplicationArguments.Text);
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    // Email settings.
                    writer.WriteStartElement("email");
                    writer.WriteElementString("server", EmailServer.Text);
                    writer.WriteElementString("port", EmailPort.Text);
                    writer.WriteElementString("isTlsEnabled", IsTlsEnabled.IsChecked == true ? t : f);
                    writer.WriteElementString("isAuthRequired", IsSmtpAuthenticationRequired.IsChecked == true ? t : f);
                    // Encrypt username.
                    if (!string.IsNullOrEmpty(SmtpUsername.Text))
                    {
                        writer.WriteStartElement("username");
                        writer.WriteAttributeString("encrypted", t);
                        writer.WriteString(StringCipher.Encrypt(SmtpUsername.Text));
                        writer.WriteEndElement();
                    }
                    else
                    {
                        writer.WriteElementString("username", null);
                    }
                    // Encrypt password.
                    if (!string.IsNullOrEmpty(SmtpPassword.Password.ToString()))
                    {
                        writer.WriteStartElement("password");
                        writer.WriteAttributeString("encrypted", t);
                        writer.WriteString(StringCipher.Encrypt(SmtpPassword.Password.ToString()));
                        writer.WriteEndElement();
                    }
                    else
                    {
                        writer.WriteElementString("password", null);
                    }
                    writer.WriteStartElement("recipientList");
                    // Check if multiple recipients were provided.
                    if (EmailRecipient.Text.Contains(","))
                    {
                        // Multiple recipients. Split and add each to settings.
                        string[] recips = EmailRecipient.Text.Split(',');
                        for (int i = 0; i < recips.Length; i++)
                        {
                            if (!string.IsNullOrEmpty(recips[i]))
                            {
                                writer.WriteElementString("address", recips[i].Trim());
                            }
                        }
                    }
                    else
                    {
                        // Single recipient. Add to settings.
                        writer.WriteElementString("address", EmailRecipient.Text);
                    }
                    writer.WriteEndElement();
                    writer.WriteStartElement("sender");
                    writer.WriteElementString("address", EmailSender.Text);
                    writer.WriteElementString("displayName", EmailSenderFriendly.Text);
                    writer.WriteEndElement();

                    // Email message.
                    writer.WriteStartElement("message");
                    writer.WriteElementString("subject", EmailSubject.Text);
                    writer.WriteElementString("body", EmailBody.Text);
                    writer.WriteEndElement();

                    // Email options.
                    writer.WriteStartElement("options");
                    writer.WriteElementString("rateLimitSeconds", RateLimitOption.IsChecked == true ? RateLimitSeconds.Text : "0");
                    writer.WriteElementString("bufferSeconds", BufferOption.IsChecked == true ? BufferSeconds.Text : "0");
                    writer.WriteEndElement();
                    // End email section.
                    writer.WriteEndElement();


                    // End tags.
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (UnauthorizedAccessException)
            {
                ShowMessageBox(
                    message: "You don't have access to write to your settings file."
                             + Environment.NewLine + "Try relaunching this application as an administrator."
                             + Environment.NewLine + Environment.NewLine
                             + "Settings location: " + settingsPath,
                    title: "Failed to save your settings",
                    type: DialogWindow.Type.Error);
                return false;
            }
            catch (Exception ex)
            {
                ShowMessageBox(
                    message: ex.Message,
                    title: "Failed to save your settings",
                    type: DialogWindow.Type.Error);
                return false;
            }

            return true;
        }

        private int[] PortStringToArray(string ports)
        {
            return (from part in ports.Split(',')
                    let range = part.Split('-')
                    let start = int.Parse(range[0])
                    let end = int.Parse(range[range.Length - 1])
                    from i in Enumerable.Range(start, end - start + 1)
                    orderby i
                    select i).Distinct().ToArray();
        }

        private string FormatTcpPortRange(int[] range)
        {
            // Format an array of ints as a string.
            // Each number is seprated by a comma.  Ranges of consecutive numbers are separated by a hyphen.

            var sb = new StringBuilder();

            int start = range[0];
            int end = range[0];
            for (int i = 1; i < range.Length; ++i)
            {
                if (range[i] == (range[i - 1] + 1))
                {
                    end = range[i];
                }
                else
                {
                    if (start == end)
                        sb.Append($"{start},");
                    else if (start + 1 == end)
                        sb.Append($"{start},{end},");
                    else
                        sb.Append($"{start}-{end},");

                    start = end = range[i];
                }
            }

            if (start == end)
                sb.Append(start);
            else if (start + 1 == end)
                sb.Append($"{start},{end}");
            else
                sb.Append($"{start}-{end}");

            return sb.ToString();
        }
    }
}