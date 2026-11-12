using System;
using System.IO;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using tcpTrigger.GUI.Configuration;
using tcpTrigger.GUI.Services;

namespace tcpTrigger.GUI.Views
{
    // Shows whether the tcpTrigger service is monitoring (running or stopped).
    public partial class MonitorStatusBar : UserControl
    {
        // How often the tcpTrigger service state is checked.
        private static readonly TimeSpan s_visibleInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan s_trayInterval = TimeSpan.FromSeconds(60);

        private readonly DispatcherTimer _timer;
        private bool _isStarting;
        private bool _isStopping;

        // Whether the last check found the service running.
        private bool _wasRunning;

        // Why the last start failed. Shown in the banner.
        private string _startError;

        // Raised when the banner's Open Settings button is clicked.
        public event RoutedEventHandler SettingsClick;

        // Raised when "Service details" is clicked in the status menu.
        public event RoutedEventHandler ServiceDetailsClick;

        // Raised when the service was running at the last check and isn't now.
        public event EventHandler MonitoringStopped;

        public MonitorStatusBar()
        {
            InitializeComponent();

            // Check the state of the tcpTrigger service on a timer, more often while the window is visible.
            _timer = new DispatcherTimer
            {
                Interval = s_trayInterval
            };
            _timer.Tick += (sender, e) => CheckServiceStatus();
            _timer.Start();

            IsVisibleChanged += (sender, e) =>
            {
                // Do less frequent checks when hidden to the tray. More frequent when visible.
                if (IsVisible)
                {
                    _timer.Interval = s_visibleInterval;
                    CheckServiceStatus();
                }
                else
                {
                    _timer.Interval = s_trayInterval;
                }
            };
        }

        private void CheckServiceStatus()
        {
            if (_isStarting || _isStopping)
            {
                // The button being used shows the progress, and checks again when it's done.
                return;
            }

            ServiceControllerStatus status;
            try
            {
                status = TcpTriggerService.GetStatus();
            }
            catch (InvalidOperationException)
            {
                ShowNotMonitoring("Not monitoring. The tcpTrigger service isn't installed.");
                CheckForStop(isRunning: false);
                return;
            }
            catch
            {
                ShowNotMonitoring("Not monitoring. The status of the tcpTrigger service couldn't be checked.");
                CheckForStop(isRunning: false);
                return;
            }

            switch (status)
            {
                case ServiceControllerStatus.Running:
                    _startError = null;
                    ShowMonitoring();
                    break;
                case ServiceControllerStatus.Stopped:
                    {
                        // A new installation has no settings file, and the service won't start
                        // without one. Point to Settings instead of offering Start.
                        if (!File.Exists(ConfigPaths.GetConfigPath()))
                        {
                            ShowNotMonitoring("Not monitoring. tcpTrigger isn't set up yet.", SettingsButton);
                            break;
                        }

                        string message = "Not monitoring. The tcpTrigger service is stopped.";
                        if (_startError != null)
                        {
                            message += $"{Environment.NewLine}Unable to start the service. {_startError}";
                        }
                        ShowNotMonitoring(message, StartButton);
                        break;
                    }
                case ServiceControllerStatus.StartPending:
                    ShowNotMonitoring("Not monitoring. The tcpTrigger service is starting.");
                    break;
                case ServiceControllerStatus.StopPending:
                    ShowNotMonitoring("Not monitoring. The tcpTrigger service is stopping.");
                    break;
                default:
                    ShowNotMonitoring("Not monitoring. The tcpTrigger service isn't running.");
                    break;
            }

            CheckForStop(status == ServiceControllerStatus.Running);
        }

        // Raises MonitoringStopped if the service was running at the last check and isn't now.
        private void CheckForStop(bool isRunning)
        {
            if (_wasRunning && !isRunning)
            {
                MonitoringStopped?.Invoke(this, EventArgs.Empty);
            }
            _wasRunning = isRunning;
        }

        private void ShowMonitoring()
        {
            MonitoringBar.Visibility = Visibility.Visible;
            NotMonitoringBanner.Visibility = Visibility.Collapsed;
        }

        // `button` is the one button to show in the banner, or null for none.
        private void ShowNotMonitoring(string message, Button button = null)
        {
            BannerText.Text = message;
            StartButton.Visibility = button == StartButton ? Visibility.Visible : Visibility.Collapsed;
            SettingsButton.Visibility = button == SettingsButton ? Visibility.Visible : Visibility.Collapsed;
            MonitoringBar.Visibility = Visibility.Collapsed;
            NotMonitoringBanner.Visibility = Visibility.Visible;
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsClick?.Invoke(this, e);
        }

        // Opens the status menu above the "Monitoring" status.
        private void ServiceStatus_Click(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = ServiceStatusButton.ContextMenu;
            menu.PlacementTarget = ServiceStatusButton;
            menu.IsOpen = true;
        }

        private void ServiceDetails_Click(object sender, RoutedEventArgs e)
        {
            ServiceDetailsClick?.Invoke(this, e);
        }

        // Asks first, then stops the service through an elevated copy of the app.
        private async void StopService_Click(object sender, RoutedEventArgs e)
        {
            MainWindow mainWindow = (MainWindow)Window.GetWindow(this);
            if (!await mainWindow.ConfirmStopServiceAsync())
            {
                return;
            }

            _isStopping = true;
            ServiceStatusButton.IsEnabled = false;
            MonitoringText.Text = "Stopping...";

            string stopError = await ElevatedServiceControl.StopAsync();

            _isStopping = false;
            ServiceStatusButton.IsEnabled = true;
            MonitoringText.Text = "Monitoring";
            CheckServiceStatus();

            if (stopError != null)
            {
                await mainWindow.ShowMessageAsync("Unable to stop the service", stopError);
            }
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            _isStarting = true;
            _startError = null;
            StartButton.IsEnabled = false;
            ShieldIcon.Visibility = Visibility.Collapsed;
            StartingSpinner.Visibility = Visibility.Visible;
            StartButtonText.Text = "Starting...";

            _startError = await ElevatedServiceControl.StartAsync();

            _isStarting = false;
            StartButton.IsEnabled = true;
            StartingSpinner.Visibility = Visibility.Collapsed;
            ShieldIcon.Visibility = Visibility.Visible;
            StartButtonText.Text = "Start service";
            CheckServiceStatus();
        }
    }
}