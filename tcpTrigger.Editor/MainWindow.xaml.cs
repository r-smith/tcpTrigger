using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml;

namespace tcpTrigger.Editor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private HashSet<string> ExcludedNetworkInterfaces = new HashSet<string>();
        private List<TcpTriggerInterface> AllNetworkInterfaces;

        public MainWindow()
        {
            InitializeComponent();

            LoadConfiguration();
            AllNetworkInterfaces = GetNetworkInterfaces();
            AllNetworkInterfaces.Sort((x, y) => x.Description.CompareTo(y.Description));
            Devices.ItemsSource = AllNetworkInterfaces;
        }

        private List<TcpTriggerInterface> GetNetworkInterfaces()
        {
            List<TcpTriggerInterface> networkInterfaces = new List<TcpTriggerInterface>();

            // Enumerate network interfaces.
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    networkInterface.OperationalStatus == OperationalStatus.Up)
                {
                    List<IPAddress> ipAddresses = new List<IPAddress>();
                    foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ipAddresses.Add(address.Address);
                        }
                    }
                    if (ipAddresses.Count > 0)
                    {
                        ipAddresses.Sort(new IPAddressComparer());
                        networkInterfaces.Add(
                                new TcpTriggerInterface(
                                    guid: networkInterface.Id,
                                    description: networkInterface.Description,
                                    isExcluded: ExcludedNetworkInterfaces.Contains(networkInterface.Id),
                                    ipAddresses: ipAddresses,
                                    macAddress: networkInterface.GetPhysicalAddress())
                                );
                    }
                }
            }

            return networkInterfaces;
        }

        private string GetConfigurationPath()
        {
            // Locate the tcpTrigger.xml configuration file.
            // First check the current directory. If not found, use ProgramData, regardless if the file exists or not.
            const string fileName = "tcpTrigger.xml";
            if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + fileName))
                return AppDomain.CurrentDomain.BaseDirectory + fileName;
            else
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\" + fileName;
        }

        private void LoadConfiguration()
        {
            string configurationPath = GetConfigurationPath();

            if (!File.Exists(configurationPath))
            {
                // No configuration was found. This is expected on new installations.
                // Load defaults and abort reading config file.
                LoadDefaults();
                return;
            }

            // currentNode is updated with the XML node path for every element that is read
            // from the configuration file. This aids in debugging and provides more useful
            // error messages when something goes wrong.
            string currentNode = string.Empty;
            try
            {
                var xd = new XmlDocument();
                xd.Load(configurationPath);

                XmlNode xn;
                XmlNodeList nl;
                string encryptedValue;
                // tcpTrigger/enabledComponents
                currentNode = ConfigurationNode.enabledComponents_tcp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { MonitorTcpOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledComponents_icmp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { MonitorIcmpOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledComponents_namePoison;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { MonitorPoisonOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledComponents_rogueDhcp;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { MonitorDhcpOption.IsChecked = bool.Parse(xn.InnerText); }

                // tcpTrigger/monitoredPorts
                currentNode = ConfigurationNode.monitoredPorts_tcp_include;
                TcpIncludePorts.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = ConfigurationNode.monitoredPorts_tcp_exclude;
                TcpExcludePorts.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/dhcpServerIgnoreList
                currentNode = ConfigurationNode.dhcpServerIgnoreList_ipAddress;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                List<string> ignoredDhcpServers = new List<string>();
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        ignoredDhcpServers.Add(nl[i].InnerText);
                }
                DhcpServers.Text = string.Join(", ", ignoredDhcpServers.ToArray());

                // tcpTrigger/endpointIgnoreList
                currentNode = ConfigurationNode.endpointIgnoreList_ipAddress;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                var sb = new StringBuilder();
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        sb.AppendLine(nl[i].InnerText.Trim());
                }
                Whitelist.Text = sb.ToString();

                // tcpTrigger/networkInterfaceExcludeList
                currentNode = ConfigurationNode.networkInterfaceExcludeList_deviceGuid;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        ExcludedNetworkInterfaces.Add(nl[i].InnerText);
                }

                // tcpTrigger/enabledActions
                currentNode = ConfigurationNode.enabledActions_log;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { LogOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledActions_windowsEventLog;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { EventLogOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledActions_emailNotification;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { SendEmailOption.IsChecked = bool.Parse(xn.InnerText); }

                currentNode = ConfigurationNode.enabledActions_executeCommand;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { LaunchAppOption.IsChecked = bool.Parse(xn.InnerText); }

                // tcpTrigger/actionSettings
                currentNode = ConfigurationNode.actionsSettings_rateLimitMinutes;
                RateLimitMinutes.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                if (RateLimitMinutes.Text.Length > 0)
                    RateLimitOption.IsChecked = true;

                currentNode = ConfigurationNode.actionsSettings_logPath;
                LogPath.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = ConfigurationNode.actionsSettings_command_path;
                ApplicationPath.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = ConfigurationNode.actionsSettings_command_arguments;
                ApplicationArguments.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/emailConfiguration
                currentNode = ConfigurationNode.emailConfiguration_server;
                EmailServer.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = ConfigurationNode.emailConfiguration_port;
                EmailPort.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                currentNode = ConfigurationNode.emailConfiguration_isAuthRequired;
                xn = xd.DocumentElement.SelectSingleNode(currentNode);
                if (xn != null) { IsSmtpAuthenticationRequired.IsChecked = bool.Parse(xn.InnerText); }

                // Decrypt username.
                currentNode = ConfigurationNode.emailConfiguration_username;
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
                currentNode = ConfigurationNode.emailConfiguration_password;
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

                currentNode = ConfigurationNode.emailConfiguration_recipientList_address;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                List<string> recipients = new List<string>();
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        recipients.Add(nl[i].InnerText);
                }
                // Join recipients list to single string.
                EmailRecipient.Text = string.Join(", ", recipients.ToArray());

                currentNode = ConfigurationNode.emailConfiguration_sender_address;
                EmailSender.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;
                currentNode = ConfigurationNode.emailConfiguration_sender_displayName;
                EmailSenderFriendly.Text = xd.DocumentElement.SelectSingleNode(currentNode)?.InnerText;

                // tcpTrigger/customMessage
                currentNode = ConfigurationNode.customMessage;
                nl = xd.DocumentElement.SelectNodes(currentNode);
                for (int i = 0; i < nl.Count; i++)
                {
                    if (nl[i].Attributes["type"]?.InnerText == "tcp")
                    {
                        TcpMessageBody.Text = nl[i].SelectSingleNode("body")?.InnerText;
                    }
                    if (nl[i].Attributes["type"]?.InnerText == "icmp")
                    {
                        IcmpMessageBody.Text = nl[i].SelectSingleNode("body")?.InnerText;
                    }
                    if (nl[i].Attributes["type"]?.InnerText == "namePoison")
                    {
                        NamePoisonMessageBody.Text = nl[i].SelectSingleNode("body")?.InnerText;
                    }
                    if (nl[i].Attributes["type"]?.InnerText == "rogueDhcp")
                    {
                        RogueDhcpMessageBody.Text = nl[i].SelectSingleNode("body")?.InnerText;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowMessageBox(
                    message: $"Your configuration file is: '{configurationPath}'{Environment.NewLine}Unable to parse XML node: {currentNode}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    title: "Failed to read your settings",
                    type: DialogWindow.Type.Error);
            }
        }

        private bool WriteConfiguration()
        {
            string configurationPath = GetConfigurationPath();
            const string t = "true";
            const string f = "false";

            // Ensure the base directory for the configuration file exists before attempting to write.
            if (!Directory.Exists(Path.GetDirectoryName(configurationPath)))
            {
                ShowMessageBox(
                    message: $"The folder '{Path.GetDirectoryName(configurationPath)}' was not found.",
                    title: "Failed to save your settings",
                    type: DialogWindow.Type.Error);
                return false;
            }

            // Use options specified in GUI to write to configuration file.
            try
            {
                // Convert port numbers to an ordered int array and remove duplicates.
                // Port numbers can be entered comma-separated and also specified using ranges with '-'.
                if (TcpIncludePorts.Text.Length > 0)
                {
                    int[] portNumbers = (from part in TcpIncludePorts.Text.Split(',')
                                         let range = part.Split('-')
                                         let start = int.Parse(range[0])
                                         let end = int.Parse(range[range.Length - 1])
                                         from i in Enumerable.Range(start, end - start + 1)
                                         orderby i
                                         select i).Distinct().ToArray();
                    TcpIncludePorts.Text = FormatTcpPortRange(portNumbers);
                }
                if (TcpExcludePorts.Text.Length > 0)
                {
                    int[] portNumbers = (from part in TcpExcludePorts.Text.Split(',')
                                         let range = part.Split('-')
                                         let start = int.Parse(range[0])
                                         let end = int.Parse(range[range.Length - 1])
                                         from i in Enumerable.Range(start, end - start + 1)
                                         orderby i
                                         select i).Distinct().ToArray();
                    TcpExcludePorts.Text = FormatTcpPortRange(portNumbers);
                }

                using (XmlWriter writer = XmlWriter.Create(configurationPath, new XmlWriterSettings() { Indent = true }))
                {
                    // Start tags.
                    writer.WriteStartDocument();
                    writer.WriteStartElement("tcpTrigger");

                    // Enabled components.
                    writer.WriteStartElement("enabledComponents");
                    writer.WriteElementString("tcp", MonitorTcpOption.IsChecked == true ? t : f);
                    writer.WriteElementString("icmp", MonitorIcmpOption.IsChecked == true ? t : f);
                    writer.WriteElementString("namePoison", MonitorPoisonOption.IsChecked == true ? t : f);
                    writer.WriteElementString("rogueDhcp", MonitorDhcpOption.IsChecked == true ? t : f);
                    writer.WriteEndElement();

                    // Monitored ports.
                    writer.WriteStartElement("monitoredPorts");
                    writer.WriteStartElement("tcp");
                    writer.WriteElementString("include", TcpIncludePorts.Text);
                    writer.WriteElementString("exclude", TcpExcludePorts.Text);
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
                    writer.WriteElementString("rateLimitMinutes", RateLimitMinutes.Text);
                    writer.WriteElementString("logPath", LogPath.Text);
                    writer.WriteStartElement("command");
                    writer.WriteElementString("path", ApplicationPath.Text);
                    writer.WriteElementString("arguments", ApplicationArguments.Text);
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    // Email configuration.
                    writer.WriteStartElement("emailConfiguration");
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
                        // Multiple recipients. Split and add each to configuration.
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
                        // Single recipient. Add to configuration.
                        writer.WriteElementString("address", EmailRecipient.Text);
                    }
                    writer.WriteEndElement();
                    writer.WriteStartElement("sender");
                    writer.WriteElementString("address", EmailSender.Text);
                    writer.WriteElementString("displayName", EmailSenderFriendly.Text);
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    // Custom messages.
                    writer.WriteStartElement("customMessage");
                    writer.WriteAttributeString("type", "tcp");
                    writer.WriteElementString("subject", EmailSubject.Text);
                    writer.WriteElementString("body", TcpMessageBody.Text);
                    writer.WriteEndElement();
                    writer.WriteStartElement("customMessage");
                    writer.WriteAttributeString("type", "icmp");
                    writer.WriteElementString("subject", EmailSubject.Text);
                    writer.WriteElementString("body", IcmpMessageBody.Text);
                    writer.WriteEndElement();
                    writer.WriteStartElement("customMessage");
                    writer.WriteAttributeString("type", "namePoison");
                    writer.WriteElementString("subject", EmailSubject.Text);
                    writer.WriteElementString("body", NamePoisonMessageBody.Text);
                    writer.WriteEndElement();
                    writer.WriteStartElement("customMessage");
                    writer.WriteAttributeString("type", "rogueDhcp");
                    writer.WriteElementString("subject", EmailSubject.Text);
                    writer.WriteElementString("body", RogueDhcpMessageBody.Text);
                    writer.WriteEndElement();

                    // End tags.
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                ShowMessageBox(
                    message: "A problem was encountered while saving your configuration file."
                             + Environment.NewLine + Environment.NewLine + ex.Message,
                    title: "Failed to save your settings",
                    type: DialogWindow.Type.Error);
                return false;
            }

            return true;
        }

        private bool ValidateUserSettings()
        {
            if (MonitorTcpOption.IsChecked == true)
            {
                if (
                    (TcpIncludePorts.Text.Length > 0 && !IsTcpPortsValid(TcpIncludePorts.Text))
                    || (TcpExcludePorts.Text.Length > 0 && !IsTcpPortsValid(TcpExcludePorts.Text)))
                {
                    ShowMessageBox(
                        message: "A port must be a number between 1-65535." + Environment.NewLine
                                 + "Use a comma to specify multiple ports." + Environment.NewLine
                                 + "Use a hyphen to specify ranges." + Environment.NewLine + Environment.NewLine
                                 + "Example: 21,23,400-450,3389",
                        title: "Invalid port number(s)",
                        type: DialogWindow.Type.Error,
                        tab: MainTab);
                    return false;
                }
            }

            if (MonitorDhcpOption.IsChecked == true && DhcpServers.Text.Length > 0)
            {
                if (!IsIpAddressListValid(DhcpServers.Text))
                {
                    ShowMessageBox(
                        message: "DHCP servers must be entered by IP address. If you have more than one server, use a comma to separate them.",
                        title: "Invalid DHCP server(s)",
                        type: DialogWindow.Type.Error,
                        tab: MainTab,
                        control: DhcpServers);
                    return false;
                }
            }

            if (LogOption.IsChecked == true)
            {
                if (LogPath.Text.Length == 0)
                {
                    ShowMessageBox(
                        message: "You have the option to write to a log file enabled, but no path was entered.",
                        title: "No log path specified",
                        type: DialogWindow.Type.Error,
                        tab: ActionsTab,
                        control: LogPath);
                    return false;
                }
            }

            if (LaunchAppOption.IsChecked == true)
            {
                if (ApplicationPath.Text.Length == 0)
                {
                    ShowMessageBox(
                        message: "You have the option to launch an external application enabled, but no path was entered.",
                        title: "No application path specified",
                        type: DialogWindow.Type.Error,
                        tab: ActionsTab,
                        control: ApplicationPath);
                    return false;
                }
            }

            if (SendEmailOption.IsChecked == true)
            {
                //if (EmailSubject.Text.Length == 0)
                //{
                //    return false;
                //}

                if (EmailServer.Text.Length == 0)
                {
                    ShowMessageBox(
                        message: "You have email notifications enabled, but no email server was entered.",
                        title: "Missing email server",
                        tab: EmailTab,
                        control: EmailServer);
                    return false;
                }

                if (EmailPort.Text.Length == 0
                    || !int.TryParse(EmailPort.Text, out int n)
                    || n <= 0
                    || n > 65535)
                {
                    ShowMessageBox(
                        message: "Enter the port number used by your email server."
                                 + Environment.NewLine + "Typical port numbers include: 25, 465, and 587.",
                        title: "Missing email server port",
                        type: DialogWindow.Type.Error,
                        tab: EmailTab,
                        control: EmailPort);
                    return false;
                }

                if (EmailRecipient.Text.Length == 0 || !IsEmailAddressListValid(EmailRecipient.Text))
                {
                    ShowMessageBox(
                        message: "Check to ensure the recipient you entered is formatted like a standard email address. "
                                 + Environment.NewLine + "If you have more than one recipient, use a comma to separate them.",
                        title: "Invalid email recipient(s)",
                        type: DialogWindow.Type.Error,
                        tab: EmailTab,
                        control: EmailRecipient);
                    return false;
                }

                if (EmailSender.Text.Length == 0 || !IsEmailAddressListValid(EmailSender.Text) || EmailSender.Text.Contains(','))
                {
                    ShowMessageBox(
                        message: "Check to ensure the sender address you entered is formatted like a standard email address.",
                        title: "Invalid email sender",
                        type: DialogWindow.Type.Error,
                        tab: EmailTab,
                        control: EmailSender);
                    return false;
                }
            }

            if (Whitelist.Text.Length > 0 && !IsIpAddressListValid(Whitelist.Text))
            {
                ShowMessageBox(
                        message: "Check to ensure the IP addresses you entered are formatted properly."
                                 + Environment.NewLine + "IP addresses can be entered one per line, or comma-separated.",
                        title: "Invalid whitelist",
                        type: DialogWindow.Type.Error,
                        tab: WhitelistTab,
                        control: Whitelist);
                return false;
            }

            if (RateLimitMinutes.Text.Length > 0)
            {
                if (!int.TryParse(RateLimitMinutes.Text, out int n) || n <= 0 || n > 3600)
                {
                    ShowMessageBox(
                        message: "The number of seconds used for rate limiting must be between 1 and 3600.",
                        title: "Invalid rate limit",
                        type: DialogWindow.Type.Error,
                        tab: AdvancedTab,
                        control: RateLimitMinutes);
                    return false;
                }
            }

            return true;
        }

        private bool IsTcpPortsValid(string ports)
        {
            try
            {
                int[] portNumbers = (from part in ports.Split(',')
                                     let range = part.Split('-')
                                     let start = int.Parse(range[0])
                                     let end = int.Parse(range[range.Length - 1])
                                     from i in Enumerable.Range(start, end - start + 1)
                                     orderby i
                                     select i).Distinct().ToArray();
                string result = string.Join(",", portNumbers.Select(x => x.ToString()).ToArray());

                for (int i = 0; i < portNumbers.Length; ++i)
                    if (portNumbers[i] < 1 || portNumbers[i] > 65535)
                        return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        private bool IsIpAddressListValid(string ipAddresses)
        {
            // Validate list of IP addresses in a string. IP addresses can be separated by commas or newlines.
            // Note: The IPAddress.TryParse() method used here returns true for any string that can be parsed as an Int64.

            // Split string to array, split by both commas and newlines, then trim each item.
            string[] ipArray =
                ipAddresses
                .Trim()
                .Split(new string[] { ",", "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ip => ip.Trim()).ToArray();
            for (int i = 0; i < ipArray.Length; i++)
            {
                if (!IPAddress.TryParse(ipArray[i], out _))
                    return false;
            }
            return true;
        }

        private bool IsEmailAddressListValid(string emails)
        {
            // Validate a comma-separated list of email addresses.
            try
            {
                string[] emailArray = emails.Trim().Split(',');
                for (int i = 0; i < emailArray.Length; i++)
                {
                    _ = new MailAddress(emailArray[i].Trim());
                }
                return true;
            }
            catch
            {
                return false;
            }
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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateUserSettings() == false)
                return;

            if (WriteConfiguration() == false)
                return;

            RestartTcpTriggerService();
        }

        private void RestartTcpTriggerService()
        {
            // Setup a background thread to restart the service.
            var bw = new BackgroundWorker();
            bw.DoWork += new DoWorkEventHandler(Thread_RestartService);
            bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(Thread_RestartServiceCompleted);
            bw.RunWorkerAsync();
        }

        private void Thread_RestartService(object sender, DoWorkEventArgs e)
        {
            try
            {
                var sc = new ServiceController("tcpTrigger");
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped);
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running);
                }
            }
            catch (Exception ex)
            {
                e.Result =
                    $"Failed to restart tcpTrigger service. Configuration changes not applied.{Environment.NewLine}{Environment.NewLine}{ex.Message}";
                return;
            }
        }

        private void Thread_RestartServiceCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Result != null)
            {
                ShowMessageBox((string)e.Result);
            }
            else
            {
                ShowMessageBox(
                    message: "Your settings were saved.",
                    title: "Success",
                    type: DialogWindow.Type.Info);
            }
        }

        private void BrowseLogPath_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                try
                {
                    dialog.Description = "Select a folder for the log file.";
                    dialog.ShowNewFolderButton = true;
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        LogPath.Text = dialog.SelectedPath + @"\connections.log";
                }
                catch (Exception ex)
                {
                    ShowMessageBox(ex.Message);
                }
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog())
            {
                try
                {
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        ApplicationPath.Text = dialog.FileName;
                }
                catch (Exception ex)
                {
                    ShowMessageBox(ex.Message);
                }
            }
        }

        private void LogOption_Click(object sender, RoutedEventArgs e)
        {
            // When checked, set default log path if there isn't already a path set.
            string defaultLogPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\connections.log";
            if (LogOption.IsChecked == true && LogPath.Text.Length == 0)
                LogPath.Text = defaultLogPath;
            else if (LogOption.IsChecked == false && LogPath.Text == defaultLogPath)
                LogPath.Text = string.Empty;
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            bool isWindowOpen = false;

            foreach (Window wnd in Application.Current.Windows)
            {
                if (wnd is HelpWindow)
                {
                    isWindowOpen = true;
                    wnd.Activate();
                }
            }

            if (!isWindowOpen)
            {
                HelpWindow newWnd = new HelpWindow();
                newWnd.Show();
            }
        }

        private void NumericTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            var regex = new Regex("[^0-9.-]+");
            if (regex.IsMatch(e.Text))
                e.Handled = true;
        }

        private void TcpPorts_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            var regex = new Regex("[^0-9,\\-\\s.-]+");
            if (regex.IsMatch(e.Text))
                e.Handled = true;
        }

        private void IsSmtpAuthenticationRequired_Click(object sender, RoutedEventArgs e)
        {
            if (IsSmtpAuthenticationRequired.IsChecked == true)
                SmtpUsername.Focus();
        }

        private void TestEmail_Click(object sender, RoutedEventArgs e)
        {
            TestEmail.IsEnabled = false;
            TestEmail.Content = "Testing...";

            // Setup background worker to send test email in separate thread.
            var emailTester = new BackgroundWorker();
            emailTester.DoWork += EmailTester_DoWork;
            emailTester.RunWorkerCompleted += EmailTester_RunWorkerCompleted;
            emailTester.RunWorkerAsync(new EmailConfiguration()
            {
                Server = EmailServer.Text,
                Port = EmailPort.Text,
                IsAuthRequired = IsSmtpAuthenticationRequired.IsChecked == true,
                Username = SmtpUsername.Text,
                Password = SmtpPassword.SecurePassword,
                From = EmailSender.Text,
                FromFriendly = EmailSenderFriendly.Text,
                Recipient = EmailRecipient.Text
            });
        }

        private void EmailTester_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Result != null)
            {
                ShowMessageBox(
                    message: (string)e.Result,
                    title: "Failed to send test email",
                    type: DialogWindow.Type.Error);
            }
            else
            {
                ShowMessageBox(
                    message: "A test message was sent.",
                    title: "Email test",
                    type: DialogWindow.Type.Info);
            }

            TestEmail.IsEnabled = true;
            TestEmail.Content = "Test";
        }

        private void EmailTester_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                EmailConfiguration parameters = e.Argument as EmailConfiguration;
                SmtpClient smtpClient = new SmtpClient();
                smtpClient.Host = parameters.Server;
                if (parameters.Port.Length > 0)
                {
                    smtpClient.Port = int.Parse(parameters.Port);
                }
                if (parameters.IsAuthRequired)
                {
                    smtpClient.Credentials = new NetworkCredential(parameters.Username, parameters.Password.ToString());
                }

                using (MailMessage message = new MailMessage())
                {
                    message.From = new MailAddress(parameters.From, parameters.FromFriendly); ;
                    message.Subject = "tcpTrigger Test Email";
                    message.Body = $"This is a test email notification sent by tcpTrigger on {DateTime.Now.ToLongDateString()} at {DateTime.Now.ToLongTimeString()}.";
                    // Check if multiple recipients were provided.
                    if (parameters.Recipient.Contains(","))
                    {
                        // Multiple recipients. Split and add each to mail message.
                        string[] recips = parameters.Recipient.Split(',');
                        for (int i = 0; i < recips.Length; i++)
                        {
                            if (!string.IsNullOrEmpty(recips[i]))
                            {
                                message.To.Add(recips[i].Trim());
                            }
                        }
                    }
                    else
                    {
                        // Single recipient. Add to mail message.
                        message.To.Add(parameters.Recipient);
                    }

                    //Send the email.
                    smtpClient.Send(message);
                }
            }
            catch (Exception ex)
            {
                e.Result = ex.Message;
            }
        }

        private void ShowMessageBox(string message, string title = "Error", DialogWindow.Type type = DialogWindow.Type.Error, TabItem tab = null, Control control = null)
        {
            DialogWindow wnd;
            switch (type)
            {
                case DialogWindow.Type.Error:
                    wnd = DialogWindow.Error(message, title);
                    break;
                case DialogWindow.Type.Info:
                    wnd = DialogWindow.Info(message, title);
                    break;
                default:
                    wnd = DialogWindow.Error(message, title);
                    break;
            }
            
            if (IsLoaded)
            {
                wnd.Owner = this;
                tab?.Focus();
            }
            else
            {
                // Don't set owner because the application hasn't yet loaded.
                wnd.ShowInTaskbar = true;
                wnd.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            // Blur main window, show dialog, then restore opacity.
            Opacity = 0.65;
            wnd.ShowDialog();
            Opacity = 1;

            // Set focus to control, if passed in.
            control?.Focus();
        }

        private void LoadDefaults()
        {
            MonitorIcmpOption.IsChecked = true;
            MonitorTcpOption.IsChecked = true;
            TcpIncludePorts.Text = "1-65535";
            LogOption.IsChecked = true;
            LogPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\connections.log";
        }
    }
}
