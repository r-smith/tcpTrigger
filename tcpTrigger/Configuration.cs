﻿using Microsoft.Win32;
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
        public bool IsPopupMessageEnabled { get; private set; }
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

        private const string RegistryKey = @"System\CurrentControlSet\services\tcpTrigger";
        private const string RegistryValue = "ImagePath";

        private string GetConfigurationPath()
        {
            string installPath = string.Empty;

            // Get the install path for the tcpTrigger service from the Window registry.
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RegistryKey))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(RegistryValue);
                        if (value != null)
                        {
                            installPath = Path.GetDirectoryName((value as string).Trim('"'));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Failed to start the tcpTrigger service. Error retrieving tcpTrigger installation path from the Windows registry.{Environment.NewLine}{ex.Message}",
                    EventLogEntryType.Error,
                    400);
            }

            return (installPath.Length > 0) ? installPath + @"\tcpTrigger.xml" : string.Empty;
        }

        public bool Load()
        {
            string configuratonPath = GetConfigurationPath();

            if (string.IsNullOrEmpty(configuratonPath))
            {
                return false;
            }

            try
            {
                var xd = new XmlDocument();
                xd.Load(configuratonPath);

                XmlNode xn;
                XmlNodeList nl;
                string encryptedValue;
                // tcpTrigger/enabledComponents
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledComponents/tcp");
                if (xn != null) { IsMonitorTcpEnabled = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledComponents/icmp");
                if (xn != null) { IsMonitorIcmpEnabled = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledComponents/namePoison");
                if (xn != null) { IsMonitorPoisonEnabled = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledComponents/rogueDhcp");
                if (xn != null) { IsMonitorDhcpEnabled = bool.Parse(xn.InnerText); }

                if (IsMonitorTcpEnabled)
                {
                    // tcpTrigger/monitoredPorts
                    TcpPortsToMonitorAsString =
                        xd.DocumentElement.SelectSingleNode("/tcpTrigger/monitoredPorts/tcp/include")?.InnerText;
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
                    nl = xd.DocumentElement.SelectNodes("/tcpTrigger/dhcpServerIgnoreList/ipAddress");
                    for (int i = 0; i < nl.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(nl[i].InnerText))
                            IgnoredDhcpServers.Add(IPAddress.Parse(nl[i].InnerText));
                    }
                }

                // tcpTrigger/endpointIgnoreList
                nl = xd.DocumentElement.SelectNodes("/tcpTrigger/endpointIgnoreList/ipAddress");
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        IgnoredEndpoints.Add(IPAddress.Parse(nl[i].InnerText));
                }

                // tcpTrigger/networkInterfaceExcludeList
                nl = xd.DocumentElement.SelectNodes("/tcpTrigger/networkInterfaceExcludeList/deviceGuid");
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        ExcludedNetworkInterfaces.Add(nl[i].InnerText);
                }

                // tcpTrigger/enabledActions
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledActions/windowsEventLog");
                if (xn != null) { IsEventLogEnabled = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledActions/emailNotification");
                if (xn != null) { IsEmailNotificationEnabled = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledActions/popupNotification");
                if (xn != null) { IsPopupMessageEnabled = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledActions/executeCommand");
                if (xn != null) { IsExternalAppEnabled = bool.Parse(xn.InnerText); }

                // tcpTrigger/actionSettings
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/actionSettings/rateLimitMinutes");
                if (xn != null) { ActionRateLimitMinutes = int.Parse(xn.InnerText); }
                else { ActionRateLimitMinutes = 0; }
                TriggeredApplicationPath =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/actionSettings/command/path")?.InnerText;
                TriggeredApplicationArguments =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/actionSettings/command/arguments")?.InnerText;

                // tcpTrigger/emailConfiguration
                EmailServer =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/server")?.InnerText;
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/port");
                if (xn != null && !string.IsNullOrEmpty(xn.InnerText)) { EmailServerPort = int.Parse(xn.InnerText); }
                else { EmailServerPort = 25; }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/isAuthRequired");
                if (xn != null) { IsEmailAuthRequired = bool.Parse(xn.InnerText); }
                // Decrypt username.
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/username");
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
                // Decrypt password.
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/password");
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

                nl = xd.DocumentElement.SelectNodes("/tcpTrigger/emailConfiguration/recipientList/address");
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        EmailRecipients.Add(nl[i].InnerText);
                }
                EmailSender =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/sender/address")?.InnerText;
                EmailSenderDisplayName =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/sender/displayName")?.InnerText;

                // tcpTrigger/customMessage
                nl = xd.DocumentElement.SelectNodes("/tcpTrigger/customMessage");
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
                    $"Failed to start the tcpTrigger service. Error parsing configuration file '{configuratonPath}'.{Environment.NewLine}{ex.Message}",
                    EventLogEntryType.Error,
                    400);
                return false;
            }
        }
    }
}
