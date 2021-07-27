using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Windows;
using System.Xml;

namespace tcpTrigger.Editor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            LoadConfiguration();
        }

        private string GetInstallPath()
        {
            string installPath = string.Empty;

            // Get the install path for the tcpTrigger service from the Window registry.
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\services\tcpTrigger"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("ImagePath");
                        if (value != null)
                        {
                            installPath = Path.GetDirectoryName((value as string).Trim('"'));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not retrieve registry information for the tcpTrigger service. {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return installPath;
        }

        private void LoadConfiguration()
        {
            string configurationPath = GetInstallPath();

            if (configurationPath.Length == 0 || !Directory.Exists(configurationPath))
            {
                MessageBox.Show("The configuration path for the tcpTrigger service was not found.", "Error");
                return;
            }

            configurationPath += @"\tcpTrigger.xml";

            try
            {
                var xd = new XmlDocument();
                xd.Load(configurationPath);

                XmlNode xn;
                XmlNodeList nl;
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
                // tcpTrigger/dhcpServerIgnoreList
                nl = xd.DocumentElement.SelectNodes("/tcpTrigger/dhcpServerIgnoreList/ipAddress");
                List<string> ignoredDhcpServers = new List<string>();
                for (int i = 0; i < nl.Count; i++)
                {
                    if (!string.IsNullOrEmpty(nl[i].InnerText))
                        ignoredDhcpServers.Add(nl[i].InnerText);
                }
                DhcpServers.Text = string.Join(", ", ignoredDhcpServers.ToArray());

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
                // tcpTrigger/emailSettings
                EmailServer.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailSettings/server")?.InnerText;
                EmailPort.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailSettings/port")?.InnerText;
                EmailRecipient.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailSettings/recipientList/address")?.InnerText;
                EmailSender.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailSettings/sender/address")?.InnerText;
                EmailSenderFriendly.Text =
                    xd.DocumentElement.SelectSingleNode("/tcpTrigger/emailSettings/sender/displayName")?.InnerText;
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
            string configurationPath = GetInstallPath();
            const string t = "true";
            const string f = "false";

            // Ensure configuration path exists.
            if (configurationPath.Length == 0 || !Directory.Exists(configurationPath))
            {
                MessageBox.Show("The configuration path for the tcpTrigger service was not found.", "Error");
                return false;
            }

            // Use options specified in GUI to write to configuration file.
            configurationPath += @"\tcpTrigger.xml";
            try
            {
                // Convert port numbers to an ordered int array and remove duplicates.
                // Port numbers can be entered comma-separated and also specified using ranges with '-'.
                var portNumbers = (from part in TcpIncludePorts.Text.Split(',')
                                   let range = part.Split('-')
                                   let start = int.Parse(range[0])
                                   let end = int.Parse(range[range.Length - 1])
                                   from i in Enumerable.Range(start, end - start + 1)
                                   orderby i
                                   select i).Distinct().ToArray();
                TcpIncludePorts.Text = FormatTcpPortRange(portNumbers);

                using (XmlWriter writer = XmlWriter.Create(configurationPath, new XmlWriterSettings() { Indent = true }))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("tcpTrigger");

                    writer.WriteStartElement("enabledComponents");
                    writer.WriteElementString("tcp", MonitorTcpOption.IsChecked == true ? t : f);
                    writer.WriteElementString("icmp", MonitorIcmpOption.IsChecked == true ? t : f);
                    writer.WriteElementString("namePoison", MonitorPoisonOption.IsChecked == true ? t : f);
                    writer.WriteElementString("rogueDhcp", MonitorDhcpOption.IsChecked == true ? t : f);
                    writer.WriteEndElement();

                    writer.WriteStartElement("monitoredPorts");
                    writer.WriteStartElement("tcp");
                    writer.WriteElementString("include", TcpIncludePorts.Text);
                    writer.WriteElementString("exclude", string.Empty);
                    writer.WriteEndElement();
                    writer.WriteEndElement();

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

                    writer.WriteStartElement("enabledActions");
                    writer.WriteElementString("windowsEventLog", EventLogOption.IsChecked == true ? t : f);
                    writer.WriteElementString("emailNotification", SendEmailOption.IsChecked == true ? t : f);
                    writer.WriteElementString("popupNotification", DisplayPopupOption.IsChecked == true ? t : f);
                    writer.WriteElementString("executeCommand", LaunchAppOption.IsChecked == true ? t : f);
                    writer.WriteEndElement();

                    writer.WriteStartElement("actionSettings");
                    writer.WriteElementString("rateLimitMinutes", RateLimitMinutes.Text);
                    writer.WriteStartElement("command");
                    writer.WriteElementString("path",
                        (LaunchAppOption.IsChecked == true)
                        ? ApplicationPath.Text
                        : string.Empty);
                    writer.WriteElementString("arguments",
                        (LaunchAppOption.IsChecked == true)
                        ? ApplicationArguments.Text
                        : string.Empty);
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    writer.WriteStartElement("emailSettings");
                    writer.WriteElementString("server",
                        (SendEmailOption.IsChecked == true)
                        ? EmailServer.Text
                        : string.Empty);
                    writer.WriteElementString("port",
                        (SendEmailOption.IsChecked == true)
                        ? EmailPort.Text
                        : string.Empty);
                    writer.WriteStartElement("recipientList");
                    writer.WriteElementString("address",
                        (SendEmailOption.IsChecked == true)
                        ? EmailRecipient.Text
                        : string.Empty);
                    writer.WriteEndElement();
                    writer.WriteStartElement("sender");
                    writer.WriteElementString("address",
                        (SendEmailOption.IsChecked == true)
                        ? EmailSender.Text
                        : string.Empty);
                    writer.WriteElementString("displayName",
                        (SendEmailOption.IsChecked == true)
                        ? EmailSenderFriendly.Text
                        : string.Empty);
                    writer.WriteEndElement();
                    writer.WriteEndElement();

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
                    || !(int.TryParse(EmailPort.Text, out int n))
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
                if (!(int.TryParse(RateLimitMinutes.Text, out int n)) || n < 0)
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
            //gridLoading.Visibility = Visibility.Visible;
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
            //gridLoading.Visibility = Visibility.Collapsed;

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
    }
}
