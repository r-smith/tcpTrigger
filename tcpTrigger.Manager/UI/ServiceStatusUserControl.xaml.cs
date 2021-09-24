using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace tcpTrigger.Manager
{
    /// <summary>
    /// Interaction logic for ServiceStatus.xaml
    /// </summary>
    public partial class ServiceStatusUserControl : UserControl
    {
        private const int EXIT_SUCCESS = 0;
        private const int EXIT_FAILURE = 1;
        private const int EXIT_CANCEL = 2;
        private const string _serviceName = "tcpTrigger";
        private const string Stopping = "Stopping...";
        private const string Starting = "Starting...";
        private const string StartService = "Start service";
        private const string StopService = "Stop service";

        private readonly ObservableCollection<ServiceEvent> ServiceEvents = new ObservableCollection<ServiceEvent>();
        private readonly ServiceController TcpTriggerService = new ServiceController(_serviceName);
        private bool IsInitialLoading;

        public ServiceStatusUserControl()
        {
            InitializeComponent();

            SetUacIcon();
            ServiceLog.ItemsSource = ServiceEvents;
            SubscribeToServiceEvents();

            System.Timers.Timer refreshServiceStatus = new System.Timers.Timer();
            refreshServiceStatus.Interval = TimeSpan.FromSeconds(5).TotalMilliseconds;
            refreshServiceStatus.Elapsed += RefreshServiceStatus_Elapsed;
            refreshServiceStatus.Enabled = true;
            RefreshServiceStatus_Elapsed(null, null);
        }

        private async void SubscribeToServiceEvents()
        {
            // Delay running initial event log subscription.
            // This gives GUI time to load when launching the application on single core CPUs.
            await Task.Delay(250);

            // Build event log query for retreiving tcpTrigger service events and Windows shutdown/startup events.
            const int _maxDays = 30;
            string timeSpan = $"and TimeCreated[timediff(@SystemTime) &lt;= {TimeSpan.FromDays(_maxDays).TotalMilliseconds}]";
            string _detectionQuery =
                "<QueryList>"
                + "<Query Id='0'>"
                + $"  <Select Path='Application'>*[System[Provider[@Name='tcpTrigger'] {timeSpan}]]</Select>"
                + "  <Suppress Path='Application'>*[System[( (EventID &gt;= 200 and EventID &lt;= 210) )]]</Suppress>"
                + "</Query>"
                + "<Query Id='1'>"
                + "  <Select Path='System'>*[System[Provider[@Name='Microsoft-Windows-Eventlog' or @Name='Microsoft-Windows-Kernel-General' or @Name='User32']"
                + $"    and (EventID=12 or EventID=13 or EventID=6008 or EventID=1074) {timeSpan}]]</Select>"
                + "</Query>"
                + "</QueryList>";

            EventLogWatcher watcher = null;
            IsInitialLoading = true;
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
                catch
                {
                    if (watcher != null)
                    {
                        watcher.Enabled = false;
                        watcher.Dispose();
                    }
                }
            });
            IsInitialLoading = false;
        }

        private void EventLogEventRead(object obj, EventRecordWrittenEventArgs arg)
        {
            if (arg.EventRecord != null)
            {
                try
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ServiceEvents.Insert(0, new ServiceEvent
                        {
                            Timestamp = arg.EventRecord.TimeCreated.Value,
                            EventId = arg.EventRecord.Id,
                            Description = arg.EventRecord.FormatDescription()
                        });
                    }));
                }
                catch { }
            }

            if (!IsInitialLoading)
            {
                RefreshServiceStatus_Elapsed(null, null);
            }
        }

        private void SetUacIcon()
        {
            ElevateImage.Source = Elevate.GetUacIcon();
            if (ElevateImage.Source == null)
            {
                ElevateImage.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshServiceStatus_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (StartStopButton.IsEnabled == false
                && (StartStopButtonContent.Text.Equals(Starting)
                    || StartStopButtonContent.Text.Equals(Stopping)))
                {
                    return;
                }

                try
                {
                    TcpTriggerService.Refresh();
                    Status.Text = TcpTriggerService.Status.ToString().ToLower();

                    switch (TcpTriggerService.Status)
                    {
                        case ServiceControllerStatus.Running:
                            StartStopButtonContent.Text = StopService;
                            StartStopButton.Opacity = 1.0;
                            StartStopButton.IsEnabled = true;
                            StatusImage.Source = (DrawingImage)Application.Current.Resources["icon.check-circle"];
                            break;
                        case ServiceControllerStatus.Stopped:
                            StartStopButtonContent.Text = StartService;
                            StartStopButton.Opacity = 1.0;
                            StartStopButton.IsEnabled = true;
                            StatusImage.Source = (DrawingImage)Application.Current.Resources["icon.exclamation-circle"];
                            break;
                        default:
                            StartStopButtonContent.Text = StartService;
                            StartStopButton.Opacity = 0.7;
                            StartStopButton.IsEnabled = false;
                            StatusImage.Source = (DrawingImage)Application.Current.Resources["icon.exclamation-circle"];
                            break;
                    }
                }
                catch { }
            }));
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (StartStopButton.IsEnabled)
            {
                using (BackgroundWorker bw = new BackgroundWorker())
                {
                    bw.DoWork += new DoWorkEventHandler(Thread_StartStopService);
                    bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(Thread_StartStopServiceCompleted);

                    ElevateImage.Visibility = Visibility.Collapsed;
                    Spinner.Visibility = Visibility.Visible;
                    StartStopButton.IsEnabled = false;
                    StartStopButtonContent.Opacity = 0.8;

                    if (StartStopButtonContent.Text.Equals(StopService))
                    {
                        StartStopButtonContent.Text = Stopping;
                        bw.RunWorkerAsync("--stop-service");
                    }
                    else
                    {
                        StartStopButtonContent.Text = Starting;
                        bw.RunWorkerAsync("--restart-service");
                    }
                }
            }
        }

        private void Thread_StartStopService(object sender, DoWorkEventArgs e)
        {
            try
            {
                e.Result = EXIT_SUCCESS;
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                    Arguments = (string)e.Argument
                };
                using (Process process = Process.Start(startInfo))
                {
                    const int _maxWaitMilliseconds = 45 * 1000;
                    _ = process.WaitForExit(_maxWaitMilliseconds);
                }
            }
            catch (Win32Exception)
            {
                // The user refused elevation.
                e.Result = EXIT_CANCEL;
            }
            catch
            {
                // Something unexpected happened when trying to run the process.
                e.Result = EXIT_FAILURE;
            }
        }

        private void Thread_StartStopServiceCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            ElevateImage.Visibility = Visibility.Visible;
            Spinner.Visibility = Visibility.Collapsed;
            StartStopButton.IsEnabled = true;
            StartStopButtonContent.Opacity = 1.0;
            RefreshServiceStatus_Elapsed(null, null);
        }

        private void ServiceLog_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServiceLog.SelectedIndex < 0)
            {
                Grid.SetRowSpan(ServiceLog, 3);
                EventDetailsBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                try
                {
                    ServiceEvent entry = (ServiceEvent)ServiceLog.SelectedItem;
                    EventDetails.Text = entry.Description ?? string.Empty;
                }
                catch
                {
                    EventDetails.Text = string.Empty;
                }
                finally
                {
                    Grid.SetRowSpan(ServiceLog, 1);
                    EventDetailsBorder.Visibility = Visibility.Visible;
                }
            }
        }

        private void CloseDetails_Click(object sender, RoutedEventArgs e)
        {
            ServiceLog.SelectedIndex = -1;
            Grid.SetRowSpan(ServiceLog, 3);
            EventDetailsBorder.Visibility = Visibility.Collapsed;
        }
    }

    internal class ServiceEvent
    {
        public DateTime Timestamp { get; set; }
        public int EventId { get; set; }
        public string Description { get; set; }
        public string EventSummary
        {
            get
            {
                string summary;
                switch (EventId)
                {
                    case 90:
                        summary = "Service started";
                        break;
                    case 91:
                        summary = "Service stopped";
                        break;
                    case 100:
                        summary = "Applied user settings";
                        break;
                    case 101:
                        summary = "Network change detected";
                        break;
                    case 400:
                        summary = "Error";
                        break;
                    case 12:
                        summary = "Windows: Startup";
                        break;
                    case 13:
                        summary = "Windows: Shutdown";
                        break;
                    case 1074:
                        summary = "Windows: Shutdown initiating";
                        break;
                    case 6008:
                        summary = "Windows: Unexpected shutdown";
                        break;
                    default:
                        summary = string.Empty;
                        break;
                }
                return summary;
            }
        }
    }
}
