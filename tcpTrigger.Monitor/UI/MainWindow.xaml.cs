using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net;
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

        public MainWindow()
        {
            InitializeComponent();

            Settings.Load();
            SetCheckboxState();
            Log.ItemsSource = DetectionEvents;
            SubscribeToDetectionEvents();
        }

        private async void SubscribeToDetectionEvents()
        {
            const string _detectionQuery =
                "<QueryList>"
                + "<Query Id='0'>"
                + "  <Select Path='Application'>"
                + "    *[System[Provider[@Name='tcpTrigger']"
                + "      and ( (EventID &gt;= 200 and EventID &lt;= 203) )]]"
                + "  </Select>"
                + "</Query>"
                + "</QueryList>";

            EventLogWatcher watcher = null;
            await Task.Run(() =>
            {
                try
                {
                    EventLogQuery logQuery = new EventLogQuery("Application", PathType.LogName, _detectionQuery);
                    watcher = new EventLogWatcher(logQuery, null, true);
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
                if (recordDescription.StartsWith("A captured"))
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

        private void LaunchAtLogonOption_Click(object sender, RoutedEventArgs e)
        {
            Settings.LaunchAtLogon = LaunchAtLogonOption.IsChecked;
            if (Settings.LaunchAtLogon)
            {
                // Attempt to create a shortcut to the user's startup folder by copying shortcut from start menu.
                try
                {
                    string sourcePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs\tcpTrigger\tcpTrigger Monitor.lnk";
                    string destinationPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\tcpTrigger Monitor.lnk";
                    // Copy shortcut from common start menu to user's startup. Throw exception if source shortcut doesn't exist.
                    // The start menu shortcut is created by the tcpTrigger installer, but could have been removed by the user.
                    File.Copy(sourceFileName: sourcePath,
                              destFileName: destinationPath,
                              overwrite: true);
                }
                catch (Exception ex)
                {
                    // Failed to copy shortcut to user's startup folder.
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
    }
}
