using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.ServiceProcess;
using System.Text;

namespace tcpTrigger
{
    internal partial class TcpTriggerService : ServiceBase
    {
        // Custom service command to reload the configuration file. The GUI uses this value
        // to trigger a config reload.
        public const int ReloadConfigurationCommand = 128;

        // A running listener for each interface being monitored.
        private List<PacketListener> _listeners = new List<PacketListener>();

        // Stores all network interfaces on the system. Used to detect network changes.
        private List<TcpTriggerInterface> _detectedInterfaces;

        // Timer that checks for network changes and restarts listeners when one is found.
        private System.Timers.Timer _networkCheckTimer;

        // How often the network check runs, and how long after Windows reports an IP address change.
        private static readonly TimeSpan s_networkCheckInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan s_addressChangeDelay = TimeSpan.FromSeconds(5);

        // Set when Windows reports an IP address change, so the next network check restarts the listeners
        // even if the interfaces look the same.
        private bool _hasAddressChanged;

        // Runs the notifications for detections on a background thread.
        private DetectionQueue _detectionQueue;

        // Held while starting monitoring, stopping monitoring, or checking for network changes
        // so they never run at the same time.
        private readonly object _monitoringLock = new object();

        // False once monitoring stops, so a network check that was already scheduled does nothing.
        private bool _isMonitoring;

        public TcpTriggerService()
        {
            InitializeComponent();

            EventLog.Log = "tcpTrigger";
        }

        // Runs the service in a console window for debugging.
        internal void RunInConsole()
        {
            try
            {
                OnStart(null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"tcpTrigger failed to start: {ex.Message}");
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine("tcpTrigger is running. Press Enter to stop.");
            Console.ReadLine();
            OnStop();
        }

        // Called by Windows when the tcpTrigger service starts.
        protected override void OnStart(string[] args)
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Logger.Write(
                "The tcpTrigger service is starting."
                + $"{Environment.NewLine}{Environment.NewLine}Version: {version}",
                Logger.EventCode.ServiceStarting);

            try
            {
                LoadConfiguration();
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    "The tcpTrigger service failed to start because its configuration couldn't be read. "
                    + "Check that the configuration file exists and is valid."
                    + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    Logger.EventCode.StartFailed);

                // Rethrow so Windows reports that the service failed to start,
                // rather than seeing the process terminated unexpectedly.
                throw;
            }

            StartMonitoring();
        }

        // Called by Windows when the tcpTrigger service stops.
        protected override void OnStop()
        {
            StopMonitoring();

            // ExitCode is only set when the service stops itself because of an error.
            if (ExitCode != 0)
            {
                Logger.WriteError("The tcpTrigger service stopped because of an error.", Logger.EventCode.ServiceStoppedOnError);
            }
            else
            {
                Logger.Write("The tcpTrigger service stopped successfully.", Logger.EventCode.ServiceStopped);
            }
        }

        // Called by Windows when a custom command is sent to the tcpTrigger service.
        protected override void OnCustomCommand(int command)
        {
            if (command == ReloadConfigurationCommand)
            {
                ReloadConfiguration();
            }
        }

        // Stops monitoring, loads the configuration file again, and restarts monitoring with the new settings.
        // If that fails, the service stops.
        private void ReloadConfiguration()
        {
            // Windows runs each custom command on its own thread, so hold the lock for the reload.
            lock (_monitoringLock)
            {
                Logger.Write("The tcpTrigger service is reloading its configuration.", Logger.EventCode.ConfigurationReloading);

                try
                {
                    StopMonitoring();
                    LoadConfiguration();
                    StartMonitoring();
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        "The tcpTrigger configuration failed to reload, so the service is stopping. "
                        + "Fix the configuration file, then start the service."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        Logger.EventCode.ReloadFailed);

                    // Stop with an error code, so Windows records the stop as a failure.
                    const int ErrorExceptionInService = 1064;
                    ExitCode = ErrorExceptionInService;
                    Stop();
                }
            }
        }

        // Reads the configuration file, corrects any settings that can't be used, and records
        // the applied configuration to the event log. Throws if the file can't be read or parsed.
        private void LoadConfiguration()
        {
            Settings.Load();

            // Validate notifications. Notifications that can't be used are removed.
            List<Notification> usableNotifications = new List<Notification>();
            foreach (Notification notification in Settings.Notifications)
            {
                if (IsNotificationUsable(notification))
                {
                    usableNotifications.Add(notification);
                }
            }
            Settings.Notifications = usableNotifications;

            // Validate ignore rules. Rules that can't be used are removed.
            foreach (IgnoreRule rule in Settings.IgnoreRules)
            {
                string ruleName = string.IsNullOrEmpty(rule.Name) ? "An unnamed ignore rule" : $"The ignore rule '{rule.Name}'";
                string invalidEntries = string.Join(", ", rule.InvalidEntries);

                if (!rule.IsUsable)
                {
                    string message = $"{ruleName} has been disabled because it needs at least one valid source, destination, and service.";
                    if (rule.InvalidEntries.Count > 0)
                    {
                        message += $" These entries aren't valid: {invalidEntries}.";
                    }
                    Logger.WriteWarning(
                        message + " Update your tcpTrigger configuration to fix the rule.",
                        Logger.EventCode.IgnoreRuleInvalid);
                }
                else if (rule.InvalidEntries.Count > 0)
                {
                    Logger.WriteWarning(
                        $"{ruleName} has entries that aren't valid, so they were skipped: {invalidEntries}. "
                        + "Update your tcpTrigger configuration to fix or remove them.",
                        Logger.EventCode.IgnoreRuleInvalid);
                }
            }
            Settings.IgnoreRules = Settings.IgnoreRules.Where(rule => rule.IsUsable).ToList();

            // Write final applied configuration to event log.
            Logger.Write(
                "The tcpTrigger service applied the following configuration:"
                + $"{Environment.NewLine}{Environment.NewLine}"
                + Settings.DumpToString(),
                Logger.EventCode.ConfigurationApplied);
        }

        // Returns false and writes a warning if the notification is missing a setting it needs.
        private static bool IsNotificationUsable(Notification notification)
        {
            string name = string.IsNullOrEmpty(notification.Name)
                ? "An unnamed notification"
                : $"The notification '{notification.Name}'";

            switch (notification)
            {
                case NotificationLogFile logFile:
                    if (string.IsNullOrEmpty(logFile.Path) || !Directory.Exists(Path.GetDirectoryName(logFile.Path)))
                    {
                        string reason = string.IsNullOrEmpty(logFile.Path)
                            ? "no log path is set"
                            : $"the folder for the log path '{logFile.Path}' doesn't exist";
                        Logger.WriteWarning(
                            $"{name} has been disabled because {reason}. Update your tcpTrigger configuration with a valid path.",
                            Logger.EventCode.LogPathInaccessible);
                        return false;
                    }
                    return true;
                case NotificationCommand command:
                    if (string.IsNullOrEmpty(command.Path))
                    {
                        Logger.WriteWarning(
                            $"{name} has been disabled because no command path is set. "
                            + "Update your tcpTrigger configuration with the command to run.",
                            Logger.EventCode.CommandPathMissing);
                        return false;
                    }
                    return true;
                case NotificationEmail email:
                    List<string> missing = new List<string>();
                    if (email.Recipients.Count == 0)
                    {
                        missing.Add("recipient address");
                    }
                    if (string.IsNullOrWhiteSpace(email.FromAddress))
                    {
                        missing.Add("sender address");
                    }
                    if (email.SendMethod == EmailSendMethod.Microsoft365)
                    {
                        if (string.IsNullOrWhiteSpace(email.TenantId))
                        {
                            missing.Add("Microsoft 365 tenant ID");
                        }
                        if (string.IsNullOrWhiteSpace(email.ClientId))
                        {
                            missing.Add("Microsoft 365 client ID");
                        }
                        if (string.IsNullOrWhiteSpace(email.ClientSecret))
                        {
                            missing.Add("Microsoft 365 client secret");
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(email.SmtpServer))
                    {
                        missing.Add("mail server");
                    }
                    if (string.IsNullOrWhiteSpace(email.Subject))
                    {
                        missing.Add("message subject");
                    }

                    if (missing.Count > 0)
                    {
                        Logger.WriteWarning(
                            $"{name} has been disabled because these settings are missing: {string.Join(", ", missing)}. "
                            + "Update your tcpTrigger configuration with the missing settings.",
                            Logger.EventCode.EmailSettingsIncomplete);
                        return false;
                    }
                    return true;
                default:
                    return true;
            }
        }

        // Starts the detection queue thread, the network listeners, and the network change detection.
        private void StartMonitoring()
        {
            lock (_monitoringLock)
            {
                _isMonitoring = true;

                // Start the detection queue thread.
                _detectionQueue = new DetectionQueue();
                _detectionQueue.Start();

                // Start the listeners now, then check every minute for network changes
                // and restart the listeners when the interfaces change.
                _networkCheckTimer = new System.Timers.Timer
                {
                    Interval = s_networkCheckInterval.TotalMilliseconds
                };
                _networkCheckTimer.Elapsed += NetworkCheckTimer_Elapsed;
                _networkCheckTimer.Enabled = true;
                NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
                CheckNetworkInterfaces();
            }
        }

        // Stops the network change detection, the network listeners, and the detection queue thread.
        private void StopMonitoring()
        {
            lock (_monitoringLock)
            {
                // Exit if already stopped (such as after a failed reload).
                if (!_isMonitoring)
                {
                    return;
                }

                _isMonitoring = false;

                NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
                _networkCheckTimer.Stop();
                _networkCheckTimer.Dispose();

                // Stop all existing listeners.
                foreach (PacketListener listener in _listeners)
                {
                    listener.Stop();
                }

                // Let detections already queued finish before stopping.
                _detectionQueue.Stop();

                // Forget the detected interfaces, so the next start begins with a fresh network check.
                _detectedInterfaces = null;
            }
        }

        // Checks for network changes every minute.
        private void NetworkCheckTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            CheckNetworkInterfaces();
        }

        // Runs the network check a few seconds after Windows reports an IP address change.
        // Each new report restarts the wait, so the check runs once the changes have settled.
        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e)
        {
            lock (_monitoringLock)
            {
                if (!_isMonitoring)
                {
                    return;
                }

                _hasAddressChanged = true;
                _networkCheckTimer.Interval = s_addressChangeDelay.TotalMilliseconds;
            }
        }

        // Runs on startup, every minute, and a few seconds after an IP address change. Restarts all listeners
        // when the network interfaces have changed, an IP address has changed, or a listener has failed.
        private void CheckNetworkInterfaces()
        {
            lock (_monitoringLock)
            {
                if (!_isMonitoring)
                {
                    return;
                }

                // Go back to checking every minute, in case an IP address change made this check run early.
                _networkCheckTimer.Interval = s_networkCheckInterval.TotalMilliseconds;

                try
                {
                    // Get network interfaces that support listening.
                    List<TcpTriggerInterface> networkInterfaces = TcpTriggerInterface.GetInterfaces();

                    // On startup there are no detected interfaces yet, which counts as a change.
                    TcpTriggerInterfaceComparer comparer = new TcpTriggerInterfaceComparer();
                    bool hasInterfacesChanged = _detectedInterfaces == null
                        || !networkInterfaces.SequenceEqual(_detectedInterfaces, comparer);
                    bool hasListenerFailed = _listeners.Exists(listener => listener.HasFailed);

                    // An IP address change also restarts the listeners, even if the interfaces look the same.
                    // When an adapter is disabled and enabled again, its listener can stop receiving without an error.
                    if (!hasInterfacesChanged && !hasListenerFailed && !_hasAddressChanged)
                    {
                        return;
                    }
                    _hasAddressChanged = false;

                    // Record the change to the event log, except on startup.
                    if (hasInterfacesChanged && _detectedInterfaces != null)
                    {
                        List<TcpTriggerInterface> added = networkInterfaces.Except(_detectedInterfaces, comparer).ToList();
                        List<TcpTriggerInterface> removed = _detectedInterfaces.Except(networkInterfaces, comparer).ToList();
                        Logger.Write(
                            "tcpTrigger detected a network change and is restarting monitoring. "
                            + $"Event {(int)Logger.EventCode.NetworkInterfaces} will list the interfaces being monitored."
                            + $"{Environment.NewLine}{Environment.NewLine}"
                            + $"# Added{Environment.NewLine}"
                            + (added.Count > 0 ? NetworkInterfacesToString(added) : $"None{Environment.NewLine}")
                            + $"{Environment.NewLine}"
                            + $"# Removed{Environment.NewLine}"
                            + (removed.Count > 0 ? NetworkInterfacesToString(removed) : "None"),
                            Logger.EventCode.NetworkChangeDetected);
                    }

                    // Restart all listeners. If only an IP address changed, the interfaces being monitored aren't logged again.
                    foreach (PacketListener listener in _listeners)
                    {
                        listener.Stop();
                    }
                    _detectedInterfaces = networkInterfaces;
                    StartListeners(networkInterfaces, logInterfaces: hasInterfacesChanged || hasListenerFailed);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        "tcpTrigger failed to check the network configuration. The check will be retried shortly."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        Logger.EventCode.NetworkCheckFailed);
                }
            }
        }

        // Starts a listener on each network interface. If logInterfaces is true, logs which interfaces are being monitored.
        private void StartListeners(List<TcpTriggerInterface> interfaces, bool logInterfaces)
        {
            // Start a listener on each interface. Keep only the listeners that started successfully.
            List<PacketListener> listeners = new List<PacketListener>();
            foreach (TcpTriggerInterface ipInterface in interfaces)
            {
                PacketListener listener = TryStartListener(ipInterface);
                if (listener != null)
                {
                    listeners.Add(listener);
                }
            }
            _listeners = listeners;

            // Listeners have started. Network monitoring is now active.
            // Record which adapters are being monitored to event log.
            // Record an error if no adapters are monitored.
            if (_listeners.Count > 0 && logInterfaces)
            {
                Logger.Write(
                    "The tcpTrigger service is monitoring the following network interfaces:"
                    + $"{Environment.NewLine}{Environment.NewLine}"
                    + NetworkInterfacesToString(_listeners.ConvertAll(listener => listener.Interface)),
                    Logger.EventCode.NetworkInterfaces);
            }
            else if (_listeners.Count == 0)
            {
                Logger.WriteError(
                    "The tcpTrigger service is running, but it isn't monitoring any network interfaces, so nothing will be detected.",
                    Logger.EventCode.NoInterfacesMonitored);
            }
        }

        // Starts a listener on the interface. Returns null and logs an error if it can't start.
        private PacketListener TryStartListener(TcpTriggerInterface ipInterface)
        {
            PacketListener listener = new PacketListener(ipInterface, _detectionQueue);
            try
            {
                listener.Start();
                return listener;
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    $"tcpTrigger failed to start monitoring network interface {ipInterface.Name} ({ipInterface.IP}). "
                    + "This interface is not being monitored."
                    + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    Logger.EventCode.InterfaceBindFailed);
                listener.Stop();
                return null;
            }
        }

        // Returns one line per interface, with its IP address and description.
        private string NetworkInterfacesToString(List<TcpTriggerInterface> interfaces)
        {
            StringBuilder sb = new StringBuilder();
            foreach (TcpTriggerInterface adapter in interfaces)
            {
                sb.AppendLine($"{adapter.Name}: {adapter.IP} | {adapter.Description}");
            }
            return sb.ToString();
        }
    }
}
