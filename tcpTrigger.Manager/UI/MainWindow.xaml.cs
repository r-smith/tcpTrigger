using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Effects;

namespace tcpTrigger.Manager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly HashSet<string> ExcludedNetworkInterfaces = new HashSet<string>();
        private readonly List<TcpTriggerInterface> AllNetworkInterfaces;
        private readonly ObservableCollection<WhitelistItem> WhitelistItems = new ObservableCollection<WhitelistItem>();
        private string _tcpInclude = string.Empty;
        private string _udpInclude = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            ReadSettings();
            AllNetworkInterfaces = GetNetworkInterfaces();
            AllNetworkInterfaces.Sort((x, y) => x.Description.CompareTo(y.Description));
            Devices.ItemsSource = AllNetworkInterfaces;

            Whitelist.ItemsSource = WhitelistItems;
            if (WhitelistItems.Count < 1)
                WhitelistItems.Add(new WhitelistItem());
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

        private async void TestEmail_Click(object sender, RoutedEventArgs e)
        {
            TestEmail.IsEnabled = false;
            TestEmail.Content = "Testing...";

            try
            {
                using (MailMessage message = new MailMessage())
                {
                    message.Subject = "tcpTrigger Test Email";
                    message.Body = $"This is a test email notification sent by tcpTrigger on {DateTime.Now.ToLongDateString()} at {DateTime.Now.ToLongTimeString()}.";
                    message.From = new MailAddress(EmailSender.Text, EmailSenderFriendly.Text);
                    // Check if multiple recipients were provided.
                    if (EmailRecipient.Text.Contains(","))
                    {
                        // Multiple recipients. Split and add each to mail message.
                        string[] recips = EmailRecipient.Text.Split(',');
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
                        message.To.Add(EmailRecipient.Text);
                    }

                    using (SmtpClient smtpClient = new SmtpClient())
                    {
                        smtpClient.Host = EmailServer.Text;
                        smtpClient.Port = Int32.Parse(EmailPort.Text);
                        smtpClient.EnableSsl = IsTlsEnabled.IsChecked == true;
                        if (IsSmtpAuthenticationRequired.IsChecked == true)
                        {
                            smtpClient.Credentials = new NetworkCredential(SmtpUsername.Text, SmtpPassword.SecurePassword);
                        }
                        await smtpClient.SendMailAsync(message);
                    }
                }

                ShowMessageBox(
                    message: "A test email was sent.",
                    title: "Email test",
                    type: DialogWindow.Type.Info);
            }

            catch (Exception ex)
            {
                ShowMessageBox(
                    message: ex.Message,
                    title: "Failed to send test email",
                    type: DialogWindow.Type.Error);
            }

            TestEmail.IsEnabled = true;
            TestEmail.Content = "Test";
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
            BufferOption.IsChecked = true;
            BufferSeconds.Text = "15";
            EmailSubject.Text = "Alert: Suspicious network activity";
            EmailBody.Text = "Network connections to {INTERFACE_IP} are being monitored. The following activity was detected:"
                + Environment.NewLine + Environment.NewLine
                + "{CONNECTION_LOG}";
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

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F1)
            {
                ShowHelp();
            }
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            ShowHelp();
        }

        private void ShowHelp(string sectionName = null)
        {
            if (HelpWindow._OpenWindow == null)
            {
                new HelpWindow().Show();
            }
            else
            {
                if (HelpWindow._OpenWindow.WindowState == WindowState.Minimized)
                {
                    HelpWindow._OpenWindow.WindowState = WindowState.Normal;
                }
                HelpWindow._OpenWindow.Activate();
            }

            if (!string.IsNullOrEmpty(sectionName))
            {
                try
                {
                    System.Windows.Documents.Paragraph paragraph =
                        (System.Windows.Documents.Paragraph)HelpWindow._OpenWindow.FindName(sectionName);
                    paragraph.BringIntoView();
                }
                catch { }
            }
        }

        private void ExternalAppTooltip_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ShowHelp("ExternalApp");
        }

        private void WhitelistAdd_Click(object sender, RoutedEventArgs e)
        {
            WhitelistItems.Add(new WhitelistItem());
            Whitelist.UpdateLayout();
            WhitelistScroller.ScrollToEnd();
            if (Whitelist.Items.Count > 0)
            {
                ContentPresenter cp = Whitelist.ItemContainerGenerator.ContainerFromIndex(Whitelist.Items.Count - 1) as ContentPresenter;
                TextBox tb = (TextBox)cp.ContentTemplate.FindName("WhitelistIP", cp);
                tb?.Focus();
            }
        }

        private void WhitelistRemove_Click(object sender, RoutedEventArgs e)
        {
            WhitelistItem item = (sender as Button).DataContext as WhitelistItem;
            WhitelistItems.Remove(item);
            if (WhitelistItems.Count < 1)
                WhitelistItems.Add(new WhitelistItem());
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ((HwndSource)PresentationSource.FromVisual(this)).AddHook(HookProc);
        }

        protected virtual IntPtr HookProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Single instance application. If app is already running, bring window to front.
            if (msg == App.NativeMethods.WM_SHOWME)
            {
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                Topmost = true;
                Topmost = false;
                Focus();
            }

            return IntPtr.Zero;
        }
    }
}
