using Microsoft.Win32;
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
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork || address.Address.AddressFamily == AddressFamily.InterNetworkV6)
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
                // Should any defaults get set or should a warning be displayed?
                // At the moment, abort loading and do nothing.
                return;
            }

            try
            {
                var xd = new XmlDocument();
                xd.Load(configurationPath);

                XmlNode xn;
                XmlNodeList nl;
                string encryptedValue;
                // tcpTrigger/enabledComponents
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledComponents/tcp");
                if (xn != null) { MonitorTcpOption.IsChecked = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledComponents/icmp");
                if (xn != null) { MonitorIcmpOption.IsChecked = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledComponents/namePoison");
                if (xn != null) { MonitorPoisonOption.IsChecked = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledComponents/rogueDhcp");
                if (xn != null) { MonitorDhcpOption.IsChecked = bool.Parse(xn.InnerText); }
                // tcpTrigger/monitoredPorts
                TcpIncludePorts.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/monitoredPorts/tcp/include")?.InnerText;
                TcpExcludePorts.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/monitoredPorts/tcp/exclude")?.InnerText;
                // tcpTrigger/dhcpServerIgnoreList
                nl = xd.DocumentElement.SelectNodes("/tcpTrigger/dhcpServerIgnoreList/ipAddress");
                List<string> ignoredDhcpServers = new List<string>();
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        ignoredDhcpServers.Add(nl[i].InnerText);
                }
                DhcpServers.Text = string.Join(", ", ignoredDhcpServers.ToArray());
                // tcpTrigger/endpointIgnoreList
                nl = xd.DocumentElement.SelectNodes("/tcpTrigger/endpointIgnoreList/ipAddress");
                var sb = new StringBuilder();
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        sb.AppendLine(nl[i].InnerText.Trim());
                }
                Whitelist.Text = sb.ToString();
                // tcpTrigger/networkInterfaceExcludeList
                nl = xd.DocumentElement.SelectNodes("/tcpTrigger/networkInterfaceExcludeList/deviceGuid");
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        ExcludedNetworkInterfaces.Add(nl[i].InnerText);
                }

                // tcpTrigger/enabledActions
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledActions/windowsEventLog");
                if (xn != null) { EventLogOption.IsChecked = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledActions/emailNotification");
                if (xn != null) { SendEmailOption.IsChecked = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledActions/popupNotification");
                if (xn != null) { DisplayPopupOption.IsChecked = bool.Parse(xn.InnerText); }
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/enabledActions/executeCommand");
                if (xn != null) { LaunchAppOption.IsChecked = bool.Parse(xn.InnerText); }
                // tcpTrigger/actionSettings
                RateLimitMinutes.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/actionSettings/rateLimitMinutes")?.InnerText;
                if (RateLimitMinutes.Text.Length > 0)
                    RateLimitOption.IsChecked = true;
                ApplicationPath.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/actionSettings/command/path")?.InnerText;
                ApplicationArguments.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/actionSettings/command/arguments")?.InnerText;
                // tcpTrigger/emailConfiguration
                EmailServer.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/server")?.InnerText;
                EmailPort.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/port")?.InnerText;
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/isAuthRequired");
                if (xn != null) { IsSmtpAuthenticationRequired.IsChecked = bool.Parse(xn.InnerText); }
                // Decrypt username.
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/username");
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
                // Decrypt password.
                xn = xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/password");
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
                // Join recipient list to single string.
                nl = xd.DocumentElement.SelectNodes("/tcpTrigger/emailConfiguration/recipientList/address");
                List<string> recipients = new List<string>();
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        recipients.Add(nl[i].InnerText);
                }
                EmailRecipient.Text = string.Join(", ", recipients.ToArray());
                EmailSender.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/sender/address")?.InnerText;
                EmailSenderFriendly.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailConfiguration/sender/displayName")?.InnerText;
                // tcpTrigger/customMessage
                nl = xd.DocumentElement.SelectNodes("/tcpTrigger/customMessage");
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
                MessageBox.Show($"Error reading configuration '{configurationPath}'. {ex.Message}");
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
                MessageBox.Show("A folder for the tcpTrigger configuration file was not found.", "Error");
                return false;
            }

            // Use options specified in GUI to write to configuration file.
            try
            {
                // Convert port numbers to an ordered int array and remove duplicates.
                // Port numbers can be entered comma-separated and also specified using ranges with '-'.
                int[] portNumbers = (from part in TcpIncludePorts.Text.Split(',')
                                     let range = part.Split('-')
                                     let start = int.Parse(range[0])
                                     let end = int.Parse(range[range.Length - 1])
                                     from i in Enumerable.Range(start, end - start + 1)
                                     orderby i
                                     select i).Distinct().ToArray();
                TcpIncludePorts.Text = FormatTcpPortRange(portNumbers);
                portNumbers = (from part in TcpExcludePorts.Text.Split(',')
                               let range = part.Split('-')
                               let start = int.Parse(range[0])
                               let end = int.Parse(range[range.Length - 1])
                               from i in Enumerable.Range(start, end - start + 1)
                               orderby i
                               select i).Distinct().ToArray();
                TcpExcludePorts.Text = FormatTcpPortRange(portNumbers);

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
                    string[] dhcpServers = DhcpServers.Text.Split(',');
                    for (int i = 0; i < dhcpServers.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(dhcpServers[i]))
                        {
                            writer.WriteElementString("ipAddress", dhcpServers[i].Trim());
                        }
                    }
                    writer.WriteEndElement();

                    // Ignored endpoints.
                    writer.WriteStartElement("endpointIgnoreList");
                    // Split whitelist to string array (split by both commas and newlines) and then trim each item,
                    string[] ignoredEndpoints = Whitelist.Text.Trim().Split(new char[] { ',', '\n' }).Select(ip => ip.Trim()).ToArray();
                    // Sort IPs.
                    ignoredEndpoints = ignoredEndpoints.OrderBy(i => new Version(i.ToString())).ToArray();
                    for (int i = 0; i < ignoredEndpoints.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(ignoredEndpoints[i]))
                        {
                            writer.WriteElementString("ipAddress", ignoredEndpoints[i].Trim());
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
                    writer.WriteElementString("windowsEventLog", EventLogOption.IsChecked == true ? t : f);
                    writer.WriteElementString("emailNotification", SendEmailOption.IsChecked == true ? t : f);
                    writer.WriteElementString("popupNotification", DisplayPopupOption.IsChecked == true ? t : f);
                    writer.WriteElementString("executeCommand", LaunchAppOption.IsChecked == true ? t : f);
                    writer.WriteEndElement();

                    // Action settings.
                    writer.WriteStartElement("actionSettings");
                    writer.WriteElementString("rateLimitMinutes", RateLimitMinutes.Text);
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
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private bool ValidateUserSettings()
        {
            if (MonitorTcpOption.IsChecked == true && !IsTcpPortsValid())
            {
                MainTab.Focus();
                MessageBox.Show("Please enter a valid port number to monitor. Multiple port numbers should be separated with a comma. Ranges should be separated with a hyphen. Example: 21,23,2000-3000", "Error");
                return false;
            }


            if (LaunchAppOption.IsChecked == true)
            {
                if (ApplicationPath.Text.Length == 0)
                {
                    MainTab.Focus();
                    MessageBox.Show("An external application is required.", "Error");
                    return false;
                }
            }

            if (SendEmailOption.IsChecked == true)
            {
                if (EmailSubject.Text.Length == 0)
                {
                    MainTab.Focus();
                    MessageBox.Show("An email subject is required.", "Error");
                    return false;
                }

                if (EmailServer.Text.Length == 0)
                {
                    EmailTab.Focus();
                    MessageBox.Show("An outgoing mail server is required.", "Error");
                    return false;
                }

                if (EmailPort.Text.Length == 0
                    || !int.TryParse(EmailPort.Text, out int n)
                    || n <= 0
                    || n > 65535)
                {
                    EmailTab.Focus();
                    MessageBox.Show("Please enter a valid mail server port number.", "Error");
                    return false;
                }

                if (EmailRecipient.Text.Length == 0)
                {
                    EmailTab.Focus();
                    MessageBox.Show("An email recipient is required.", "Error");
                    return false;
                }

                if (EmailSender.Text.Length == 0)
                {
                    EmailTab.Focus();
                    MessageBox.Show("A sender email address is required.", "Error");
                    return false;
                }
            }

            if (RateLimitMinutes.Text.Length > 0)
            {
                if (!int.TryParse(RateLimitMinutes.Text, out int n) || n < 0)
                {
                    AdvancedTab.Focus();
                    MessageBox.Show("Please enter a valid number of minutes for rate limiting.", "Error");
                    return false;
                }
            }

            return true;
        }

        private bool IsTcpPortsValid()
        {
            try
            {
                int[] portNumbers = (from part in TcpIncludePorts.Text.Split(',')
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
                    $"Failed to restart tcpTrigger service. Configuration changes not applied. {ex.Message}";
                return;
            }
        }

        private void Thread_RestartServiceCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Result != null)
                MessageBox.Show((string)e.Result, "Error");
            else
                MessageBox.Show("Configuration file has been saved.", "Success");
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            try
            {
                if (dialog.ShowDialog() == true)
                {
                    ApplicationPath.Text = dialog.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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
                MessageBox.Show("Failed to send test email. " + (string)e.Result, "Error");
            else
                MessageBox.Show("Message sent.", "Email Test");

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
    }
}
