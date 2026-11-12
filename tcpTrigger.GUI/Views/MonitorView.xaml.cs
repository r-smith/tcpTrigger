using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using tcpTrigger.GUI.Models;
using tcpTrigger.GUI.Services;

namespace tcpTrigger.GUI.Views
{
    public partial class MonitorView : UserControl
    {
        private const int MaxEvents = 10_000;

        // New detections bring the window forward at most once in this many seconds.
        private const int AutoFocusSeconds = 30;

        private readonly LimitedObservableCollection<DetectionEvent> _detectionEvents = new LimitedObservableCollection<DetectionEvent>(MaxEvents);
        private readonly DetectionLogWatcher _logWatcher = new DetectionLogWatcher();

        // Starts at launch, so detections in the first AutoFocusSeconds don't bring the window forward.
        private DateTime _lastAutoFocusTime = DateTime.Now;

        // The row the right-click menu was opened on, and its detection.
        private DataGridRow _menuRow;
        private DetectionEvent _menuEvent;

        // The detection whose details line is open, or null if none.
        private DetectionEvent _openDetection;

        // Whether detailed view is showing instead of compact view.
        private bool _isDetailedView;

        // The search text for the filter.
        private string _filterText = string.Empty;

        // Waits for a short pause in typing before filtering.
        private readonly DispatcherTimer _filterDelay = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };

        // What the empty list says when there's no filter.
        private string _emptyTitle;
        private string _emptyMessage;

        public MonitorView()
        {
            InitializeComponent();
            SetDetailedView(UserPreferences.DetailedView);

            _emptyTitle = EmptyTitle.Text;
            _emptyMessage = EmptyMessage.Text;

            _filterDelay.Tick += (sender, e) =>
            {
                _filterDelay.Stop();
                ApplyFilter();
            };

            // Stop watching the event log when the main window closes.
            Unloaded += (sender, e) => _logWatcher.Dispose();

            LoadDetectionEvents();
        }

        private async void LoadDetectionEvents()
        {
            _logWatcher.DetectionReceived += LogWatcher_DetectionReceived;
            try
            {
                // Read existing events and start watching for new ones.
                List<DetectionEvent> existing = await Task.Run(() =>
                    _logWatcher.Start(MaxEvents, UserPreferences.EventLogMaxDays, UserPreferences.EventLogFromDate));

                // `existing` is newest first. Any new events that arrived while loading
                // were already inserted at the top, so these are added below them.
                foreach (DetectionEvent detection in existing)
                {
                    if (_detectionEvents.Count >= _detectionEvents.Capacity)
                    {
                        break;
                    }
                    _detectionEvents.Add(detection);
                }
            }
            catch (Exception ex)
            {
                _logWatcher.Dispose();
                _emptyTitle = "Unable to read detections";
                _emptyMessage = $"The tcpTrigger event log couldn't be read. {ex.Message}";
            }

            // Bind the list to the grid only after it's filled, so the grid isn't
            // updated once per event during the initial load. Then remove the loading overlay.
            ICollectionView view = CollectionViewSource.GetDefaultView(_detectionEvents);
            Log.ItemsSource = view;
            LoadingOverlay.Child = null;
            LoadingOverlay.Visibility = Visibility.Collapsed;

            // Applies a filter typed while the history was loading, and sets the empty list's text.
            ApplyFilter();
        }

        // Called on a background thread for each new detection event.
        private void LogWatcher_DetectionReceived(DetectionEvent detection)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _detectionEvents.Insert(0, detection);
                UpdateFilterCount();

                if (UserPreferences.FocusOnUpdate)
                {
                    SetTopMost();
                }
            }));
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Log.ItemsSource != null && Log.Items.Count > 0)
                {
                    Log.ScrollIntoView(Log.Items[0]);
                }
            }), DispatcherPriority.ApplicationIdle);
        }

        // Switches between detailed and compact view.
        public void SetDetailedView(bool detailedView)
        {
            _isDetailedView = detailedView;

            Visibility compact = detailedView ? Visibility.Collapsed : Visibility.Visible;
            CompactSourceColumn.Visibility = compact;
            CompactPortColumn.Visibility = compact;

            Visibility detailed = detailedView ? Visibility.Visible : Visibility.Collapsed;
            DetailedSourceIPColumn.Visibility = detailed;
            DetailedDestinationIPColumn.Visibility = detailed;
            DetailedDestinationPortColumn.Visibility = detailed;
            DetailedServiceColumn.Visibility = detailed;
            DetailedProtocolColumn.Visibility = detailed;

            Log.HeadersVisibility = detailedView ? DataGridHeadersVisibility.Column : DataGridHeadersVisibility.None;

            // Detailed view doesn't need to show any extra details.
            if (detailedView && _openDetection != null)
            {
                _openDetection.ShowDetails = false;
                _openDetection = null;
            }
        }

        // Shows the filter bar and puts the cursor in the filter box.
        public void OpenFilter()
        {
            FilterBar.Visibility = Visibility.Visible;
            FilterBox.Focus();
            FilterBox.SelectAll();
        }

        // Opens the filter bar or toggles it closed if there's nothing typed.
        public void ToggleFilter()
        {
            if (FilterBar.Visibility == Visibility.Visible && string.IsNullOrWhiteSpace(FilterBox.Text))
            {
                CloseFilter();
            }
            else
            {
                OpenFilter();
            }
        }

        // Clears the filter and hides the filter bar.
        private void CloseFilter()
        {
            FilterBox.Text = string.Empty;
            _filterDelay.Stop();
            ApplyFilter();
            FilterBar.Visibility = Visibility.Collapsed;
            Log.Focus();
        }

        private void CloseFilter_Click(object sender, RoutedEventArgs e)
        {
            CloseFilter();
        }

        // Each change restarts the delay, so the list is filtered once typing pauses.
        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filterDelay.Stop();
            _filterDelay.Start();
        }

        private void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseFilter();
                e.Handled = true;
            }
        }

        // Filters the list by the filter box's text. New detections are filtered as they arrive.
        private void ApplyFilter()
        {
            _filterText = FilterBox.Text.Trim();

            // The view is null until the history has loaded. Loading calls this again when done.
            if (Log.ItemsSource is ICollectionView view)
            {
                view.Filter = _filterText.Length == 0 ? null : (Predicate<object>)MatchesFilter;
            }

            UpdateFilterCount();
            UpdateEmptyText();
        }

        // A detection matches if the filter text appears in its source or destination address,
        // destination port, port name, or protocol, ignoring case.
        private bool MatchesFilter(object item)
        {
            DetectionEvent detection = (DetectionEvent)item;
            return Contains(detection.SourceIP?.ToString())
                || Contains(detection.DestinationIP?.ToString())
                || (detection.Type != MatchType.ICMP && Contains(detection.DestinationPort.ToString()))
                || Contains(detection.PortName)
                || Contains(detection.Type.ToString());
        }

        private bool Contains(string value)
        {
            return value != null && value.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Shows how many detections match when a filter is set.
        private void UpdateFilterCount()
        {
            FilterCount.Text = _filterText.Length == 0
                ? string.Empty
                : $"{Log.Items.Count:N0} of {_detectionEvents.Count:N0}";
        }

        // Set the empty list message depending on whether there's no detections or nothing matches the filter.
        private void UpdateEmptyText()
        {
            if (_filterText.Length == 0)
            {
                EmptyTitle.Text = _emptyTitle;
                EmptyMessage.Text = _emptyMessage;
            }
            else
            {
                EmptyTitle.Text = "No matches";
                EmptyMessage.Text = $"No detections match \"{_filterText}\".";
            }
        }

        private void SetTopMost()
        {
            if ((DateTime.Now - _lastAutoFocusTime).TotalSeconds < AutoFocusSeconds)
            {
                return;
            }

            _lastAutoFocusTime = DateTime.Now;
            MainWindow mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow.ShowOnTop();
        }

        // Opens the right-click menu only over a row.
        private void Log_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            DependencyObject clicked = e.OriginalSource as DependencyObject;
            _menuRow = clicked == null ? null : ItemsControl.ContainerFromElement(Log, clicked) as DataGridRow;
            if (_menuRow == null)
            {
                e.Handled = true;
                return;
            }

            _menuEvent = (DetectionEvent)_menuRow.Item;
            _menuRow.Background = (Brush)FindResource("DG.Row.MouseOver.Background");

            LookupHostnameItem.IsEnabled = _menuEvent.SourceIP != null;

            // Ignore rules only take IPv4 source addresses.
            IgnoreSourceItem.IsEnabled = _menuEvent.SourceIP?.AddressFamily == AddressFamily.InterNetwork;
        }

        // Clicking a row opens its details, and closes any other row's details. Clicking a row
        // with details shown closes it.
        private void Log_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDetailedView)
            {
                return;
            }

            if (e.OriginalSource is DependencyObject clicked
                && ItemsControl.ContainerFromElement(Log, clicked) is DataGridRow row)
            {
                DetectionEvent detection = (DetectionEvent)row.Item;

                if (_openDetection != null)
                {
                    _openDetection.ShowDetails = false;
                }

                // Clicking an open row closes it. Clicking any other row opens that one instead.
                _openDetection = detection == _openDetection ? null : detection;

                if (_openDetection != null)
                {
                    _openDetection.ShowDetails = true;
                }
            }
        }

        // Make the arrow keys scroll the entire list instead of changing the selected row.
        private void Log_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(Log.Template.FindName("DG_ScrollViewer", Log) is ScrollViewer scroller))
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Up:
                    scroller.LineUp();
                    break;
                case Key.Down:
                    scroller.LineDown();
                    break;
                case Key.PageUp:
                    scroller.PageUp();
                    break;
                case Key.PageDown:
                    scroller.PageDown();
                    break;
                case Key.Home:
                    scroller.ScrollToHome();
                    break;
                case Key.End:
                    scroller.ScrollToEnd();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private void LogMenu_Closed(object sender, RoutedEventArgs e)
        {
            _menuRow?.ClearValue(BackgroundProperty);
            _menuRow = null;
        }

        private void CopySourceIP_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(_menuEvent.SourceIP?.ToString() ?? string.Empty);
        }

        private void CopyEventDetails_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(GetDetails(_menuEvent));
        }

        // Shows the source's hostname from a reverse DNS lookup.
        private async void LookupHostname_Click(object sender, RoutedEventArgs e)
        {
            IPAddress address = _menuEvent.SourceIP;
            string notFound = $"No hostname was found for {address}.";
            string message;
            string hostname = null;

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                IPHostEntry entry = await Dns.GetHostEntryAsync(address);

                // Some lookups that find nothing return the address itself as the name.
                if (string.IsNullOrEmpty(entry.HostName) || entry.HostName == address.ToString())
                {
                    message = notFound;
                }
                else
                {
                    hostname = entry.HostName;
                    message = $"{address} is {hostname}.";
                }
            }
            catch (SocketException)
            {
                message = notFound;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            MainWindow mainWindow = (MainWindow)Window.GetWindow(this);
            await mainWindow.ShowMessageAsync("DNS lookup", message, hostname);
        }

        private async void IgnoreSource_Click(object sender, RoutedEventArgs e)
        {
            MainWindow mainWindow = (MainWindow)Window.GetWindow(this);
            await mainWindow.CreateIgnoreRuleAsync(GetIgnoreRule(_menuEvent));
        }

        // A new rule for this detection's source, on the same port and protocol.
        private static IgnoreRule GetIgnoreRule(DetectionEvent detection)
        {
            string source = detection.SourceIP.ToString();
            return new IgnoreRule
            {
                IsEnabled = true,
                Name = source,
                Sources = new List<string> { source },
                Destinations = new List<string> { "any" },
                Services = new List<string> { GetService(detection) }
            };
        }

        // The detection's port and protocol, written the way ignore rules take them.
        private static string GetService(DetectionEvent detection)
        {
            switch (detection.Type)
            {
                case MatchType.TCP:
                    return $"tcp:{detection.DestinationPort}";
                case MatchType.UDP:
                    return $"udp:{detection.DestinationPort}";
                case MatchType.ICMP:
                    return "icmp";
                default:
                    return "any";
            }
        }

        // One field per line, like the tcpTrigger event log.
        private static string GetDetails(DetectionEvent detection)
        {
            List<string> lines = new List<string>
            {
                $"Time: {detection.Timestamp}",
                $"Type: {detection.Action}",
                $"Source IP: {detection.SourceIP}"
            };
            if (detection.Type != MatchType.ICMP)
            {
                lines.Add($"Source port: {detection.SourcePort}");
            }
            lines.Add($"Destination IP: {detection.DestinationIP}");
            if (detection.Type != MatchType.ICMP)
            {
                lines.Add($"Destination port: {detection.DestinationPort}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (ExternalException)
            {
                // Another program has the clipboard open.
                MainWindow mainWindow = (MainWindow)Window.GetWindow(this);
                _ = mainWindow.ShowMessageAsync("Unable to copy", "The clipboard is being used by another program. Try again.");
            }
        }
    }
}