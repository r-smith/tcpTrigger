using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Configuration;
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

            if (LoadConfiguration() == false)
            {
                App.Current.Shutdown();
                return;
            }
        }

        private bool LoadConfiguration()
        {
            var installPath = string.Empty;
            var didTaskSucceed = true;

            // Get service install path from registry.
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\services\tcpTrigger"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("ImagePath");
                        if (value != null)
                        {
                            installPath = (value as string).Trim('"');
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
                return false;
            }

            // Ensure registry value for install path contains something.
            if (installPath.Length == 0)
            {
                MessageBox.Show(
                    "Could not retrieve registry information for the tcpTrigger service.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            // Open configuration file.
            var config = ConfigurationManager.OpenExeConfiguration(installPath);

            // Use file to populate fields and options in GUI.
            try
            {
                var configuration = ConfigurationManager.OpenExeConfiguration(installPath);

                TcpIncludePorts.Text = config.AppSettings.Settings["Trigger.TcpPortsToListenOn"].Value;
                DhcpServers.Text = config.AppSettings.Settings["Dhcp.SafeServerList"].Value;
                ApplicationPath.Text = config.AppSettings.Settings["Action.ApplicationPath"].Value;
                ApplicationArguments.Text = config.AppSettings.Settings["Action.ApplicationArguments"].Value;
                EmailServer.Text = config.AppSettings.Settings["Email.Server"].Value;
                EmailPort.Text = config.AppSettings.Settings["Email.ServerPort"].Value;
                EmailRecipient.Text = config.AppSettings.Settings["Email.RecipientAddress"].Value;
                EmailSender.Text = config.AppSettings.Settings["Email.SenderAddress"].Value;
                EmailSenderFriendly.Text = config.AppSettings.Settings["Email.SenderDisplayName"].Value;
                EmailSubject.Text = config.AppSettings.Settings["Email.Subject"].Value;
                IcmpMessageBody.Text = config.AppSettings.Settings["MessageBody.Ping"].Value;
                TcpMessageBody.Text = config.AppSettings.Settings["MessageBody.TcpConnect"].Value;
                NamePoisonMessageBody.Text = config.AppSettings.Settings["MessageBody.NamePoison"].Value;
                RogueDhcpMessageBody.Text = config.AppSettings.Settings["MessageBody.RogueDhcp"].Value;

                RateLimitMinutes.Text = config.AppSettings.Settings["Action.RateLimitMinutes"].Value;
                RateLimitOption.IsChecked = RateLimitMinutes.Text.Length != 0 && RateLimitMinutes.Text != "0";

                bool.TryParse(config.AppSettings.Settings["Trigger.EnableMonitorTcpPort"].Value, out bool checkedValue);
                MonitorTcpOption.IsChecked = checkedValue;

                bool.TryParse(config.AppSettings.Settings["Trigger.EnableMonitorIcmpPing"].Value, out checkedValue);
                MonitorIcmpOption.IsChecked = checkedValue;

                bool.TryParse(config.AppSettings.Settings["Trigger.EnableNamePoisonDetection"].Value, out checkedValue);
                MonitorPoisonOption.IsChecked = checkedValue;

                bool.TryParse(config.AppSettings.Settings["Trigger.EnableRogueDhcpDetection"].Value, out checkedValue);
                MonitorDhcpOption.IsChecked = checkedValue;

                bool.TryParse(config.AppSettings.Settings["Action.EnableEventLog"].Value, out checkedValue);
                EventLogOption.IsChecked = checkedValue;

                bool.TryParse(config.AppSettings.Settings["Action.EnableRunApplication"].Value, out checkedValue);
                LaunchAppOption.IsChecked = checkedValue;

                bool.TryParse(config.AppSettings.Settings["Action.EnableEmailNotification"].Value, out checkedValue);
                SendEmailOption.IsChecked = checkedValue;

                bool.TryParse(config.AppSettings.Settings["Action.EnablePopupMessage"].Value, out checkedValue);
                DisplayPopupOption.IsChecked = checkedValue;
            }

            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to read configuration file. {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            return didTaskSucceed;
        }

        private bool WriteConfiguration()
        {
            var filePath = string.Empty;

            // Get service install path from registry.
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\services\tcpTrigger"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("ImagePath");
                        if (value != null)
                            filePath = Path.GetDirectoryName((value as string).Trim('"'));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not retrieve registry information for the tcpTrigger service. {ex.Message}", "Error");
                return false;
            }

            // Ensure path exists.
            if (filePath.Length == 0)
            {
                MessageBox.Show("Could not retrieve registry information for the tcpTrigger service.", "Error");
                return false;
            }
            if (!Directory.Exists(filePath))
            {
                MessageBox.Show("Could not find service directory.", "Error");
                return false;
            }

            // Use options specified in GUI to write to configuration file.
            filePath += @"\tcpTrigger.xml";
            try
            {
                // Convert port numbers to an ordered int array and remove duplicates.
                var portNumbers = (from part in TcpIncludePorts.Text.Split(',')
                                   let range = part.Split('-')
                                   let start = int.Parse(range[0])
                                   let end = int.Parse(range[range.Length - 1])
                                   from i in Enumerable.Range(start, end - start + 1)
                                   orderby i
                                   select i).Distinct().ToArray();
                TcpIncludePorts.Text = FormatTcpPortRange(portNumbers);

                using (XmlWriter writer = XmlWriter.Create(filePath, new XmlWriterSettings() { Indent = true }))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("tcpTrigger");

                    writer.WriteStartElement("enabledComponents");
                    writer.WriteElementString("tcp", MonitorTcpOption.IsChecked.ToString());
                    writer.WriteElementString("icmp", MonitorIcmpOption.IsChecked.ToString());
                    writer.WriteElementString("namePoison", MonitorPoisonOption.IsChecked.ToString());
                    writer.WriteElementString("rogueDhcp", MonitorDhcpOption.IsChecked.ToString());
                    writer.WriteEndElement();

                    writer.WriteStartElement("monitoredPorts");
                    writer.WriteStartElement("tcp");
                    writer.WriteElementString("include", TcpIncludePorts.Text);
                    writer.WriteElementString("exclude", string.Empty);
                    writer.WriteEndElement();
                    writer.WriteEndElement();

                    writer.WriteStartElement("rogueDhcpExclude");
                    writer.WriteElementString("ipAddress", DhcpServers.Text);
                    writer.WriteEndElement();


                    writer.WriteStartElement("enabledActions");
                    writer.WriteElementString("windowsEventLog", EventLogOption.IsChecked.ToString());
                    writer.WriteElementString("emailNotification", SendEmailOption.IsChecked.ToString());
                    writer.WriteElementString("popupNotification", DisplayPopupOption.IsChecked.ToString());
                    writer.WriteElementString("executeCommand", LaunchAppOption.IsChecked.ToString());
                    writer.WriteEndElement();

                    writer.WriteStartElement("actionSettings");
                    writer.WriteElementString("rateLimitSeconds", RateLimitMinutes.Text);
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
                var portNumbers = (from part in TcpIncludePorts.Text.Split(',')
                                   let range = part.Split('-')
                                   let start = int.Parse(range[0])
                                   let end = int.Parse(range[range.Length - 1])
                                   from i in Enumerable.Range(start, end - start + 1)
                                   orderby i
                                   select i).Distinct().ToArray();
                string result = string.Join(",", portNumbers);

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
