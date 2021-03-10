using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Windows;

namespace tcpTrigger.Editor
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            if (LoadConfigurationFile() == false)
            {
                App.Current.Shutdown();
                return;
            }
        }


        private bool LoadConfigurationFile()
        {
            string installPath = string.Empty;
            bool didTaskSucceed = true;

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\services\tcpTrigger"))
                {
                    if (key != null)
                    {
                        Object o = key.GetValue("ImagePath");
                        if (o != null)
                        {
                            installPath = o as string;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not retrieve registry information for the tcpTrigger service. {ex.Message}",
                    "tcpTrigger - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            installPath = installPath.Trim('"');

            if (installPath.Length == 0)
            {
                MessageBox.Show(
                    "Could not retrieve registry information for the tcpTrigger service.",
                    "tcpTrigger - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            var config = ConfigurationManager.OpenExeConfiguration(installPath);

            try
            {
                var configuration = ConfigurationManager.OpenExeConfiguration(installPath);

                txtListenPort.Text = config.AppSettings.Settings["Trigger.TcpPortsToListenOn"].Value;
                txtKnownDhcpServers.Text = config.AppSettings.Settings["Dhcp.SafeServerList"].Value;
                txtApplication.Text = config.AppSettings.Settings["Action.ApplicationPath"].Value;
                txtArguments.Text = config.AppSettings.Settings["Action.ApplicationArguments"].Value;
                txtMailServer.Text = config.AppSettings.Settings["Email.Server"].Value;
                txtMailPort.Text = config.AppSettings.Settings["Email.ServerPort"].Value;
                txtMailRecipient.Text = config.AppSettings.Settings["Email.RecipientAddress"].Value;
                txtMailSender.Text = config.AppSettings.Settings["Email.SenderAddress"].Value;
                txtMailSenderDisplay.Text = config.AppSettings.Settings["Email.SenderDisplayName"].Value;
                txtMailSubject.Text = config.AppSettings.Settings["Email.Subject"].Value;
                txtMessageBodyPing.Text = config.AppSettings.Settings["MessageBody.Ping"].Value;
                txtMessageBodyTcpConnect.Text = config.AppSettings.Settings["MessageBody.TcpConnect"].Value;
                txtMessageBodyNamePoison.Text = config.AppSettings.Settings["MessageBody.NamePoison"].Value;
                txtMessageBodyRogueDhcp.Text = config.AppSettings.Settings["MessageBody.RogueDhcp"].Value;

                txtMailRateLimitMinutes.Text = config.AppSettings.Settings["Action.RateLimitMinutes"].Value;
                if (txtMailRateLimitMinutes.Text.Length == 0 || txtMailRateLimitMinutes.Text == "0")
                    chkTriggerRateLimit.IsChecked = false;
                else
                    chkTriggerRateLimit.IsChecked = true;

                bool checkedValue;
                bool.TryParse(config.AppSettings.Settings["Trigger.EnableMonitorTcpPort"].Value, out checkedValue);
                if (checkedValue == true)
                    chkMonitorTcpPort.IsChecked = true;
                else
                    chkMonitorTcpPort.IsChecked = false;

                bool.TryParse(config.AppSettings.Settings["Trigger.EnableMonitorIcmpPing"].Value, out checkedValue);
                if (checkedValue == true)
                    chkMonitorIcmp.IsChecked = true;
                else
                    chkMonitorIcmp.IsChecked = false;

                bool.TryParse(config.AppSettings.Settings["Trigger.EnableNamePoisonDetection"].Value, out checkedValue);
                if (checkedValue == true)
                    chkNamePoisonDetection.IsChecked = true;
                else
                    chkNamePoisonDetection.IsChecked = false;

                bool.TryParse(config.AppSettings.Settings["Trigger.EnableRogueDhcpDetection"].Value, out checkedValue);
                if (checkedValue == true)
                    chkRogueDhcpServerDetection.IsChecked = true;
                else
                    chkRogueDhcpServerDetection.IsChecked = false;

                bool.TryParse(config.AppSettings.Settings["Action.EnableEventLog"].Value, out checkedValue);
                if (checkedValue == true)
                    chkEventLog.IsChecked = true;
                else
                    chkEventLog.IsChecked = false;

                bool.TryParse(config.AppSettings.Settings["Action.EnableRunApplication"].Value, out checkedValue);
                if (checkedValue == true)
                    chkLaunchApplication.IsChecked = true;
                else
                    chkLaunchApplication.IsChecked = false;

                bool.TryParse(config.AppSettings.Settings["Action.EnableEmailNotification"].Value, out checkedValue);
                if (checkedValue == true)
                    chkNotificationEmail.IsChecked = true;
                else
                    chkNotificationEmail.IsChecked = false;

                bool.TryParse(config.AppSettings.Settings["Action.EnablePopupMessage"].Value, out checkedValue);
                if (checkedValue == true)
                    chkDisplayPopupMessage.IsChecked = true;
                else
                    chkDisplayPopupMessage.IsChecked = false;
            }

            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error reading service configuration file. {ex.Message}",
                    "tcpTrigger - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }

            return didTaskSucceed;
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            try
            {
                if (dialog.ShowDialog() == true)
                {
                    txtApplication.Text = dialog.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to browse filesystem. {ex.Message}",
                    "tcpTrigger - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool ValidateConfiguration()
        {
            int n;
            bool isNumber;

            if (chkMonitorTcpPort.IsChecked == true && !IsTcpPortsValid())
            {
                MessageBox.Show("Please enter a valid port number to monitor. Multiple port numbers should be separated with a comma. Ranges should be separated with a hyphen. Example: 21,23,2000-3000", "Error");
                tabMain.Focus();
                return false;
            }


            if (chkLaunchApplication.IsChecked == true)
            {
                if (txtApplication.Text.Length == 0)
                {
                    MessageBox.Show("An external application is required.", "Error");
                    tabMain.Focus();
                    return false;
                }
            }

            if (chkNotificationEmail.IsChecked == true)
            {
                if (txtMailSubject.Text.Length == 0)
                {
                    MessageBox.Show("An email subject is required.", "Error");
                    tabMain.Focus();
                    return false;
                }

                if (txtMailServer.Text.Length == 0)
                {
                    MessageBox.Show("An outgoing mail server is required.", "Error");
                    tabEmail.Focus();
                    return false;
                }

                isNumber = int.TryParse(txtMailPort.Text, out n);
                if (txtMailPort.Text.Length == 0 || !isNumber || n <= 0 || n > 65536)
                {
                    MessageBox.Show("Please enter a valid mail server port number.", "Error");
                    tabEmail.Focus();
                    return false;
                }

                if (txtMailRecipient.Text.Length == 0)
                {
                    MessageBox.Show("An email recipient is required.", "Error");
                    tabEmail.Focus();
                    return false;
                }

                if (txtMailSender.Text.Length == 0)
                {
                    MessageBox.Show("A sender email address is required.", "Error");
                    tabEmail.Focus();
                    return false;
                }
            }

            if (txtMailRateLimitMinutes.Text.Length > 0)
            {
                isNumber = int.TryParse(txtMailRateLimitMinutes.Text, out n);
                if (!isNumber || n < 0)
                {
                    MessageBox.Show("Please enter a valid number of minutes for rate limiting.", "Error");
                    tabAdvanced.Focus();
                    return false;
                }
            }

            return true;
        }

        private bool IsTcpPortsValid()
        {
            try
            {
                var portNumbers = (from part in txtListenPort.Text.Split(',')
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

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateConfiguration() == false)
                return;
            
            string installPath = string.Empty;

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\services\tcpTrigger"))
                {
                    if (key != null)
                    {
                        Object o = key.GetValue("ImagePath");
                        if (o != null)
                        {
                            installPath = o as string;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not retrieve registry information for the tcpTrigger service. {ex.Message}", "Error");
                return;
            }

            installPath = installPath.Trim('"');

            if (installPath.Length == 0)
            {
                MessageBox.Show("Could not retrieve registry information for the tcpTrigger service.", "Error");
                return;
            }

            var config = ConfigurationManager.OpenExeConfiguration(installPath);

            try
            {
                var configuration = ConfigurationManager.OpenExeConfiguration(installPath);

                // Convert to an ordered int array and remove duplicates.
                var portNumbers = (from part in txtListenPort.Text.Split(',')
                                   let range = part.Split('-')
                                   let start = int.Parse(range[0])
                                   let end = int.Parse(range[range.Length - 1])
                                   from i in Enumerable.Range(start, end - start + 1)
                                   orderby i
                                   select i).Distinct().ToArray();
                txtListenPort.Text = FormatTcpPortRange(portNumbers);
                config.AppSettings.Settings["Trigger.TcpPortsToListenOn"].Value = txtListenPort.Text;

                if (chkMonitorTcpPort.IsChecked.Value == true)
                    config.AppSettings.Settings["Trigger.EnableMonitorTcpPort"].Value = "true";
                else
                    config.AppSettings.Settings["Trigger.EnableMonitorTcpPort"].Value = "false";

                if (chkMonitorIcmp.IsChecked.Value == true)
                    config.AppSettings.Settings["Trigger.EnableMonitorIcmpPing"].Value = "true";
                else
                    config.AppSettings.Settings["Trigger.EnableMonitorIcmpPing"].Value = "false";

                if (chkNamePoisonDetection.IsChecked.Value == true)
                    config.AppSettings.Settings["Trigger.EnableNamePoisonDetection"].Value = "true";
                else
                    config.AppSettings.Settings["Trigger.EnableNamePoisonDetection"].Value = "false";

                if (chkRogueDhcpServerDetection.IsChecked.Value == true)
                {
                    config.AppSettings.Settings["Trigger.EnableRogueDhcpDetection"].Value = "true";
                    config.AppSettings.Settings["Dhcp.SafeServerList"].Value = txtKnownDhcpServers.Text;
                }
                else
                    config.AppSettings.Settings["Trigger.EnableRogueDhcpDetection"].Value = "false";

                if (chkEventLog.IsChecked == true)
                    config.AppSettings.Settings["Action.EnableEventLog"].Value = "true";
                else
                    config.AppSettings.Settings["Action.EnableEventLog"].Value = "false";

                if (chkLaunchApplication.IsChecked == true)
                {
                    config.AppSettings.Settings["Action.EnableRunApplication"].Value = "true";
                    config.AppSettings.Settings["Action.ApplicationPath"].Value = txtApplication.Text;
                    config.AppSettings.Settings["Action.ApplicationArguments"].Value = txtArguments.Text;
                }
                else
                {
                    config.AppSettings.Settings["Action.EnableRunApplication"].Value = "false";
                }

                if (chkNotificationEmail.IsChecked == true)
                {
                    config.AppSettings.Settings["Action.EnableEmailNotification"].Value = "true";
                    config.AppSettings.Settings["Email.Server"].Value = txtMailServer.Text;
                    config.AppSettings.Settings["Email.ServerPort"].Value = txtMailPort.Text;
                    config.AppSettings.Settings["Email.RecipientAddress"].Value = txtMailRecipient.Text;
                    config.AppSettings.Settings["Email.SenderAddress"].Value = txtMailSender.Text;
                    config.AppSettings.Settings["Email.SenderDisplayName"].Value = txtMailSenderDisplay.Text;
                    config.AppSettings.Settings["Email.Subject"].Value = txtMailSubject.Text;
                }
                else
                {
                    config.AppSettings.Settings["Action.EnableEmailNotification"].Value = "false";
                }

                if (chkDisplayPopupMessage.IsChecked == true)
                {
                    config.AppSettings.Settings["Action.EnablePopupMessage"].Value = "true";
                }
                else
                {
                    config.AppSettings.Settings["Action.EnablePopupMessage"].Value = "false";
                }

                if (chkTriggerRateLimit.IsChecked == true)
                {
                    config.AppSettings.Settings["Action.RateLimitMinutes"].Value = txtMailRateLimitMinutes.Text;
                }
                else
                {
                    config.AppSettings.Settings["Action.RateLimitMinutes"].Value = "0";
                }

                config.AppSettings.Settings["MessageBody.Ping"].Value = txtMessageBodyPing.Text;
                config.AppSettings.Settings["MessageBody.TcpConnect"].Value = txtMessageBodyTcpConnect.Text;
                config.AppSettings.Settings["MessageBody.NamePoison"].Value = txtMessageBodyNamePoison.Text;
                config.AppSettings.Settings["MessageBody.RogueDhcp"].Value = txtMessageBodyRogueDhcp.Text;

                config.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating service configuration file. {ex.Message}", "Error");
                return;
            }

            RestartTcpTriggerService();
        }

        private void RestartTcpTriggerService()
        {
            // Setup a background thread to restart the service.
            gridLoading.Visibility = Visibility.Visible;
            var bw = new BackgroundWorker();
            bw.DoWork += new DoWorkEventHandler(bgThread_RestartService);
            bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bgThread_RestartServiceCompleted);
            bw.RunWorkerAsync();
        }

        private void bgThread_RestartService(object sender, DoWorkEventArgs e)
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


        private void bgThread_RestartServiceCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            gridLoading.Visibility = Visibility.Collapsed;

            if (e.Result != null)
                MessageBox.Show((string)e.Result, "Error");
            else
                MessageBox.Show("Configuration file has been saved.", "Success");
        }


        private void btnHelp_Click(object sender, RoutedEventArgs e)
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            tabEmail.Focus();
        }
    }
}
