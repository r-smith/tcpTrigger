﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Xml;

namespace tcpTrigger.Editor
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
                    writer.WriteElementString("rogueDhcp", MonitorDhcpOption.IsChecked == true ? t : f);
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

                    // DHCP ignore list.
                    writer.WriteStartElement("dhcpServerIgnoreList");
                    if (DhcpServers.Text.Trim().Length > 0)
                    {
                        string[] dhcpServers = DhcpServers.Text.Split(',');
                        for (int i = 0; i < dhcpServers.Length; i++)
                        {
                            if (!string.IsNullOrEmpty(dhcpServers[i]))
                            {
                                writer.WriteElementString("ipAddress", dhcpServers[i].Trim());
                            }
                        }
                    }
                    writer.WriteEndElement();

                    // Ignored endpoints.
                    writer.WriteStartElement("endpointIgnoreList");
                    if (Whitelist.Text.Trim().Length > 0)
                    {
                        try
                        {
                            // Split whitelist by both commas and newlines. Parse each item to IPAddress. Store end result in a List<IPAddress>.
                            List<IPAddress> ignoredEndpoints =
                                Whitelist.Text
                                .Trim()
                                .Split(new string[] { ",", "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(ip => IPAddress.Parse(ip.Trim()))
                                .ToList();
                            // Sort IPs using custom sorter.
                            ignoredEndpoints.Sort(new IPAddressComparer());
                            // Write each IP to config.
                            for (int i = 0; i < ignoredEndpoints.Count; i++)
                            {
                                writer.WriteElementString("ipAddress", ignoredEndpoints[i].ToString().Trim());
                            }
                        }
                        catch
                        {
                            ShowMessageBox(
                                message: "Check to ensure the IP addresses you entered are valid.",
                                title: "Failed to save your whitelist settings",
                                type: DialogWindow.Type.Error);
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
                    writer.WriteElementString("emailRateLimitSeconds", RateLimitSeconds.Text);
                    writer.WriteElementString("logPath", LogPath.Text);
                    writer.WriteStartElement("command");
                    writer.WriteElementString("path", ApplicationPath.Text);
                    writer.WriteElementString("arguments", ApplicationArguments.Text);
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    // Email settings.
                    writer.WriteStartElement("emailSettings");
                    writer.WriteElementString("server", EmailServer.Text);
                    writer.WriteElementString("port", EmailPort.Text);
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
                    writer.WriteEndElement();

                    // Email message.
                    writer.WriteStartElement("emailMessage");
                    writer.WriteElementString("subject", EmailSubject.Text);
                    writer.WriteElementString("body", EmailBody.Text);
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