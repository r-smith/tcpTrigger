using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Security.Permissions;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

            //if (GetServiceStatus() == false)
            //{
            //    App.Current.Shutdown();
            //    return;
            //}
        }

        private bool GetServiceStatus()
        {
            //var sc = new ServiceController("tcpTrigger");
            //tbServiceStatus.Text = "tcpTrigger service is currently ";

            //try
            //{
            //    switch (sc.Status)
            //    {
            //        case ServiceControllerStatus.Running:
            //            tbServiceStatus.Text += "running";
            //            break;
            //        case ServiceControllerStatus.Stopped:
            //            tbServiceStatus.Text += "stopped";
            //            break;
            //        case ServiceControllerStatus.Paused:
            //            tbServiceStatus.Text += "paused";
            //            break;
            //        case ServiceControllerStatus.StopPending:
            //            tbServiceStatus.Text += "stopping";
            //            break;
            //        case ServiceControllerStatus.StartPending:
            //            tbServiceStatus.Text += "starting";
            //            break;
            //        default:
            //            tbServiceStatus.Text += "changing status";
            //            break;
            //    }
            //}

            //catch
            //{
            //    MessageBox.Show("tcpTrigger service could not be found.");
            //    return false;
            //}

            return true;
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
                txtApplication.Text = config.AppSettings.Settings["Action.ApplicationPath"].Value;
                txtArguments.Text = config.AppSettings.Settings["Action.ApplicationArguments"].Value;
                txtMailServer.Text = config.AppSettings.Settings["Email.Server"].Value;
                txtMailPort.Text = config.AppSettings.Settings["Email.ServerPort"].Value;
                txtMailRecipient.Text = config.AppSettings.Settings["Email.RecipientAddress"].Value;
                txtMailSender.Text = config.AppSettings.Settings["Email.SenderAddress"].Value;
                txtMailSenderDisplay.Text = config.AppSettings.Settings["Email.SenderDisplayName"].Value;
                txtMailSubject.Text = config.AppSettings.Settings["Email.Subject"].Value;

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
            if (dialog.ShowDialog() == true)
            {
                txtApplication.Text = dialog.FileName;
            }
        }

        private bool ValidateConfiguration()
        {
            int n;
            bool isNumber;
            
            if (chkMonitorTcpPort.IsChecked == true && !IsTcpPortsValid())
            {
                this.ShowMessageAsync("Error", "Please enter a valid port number to monitor. Multiple port numbers should be separated with a comma.");
                tabMain.Focus();
                return false;
            }
            

            if (chkLaunchApplication.IsChecked == true)
            {
                if (txtApplication.Text.Length == 0)
                {
                    this.ShowMessageAsync("Error", "An external application is required.");
                    tabMain.Focus();
                    return false;
                }
            }

            if (chkNotificationEmail.IsChecked == true)
            {
                if (txtMailSubject.Text.Length == 0)
                {
                    this.ShowMessageAsync("Error", "An email subject is required.");
                    tabMain.Focus();
                    return false;
                }

                if (txtMailServer.Text.Length == 0)
                {
                    this.ShowMessageAsync("Error", "An outgoing mail server is required.");
                    tabEmail.Focus();
                    return false;
                }

                isNumber = int.TryParse(txtMailPort.Text, out n);
                if (txtMailPort.Text.Length == 0 || !isNumber || n <= 0 || n > 65536)
                {
                    this.ShowMessageAsync("Error", "Please enter a valid mail server port number.");
                    tabEmail.Focus();
                    return false;
                }

                if (txtMailRecipient.Text.Length == 0)
                {
                    this.ShowMessageAsync("Error", "An email recipient is required.");
                    tabEmail.Focus();
                    return false;
                }

                if (txtMailSender.Text.Length == 0)
                {
                    this.ShowMessageAsync("Error", "A sender email address is required.");
                    tabEmail.Focus();
                    return false;
                }
            }

            if (txtMailRateLimitMinutes.Text.Length > 0)
            {
                isNumber = int.TryParse(txtMailRateLimitMinutes.Text, out n);
                if (!isNumber || n < 0)
                {
                    this.ShowMessageAsync("Error", "Please enter a valid number of minutes for rate limiting.");
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
                txtListenPort.Text.Split(',').
                    Select(x => int.Parse(x)).OrderBy(x => x).ToArray();
            }
            catch
            {
                return false;
            }

            return true;
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
                this.ShowMessageAsync(
                    "Error",
                    $"Could not retrieve registry information for the tcpTrigger service. {ex.Message}");
                return;
            }

            installPath = installPath.Trim('"');

            if (installPath.Length == 0)
            {
                this.ShowMessageAsync(
                    "Error",
                    "Could not retrieve registry information for the tcpTrigger service.");
                return;
            }

            var config = ConfigurationManager.OpenExeConfiguration(installPath);

            try
            {
                var configuration = ConfigurationManager.OpenExeConfiguration(installPath);

                // Convert to ordered int array.
                var portsAsOrderedInts = txtListenPort.Text.Split(',').
                    Select(x => int.Parse(x)).OrderBy(x => x).ToArray();
                var portsAsJoinedString = string.Join(",", Array.ConvertAll(portsAsOrderedInts, x => x.ToString()));
                txtListenPort.Text = portsAsJoinedString;
                config.AppSettings.Settings["Trigger.TcpPortsToListenOn"].Value = portsAsJoinedString;
                    
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

                config.Save();
            }
            catch (Exception ex)
            {
                this.ShowMessageAsync(
                    "Error",
                    $"Error updating service configuration file. {ex.Message}");
                return;
            }

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
                this.ShowMessageAsync(
                    "Error",
                    $"Error updating the tcpTrigger service. {ex.Message}");
                return;
            }


            this.ShowMessageAsync("Success", "Configuration file has been saved.");
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
