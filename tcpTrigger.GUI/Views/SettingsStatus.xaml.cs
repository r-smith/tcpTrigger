using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using tcpTrigger.GUI.Services;

namespace tcpTrigger.GUI.Views
{
    public partial class SettingsStatus : UserControl
    {
        private const string IconRunning = "icon.check-circle";
        private const string IconStopped = "icon.exclamation-circle";
        private const string ButtonContentStartService = "Start service";
        private const string ButtonContentStartingService = "Starting...";
        private const string ButtonContentStopService = "Stop service";
        private const string ButtonContentStoppingService = "Stopping...";

        private readonly DispatcherTimer _timer;

        // True while a start or stop is in progress.
        private bool _isBusy;

        // Whether the last check found the service running.
        private bool _isRunning;

        public SettingsStatus()
        {
            InitializeComponent();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _timer.Tick += Timer_Tick;

            // Only poll the service while this tab is on screen. The control is unloaded
            // when the user switches to another tab or closes the Settings window.
            Loaded += SettingsStatus_Loaded;
            Unloaded += SettingsStatus_Unloaded;
        }

        private void SettingsStatus_Loaded(object sender, RoutedEventArgs e)
        {
            CheckServiceStatus();
            _timer.Start();
        }

        private void SettingsStatus_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            CheckServiceStatus();
        }

        private void CheckServiceStatus()
        {
            if (_isBusy)
            {
                return;
            }

            try
            {
                ServiceControllerStatus status = TcpTriggerService.GetStatus();
                _isRunning = status == ServiceControllerStatus.Running;
                ServiceStatus.Text = GetStatusText(status);

                switch (status)
                {
                    case ServiceControllerStatus.Running:
                        ServiceControlButtonText.Text = ButtonContentStopService;
                        ServiceControl.IsEnabled = true;
                        StatusImage.Source = (DrawingImage)Application.Current.Resources[IconRunning];
                        break;
                    case ServiceControllerStatus.Stopped:
                        ServiceControlButtonText.Text = ButtonContentStartService;
                        ServiceControl.IsEnabled = true;
                        StatusImage.Source = (DrawingImage)Application.Current.Resources[IconStopped];
                        break;
                    default:
                        ServiceControlButtonText.Text = ButtonContentStartService;
                        ServiceControl.IsEnabled = false;
                        StatusImage.Source = (DrawingImage)Application.Current.Resources[IconStopped];
                        break;
                }
            }
            catch (InvalidOperationException)
            {
                _isRunning = false;
                ServiceStatus.Text = "not installed";
                ServiceControlButtonText.Text = ButtonContentStartService;
                ServiceControl.IsEnabled = false;
                StatusImage.Source = (DrawingImage)Application.Current.Resources[IconStopped];
            }
            catch
            {
                _isRunning = false;
                ServiceStatus.Text = "unavailable";
                ServiceControlButtonText.Text = ButtonContentStartService;
                ServiceControl.IsEnabled = false;
                StatusImage.Source = (DrawingImage)Application.Current.Resources[IconStopped];
            }
        }

        private async void ServiceControl_Click(object sender, RoutedEventArgs e)
        {
            _isBusy = true;
            ServiceControl.IsEnabled = false;
            Spinner.Visibility = Visibility.Visible;

            string action;
            string error;
            if (_isRunning)
            {
                action = "stop";
                ServiceControlButtonText.Text = ButtonContentStoppingService;
                error = await ElevatedServiceControl.StopAsync();
            }
            else
            {
                action = "start";
                ServiceControlButtonText.Text = ButtonContentStartingService;
                error = await ElevatedServiceControl.StartAsync();
            }

            _isBusy = false;
            Spinner.Visibility = Visibility.Collapsed;
            ServiceControl.IsEnabled = true;
            CheckServiceStatus();

            if (error != null)
            {
                // Shown in the Settings window's dialog overlay.
                SettingsWindow settingsWindow = (SettingsWindow)Window.GetWindow(this);
                await settingsWindow.ShowMessageAsync($"Unable to {action} the service", error);
            }
        }

        // Finishes the sentence "The tcpTrigger service is currently ...".
        private static string GetStatusText(ServiceControllerStatus status)
        {
            switch (status)
            {
                case ServiceControllerStatus.Running:
                    return "running";
                case ServiceControllerStatus.Stopped:
                    return "stopped";
                case ServiceControllerStatus.StartPending:
                    return "starting";
                case ServiceControllerStatus.StopPending:
                    return "stopping";
                case ServiceControllerStatus.Paused:
                    return "paused";
                case ServiceControllerStatus.PausePending:
                    return "pausing";
                case ServiceControllerStatus.ContinuePending:
                    return "resuming";
                default:
                    return status.ToString().ToLower();
            }
        }

        private async void OpenServices_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                try
                {
                    Process.Start(System.IO.Path.Combine(Environment.SystemDirectory, "services.msc"));
                }
                catch
                {
                    // Failed to open services.msc. Do nothing.
                }
            });
        }
    }
}