using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;

namespace tcpTrigger.Editor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private HashSet<string> ExcludedNetworkInterfaces = new HashSet<string>();
        private List<TcpTriggerInterface> AllNetworkInterfaces;
        private string _tcpInclude = string.Empty;
        private string _udpInclude = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            ReadSettings();
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

        private string GetSettingsPath()
        {
            // Locate the tcpTrigger.xml configuration file.
            // First check the current directory. If not found, use ProgramData, regardless if the file exists or not.
            const string fileName = "tcpTrigger.xml";
            if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + fileName))
                return AppDomain.CurrentDomain.BaseDirectory + fileName;
            else
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\" + fileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateSettings() == false)
                return;

            if (WriteSettings() == false)
                return;

            ApplySettingsWindow applyWindow = new ApplySettingsWindow();
            applyWindow.Owner = this;
            Opacity = 0.65;
            Effect = new BlurEffect();
            if (applyWindow.ShowDialog() == true)
            {
                RestartServiceWindow restartWindow = new RestartServiceWindow();
                restartWindow.Owner = this;
                _ = restartWindow.ShowDialog();
            }
            Effect = null;
            Opacity = 1;
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
                        LogPath.Text = dialog.SelectedPath + @"\tcpTrigger.log";
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
            string defaultLogPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\tcpTrigger.log";
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
            emailTester.RunWorkerAsync(new EmailSettings()
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
                EmailSettings parameters = e.Argument as EmailSettings;
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
            TcpAllPortsOption.IsChecked = true;
            LogOption.IsChecked = true;
            EventLogOption.IsChecked = true;
            LogPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\tcpTrigger.log";
            RateLimitOption.IsChecked = true;
            RateLimitSeconds.Text = "180";
            EmailSubject.Text = "Alert: Suspicious network activity";
            EmailBody.Text = "Network connections to {Interface_IP} are being monitored by tcpTrigger. The following activity was detected:"
                + Environment.NewLine + Environment.NewLine
                + "{Connection_Log}";
        }

        private void TcpAllPortsOption_Checked(object sender, RoutedEventArgs e)
        {
            TcpIncludePorts.Text = "[ALL]";
            TcpIncludePorts.IsEnabled = false;
        }

        private void TcpAllPortsOption_Unchecked(object sender, RoutedEventArgs e)
        {
            TcpIncludePorts.Text = _tcpInclude;
            TcpIncludePorts.IsEnabled = true;
            TcpIncludePorts.Focus();
        }

        private void UdpAllPortsOption_Checked(object sender, RoutedEventArgs e)
        {
            UdpIncludePorts.Text = "[ALL]";
            UdpIncludePorts.IsEnabled = false;
        }

        private void UdpAllPortsOption_Unchecked(object sender, RoutedEventArgs e)
        {
            UdpIncludePorts.Text = _udpInclude;
            UdpIncludePorts.IsEnabled = true;
            UdpIncludePorts.Focus();
        }
    }
}
