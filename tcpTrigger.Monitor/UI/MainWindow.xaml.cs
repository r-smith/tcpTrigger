using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace tcpTrigger.Monitor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<DetectionEvent> DetectionEvents = new ObservableCollection<DetectionEvent>();
        private System.Windows.Forms.NotifyIcon NotifyIcon;

        public MainWindow()
        {
            InitializeComponent();

            Settings.Load();
            SetCheckboxState();
            Log.ItemsSource = DetectionEvents;
            SubscribeToDetectionEvents();
            ProcessCommandLineArgs();
        }

        private async void SubscribeToDetectionEvents()
        {
            // Build TimeCreated query to be included in the final event log query.
            // If EventLog_MaxDays is set, use that value to build the query.
            // Otherwise, if EventLog_FromDate is set, use that date to build the query.
            // Otherwise, no timespan query needed, so set to empty string.
            string timeSpan = Settings.EventLog_MaxDays > 0
                ? $"and TimeCreated[timediff(@SystemTime) &lt;= {TimeSpan.FromDays(Settings.EventLog_MaxDays).TotalMilliseconds}]"
                : !string.IsNullOrWhiteSpace(Settings.EventLog_FromDate)
                    ? $"and TimeCreated[@SystemTime&gt;='{Settings.EventLog_FromDate}']"
                    : string.Empty;

            // Build event log query for retreiving tcpTrigger detection events.
            string _detectionQuery =
                "<QueryList>"
                + "<Query Id='0'>"
                + "  <Select Path='Application'>"
                + "    *[System[Provider[@Name='tcpTrigger']"
                + "      and (EventID &gt;= 200 and EventID &lt;= 203) "
                + timeSpan + "]]"
                + "  </Select>"
                + "</Query>"
                + "</QueryList>";

            EventLogWatcher watcher = null;
            // Store current FocusOnUpdate setting. Disable this setting during initial loading of events.
            bool isAutoFocusEnabled = Settings.FocusOnUpdate;
            Settings.FocusOnUpdate = false;
            await Task.Run(() =>
            {
                try
                {
                    // Start event log watcher, while also retrieving all existing events that match our query.
                    EventLogQuery logQuery = new EventLogQuery("Application", PathType.LogName, _detectionQuery);
                    watcher = new EventLogWatcher(eventQuery: logQuery,
                                                  bookmark: null,
                                                  readExistingEvents: true);
                    watcher.EventRecordWritten += new EventHandler<EventRecordWrittenEventArgs>(EventLogEventRead);
                    watcher.Enabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    if (watcher != null)
                    {
                        watcher.Enabled = false;
                        watcher.Dispose();
                    }
                }
            });
            // Initial loading of events is complete. Restore previous FocusOnUpdate setting.
            Settings.FocusOnUpdate = isAutoFocusEnabled;
        }

        private void EventLogEventRead(object obj, EventRecordWrittenEventArgs arg)
        {
            const int DetectICMP = 200;
            const int DetectTCP = 201;
            const int DetectUDP = 202;
            const int DetectDHCP = 203;

            if (arg.EventRecord != null)
            {
                string recordDescription = arg.EventRecord.FormatDescription();
                if (string.IsNullOrEmpty(recordDescription) || recordDescription.StartsWith("A captured"))
                    return;

                string pattern;
                MatchType matchType;

                switch (arg.EventRecord.Id)
                {
                    case DetectICMP:
                        matchType = MatchType.ICMP;
                        pattern = @"^Source IP: (?<s_ip>.*)\r\n"
                                + @"^Destination IP: (?<d_ip>.*)$";
                        break;
                    case DetectTCP:
                        matchType = MatchType.TCP;
                        pattern = @"^Source IP: (?<s_ip>.*)\r\n"
                                + @"^Source port: (?<s_port>.*)\r\n"
                                + @"^Destination IP: (?<d_ip>.*)\r\n"
                                + @"^Destination port: (?<d_port>.*)\r\n";
                        break;
                    case DetectUDP:
                        matchType = MatchType.UDP;
                        pattern = @"^Source IP: (?<s_ip>.*)\r\n"
                                + @"^Source port: (?<s_port>.*)\r\n"
                                + @"^Destination IP: (?<d_ip>.*)\r\n"
                                + @"^Destination port: (?<d_port>.*)$";
                        break;
                    case DetectDHCP:
                        matchType = MatchType.DHCP;
                        pattern = @"^DHCP server IP: (?<s_ip>.*)\r\n"
                                + @"^Interface IP: (?<d_ip>.*)$";
                        break;
                    default:
                        return;
                }

                Match match = Regex.Match(recordDescription, pattern, RegexOptions.Multiline);
                if (match.Success)
                {
                    _ = IPAddress.TryParse(match.Groups["s_ip"]?.Value, out IPAddress sourceIP);
                    _ = IPAddress.TryParse(match.Groups["d_ip"]?.Value, out IPAddress destinationIP);
                    _ = int.TryParse(match.Groups["s_port"]?.Value, out int sourcePort);
                    _ = int.TryParse(match.Groups["d_port"]?.Value, out int destinationPort);

                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        DetectionEvents.Insert(0, new DetectionEvent
                        {
                            Timestamp = arg.EventRecord.TimeCreated.Value,
                            Type = matchType,
                            SourceIP = sourceIP,
                            SourcePort = sourcePort,
                            DestinationIP = destinationIP,
                            DestinationPort = destinationPort
                        });

                        if (Settings.FocusOnUpdate)
                        {
                            if (NotifyIcon != null)
                            {
                                NotifyIcon.Visible = false;
                                Visibility = Visibility.Visible;
                                Show();
                                WindowState = WindowState.Normal;
                            }
                            if (WindowState == WindowState.Minimized)
                            {
                                WindowState = WindowState.Normal;
                            }
                            Topmost = true;
                            Topmost = false;
                            Focus();
                        }
                    }));
                }
            }
        }

        private void SetCheckboxState()
        {
            LaunchAtLogonOption.IsChecked = Settings.LaunchAtLogon;
            MinimizeToTrayOption.IsChecked = Settings.MinimizeToTray;
            ExitToTrayOption.IsChecked = Settings.ExitToTray;
            FocusOnUpdateOption.IsChecked = Settings.FocusOnUpdate;
        }

        private void ProcessCommandLineArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length == 2 && args[1].Equals("-minimized"))
            {
                HideToTray();
            }
        }

        private void LaunchAtLogonOption_Click(object sender, RoutedEventArgs e)
        {
            Settings.LaunchAtLogon = LaunchAtLogonOption.IsChecked;
            if (Settings.LaunchAtLogon)
            {
                // Create a shortcut in the user's startup folder.
                try
                {
                    string destinationPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\tcpTrigger Monitor.lnk";
                    const string description = "Log viewer for tcpTrigger service";
                    const string arguments = "-minimized";

                    // Set shortcut information.
                    IShellLink link = (IShellLink)new ShellLink();
                    link.SetDescription(description);
                    link.SetPath(Assembly.GetExecutingAssembly().Location);
                    link.SetArguments(arguments);

                    // Save it.
                    IPersistFile file = (IPersistFile)link;
                    file.Save(destinationPath, false);
                }
                catch (Exception ex)
                {
                    // Failed to create shortcut in the user's startup folder.
                    // TODO: Give more helpful error message.
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Settings.LaunchAtLogon = false;
                    LaunchAtLogonOption.IsChecked = false;
                }
            }
            else
            {
                // Remove shortcut from user's startup folder.
                try
                {
                    string shortcutPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\tcpTrigger Monitor.lnk";
                    if (File.Exists(shortcutPath))
                    {
                        File.Delete(shortcutPath);
                    }
                }
                catch (Exception ex)
                {
                    // Failed to delete shortcut from user's startup folder.
                    // TODO: Give more helpful error message.
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Settings.LaunchAtLogon = true;
                    LaunchAtLogonOption.IsChecked = true;
                }
            }
        }

        private void MinimizeToTrayOption_Click(object sender, RoutedEventArgs e)
        {
            Settings.MinimizeToTray = MinimizeToTrayOption.IsChecked;
            try
            {
                Registry.SetValue(keyName: @"HKEY_CURRENT_USER\SOFTWARE\tcpTrigger",
                                  valueName: "MinimizeToTray",
                                  value: Settings.MinimizeToTray,
                                  valueKind: RegistryValueKind.DWord);
            }
            catch
            {
                // Failed to store setting in registry. Setting is still applied to current instance of application.
            }
        }

        private void ExitToTrayOption_Click(object sender, RoutedEventArgs e)
        {
            Settings.ExitToTray = ExitToTrayOption.IsChecked;
            try
            {
                Registry.SetValue(keyName: @"HKEY_CURRENT_USER\SOFTWARE\tcpTrigger",
                                  valueName: "ExitToTray",
                                  value: Settings.ExitToTray,
                                  valueKind: RegistryValueKind.DWord);
            }
            catch
            {
                // Failed to store setting in registry. Setting is still applied to current instance of application.
            }
        }

        private void FocusOnUpdateOption_Click(object sender, RoutedEventArgs e)
        {
            Settings.FocusOnUpdate = FocusOnUpdateOption.IsChecked;
            try
            {
                Registry.SetValue(keyName: @"HKEY_CURRENT_USER\SOFTWARE\tcpTrigger",
                                  valueName: "FocusOnUpdate",
                                  value: Settings.FocusOnUpdate,
                                  valueKind: RegistryValueKind.DWord);
            }
            catch
            {
                // Failed to store setting in registry. Setting is still applied to current instance of application.
            }
        }

        private void HideToTray()
        {
            Visibility = Visibility.Hidden;
            try
            {
                if (NotifyIcon == null)
                {
                    // Build context menu for tray icon.
                    System.Windows.Forms.ContextMenuStrip menuStrip = new System.Windows.Forms.ContextMenuStrip();
                    System.Windows.Forms.ToolStripMenuItem menuExit = new System.Windows.Forms.ToolStripMenuItem("Exit tcpTrigger Monitor");
                    menuExit.Click += (s, args) => Application.Current.Shutdown();
                    menuStrip.Items.Add(menuExit);

                    // Create tray icon.
                    NotifyIcon = new System.Windows.Forms.NotifyIcon
                    {
                        Icon = new System.Drawing.Icon(Application.GetResourceStream(new Uri("pack://application:,,,/tcpTrigger Monitor.ico")).Stream),
                        Text = "tcpTrigger",
                        ContextMenuStrip = menuStrip
                    };
                    NotifyIcon.MouseUp += NotifyIcon_MouseUp;
                }
                NotifyIcon.Visible = true;
            }
            catch
            {
                Visibility = Visibility.Visible;
            }
        }

        private void NotifyIcon_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                // Left click. Restore application window.
                NotifyIcon.Visible = false;
                Visibility = Visibility.Visible;
                Show();
                WindowState = WindowState.Normal;
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                // Right click. Display context menu.
                MethodInfo mi = typeof(System.Windows.Forms.NotifyIcon)
                    .GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                mi.Invoke(NotifyIcon, null);
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && Settings.MinimizeToTray)
            {
                HideToTray();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (Settings.ExitToTray)
            {
                HideToTray();
                e.Cancel = true;
            }
            else if (NotifyIcon != null)
            {
                NotifyIcon.Dispose();
            }
        }
    }
}
