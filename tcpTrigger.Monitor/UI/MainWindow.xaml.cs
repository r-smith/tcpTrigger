using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;

namespace tcpTrigger.Monitor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly LimitedObservableCollection<DetectionEvent> DetectionEvents = new LimitedObservableCollection<DetectionEvent>(10_000);
        private readonly ICollectionView DetectionEventsView;
        private System.Windows.Forms.NotifyIcon NotifyIcon;

        public MainWindow()
        {
            InitializeComponent();

            Settings.Load();
            SetCheckboxState();
            SetTrayIconState();
            RefreshMaximizeRestoreButton();

            DetectionEventsView = CollectionViewSource.GetDefaultView(DetectionEvents);
            DetectionEventsView.Filter = LogFilter;

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
            // Initial loading of events is complete.
            // Restore the previous FocusOnUpdate setting,
            // bind collection to DataGrid,
            // and remove the initial loading overlay.
            Settings.FocusOnUpdate = isAutoFocusEnabled;
            Log.ItemsSource = DetectionEventsView;
            LoadingOverlay.Child = null;
            LoadingOverlay.Visibility = Visibility.Collapsed;
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
                                + @".*\r\n"
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
                            Visibility = Visibility.Visible;
                            Show();
                            WindowState = WindowState.Normal;
                            Topmost = true;
                            Topmost = false;
                        }
                    }));
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (Log.ItemsSource != null)
                        {
                            Log.ScrollIntoView(Log.Items[0]);
                        }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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

        private bool LogFilter(object item)
        {
            var entry = item as DetectionEvent;
            if (string.IsNullOrWhiteSpace(FilterField.Text))
                return true;
            else if (entry.DestinationIP.ToString().Contains(FilterField.Text))
                return true;
            else if (entry.SourceIP.ToString().Contains(FilterField.Text))
                return true;
            else if (entry.DestinationPort.ToString().Contains(FilterField.Text))
                return true;
            else if (entry.Action.ToUpper().Contains(FilterField.Text.ToUpper()))
                return true;
            else
                return false;
        }

        private void FilterField_KeyUp(object sender, KeyEventArgs e)
        {
            DetectionEventsView.Refresh();
        }

        private void FilterClear_Click(object sender, RoutedEventArgs e)
        {
            FilterField.Text = "";
            DetectionEventsView.Refresh();
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Remove focus from filter textbox when window is clicked.
            if (FilterField.IsFocused)
            {
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(FilterField), null);
                Keyboard.ClearFocus();
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
                    string destinationPath = Path.Combine(new string[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                        "tcpTrigger Monitor.lnk"
                    });
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
                    string shortcutPath = Path.Combine(new string[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                        "tcpTrigger Monitor.lnk"
                    });
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
            SetTrayIconState();
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
            SetTrayIconState();
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

        private void LaunchSettingsManager_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path;
                path = Path.Combine(new string[]
                {
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "tcpTrigger Manager.exe"
                });
                if (!File.Exists(path))
                {
                    path = Path.Combine(new string[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                        "Programs",
                        "tcpTrigger",
                        "tcpTrigger Manager.lnk"
                    });
                }
                System.Diagnostics.Process p = new System.Diagnostics.Process();
                p.StartInfo.FileName = path;
                p.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void SetTrayIconState()
        {
            try
            {
                if ((Settings.ExitToTray || Settings.MinimizeToTray) && NotifyIcon == null)
                {
                    // Build context menu for tray icon.
                    System.Windows.Forms.ContextMenuStrip menuStrip = new System.Windows.Forms.ContextMenuStrip();
                    System.Windows.Forms.ToolStripMenuItem menuExit = new System.Windows.Forms.ToolStripMenuItem("Exit tcpTrigger Monitor");
                    menuExit.Click += (s, args) =>
                    {
                        if (NotifyIcon != null)
                        {
                            NotifyIcon.Icon = null;
                            NotifyIcon.Dispose();
                        }
                        Application.Current.Shutdown();
                    };
                    menuStrip.Items.Add(menuExit);

                    // Create tray icon.
                    NotifyIcon = new System.Windows.Forms.NotifyIcon
                    {
                        Icon = new System.Drawing.Icon(Application.GetResourceStream(new Uri("pack://application:,,,/tcpTrigger Monitor.ico")).Stream),
                        Text = "tcpTrigger",
                        ContextMenuStrip = menuStrip
                    };
                    NotifyIcon.MouseUp += NotifyIcon_MouseUp;
                    NotifyIcon.Visible = true;
                }

                if (Settings.ExitToTray == false && Settings.MinimizeToTray == false && NotifyIcon != null)
                {
                    NotifyIcon.Dispose();
                    NotifyIcon = null;
                }
            }
            catch
            {
                // Failed to create a tray icon. Disable minimize and exit to tray options.
                if (NotifyIcon != null)
                {
                    NotifyIcon.Dispose();
                    NotifyIcon = null;
                }
                Settings.ExitToTray = false;
                Settings.MinimizeToTray = false;
                SetCheckboxState();
            }
        }

        private void HideToTray()
        {
            Visibility = Visibility.Hidden;
        }

        private void NotifyIcon_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                // Left click. Restore application window.
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
            RefreshMaximizeRestoreButton();

            if (WindowState == WindowState.Minimized && Settings.MinimizeToTray)
            {
                HideToTray();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (Settings.ExitToTray)
            {
                HideToTray();
                e.Cancel = true;
            }
            else if (NotifyIcon != null)
            {
                NotifyIcon.Icon = null;
                NotifyIcon.Dispose();
            }
        }

        private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeRestoreButtonClick(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RefreshMaximizeRestoreButton()
        {
            if (WindowState == WindowState.Maximized)
            {
                maximizeButton.Visibility = Visibility.Collapsed;
                restoreButton.Visibility = Visibility.Visible;
            }
            else
            {
                maximizeButton.Visibility = Visibility.Visible;
                restoreButton.Visibility = Visibility.Collapsed;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ((HwndSource)PresentationSource.FromVisual(this)).AddHook(HookProc);
        }

        protected virtual IntPtr HookProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                // We need to tell the system what our size should be when maximized. Otherwise it will cover the whole screen,
                // including the task bar.
                MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

                // Adjust the maximized size and position to fit the work area of the correct monitor
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

                if (monitor != IntPtr.Zero)
                {
                    MONITORINFO monitorInfo = new MONITORINFO();
                    monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    GetMonitorInfo(monitor, ref monitorInfo);
                    RECT rcWorkArea = monitorInfo.rcWork;
                    RECT rcMonitorArea = monitorInfo.rcMonitor;
                    mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                    mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                    mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);
                }

                Marshal.StructureToPtr(mmi, lParam, true);
            }
            else if (msg == WM_WINDOWPOSCHANGING)
            {
                // Remove focus from filter textbox if title bar area is clicked.
                if (FilterField.IsFocused)
                {
                    FocusManager.SetFocusedElement(FocusManager.GetFocusScope(FilterField), null);
                    Keyboard.ClearFocus();
                }
            }
            else if (msg == App.NativeMethods.WM_SHOWME)
            {
                // Single instance application. If app is already running, bring window to front.
                Visibility = Visibility.Visible;
                Show();
                WindowState = WindowState.Normal;
                Topmost = true;
                Topmost = false;
                Focus();
            }

            return IntPtr.Zero;
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public RECT(int left, int top, int right, int bottom)
            {
                this.Left = left;
                this.Top = top;
                this.Right = right;
                this.Bottom = bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                this.X = x;
                this.Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }
    }
}
