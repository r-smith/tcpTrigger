using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;

namespace tcpTrigger
{
    partial class tcpTrigger : ServiceBase
    {
        private List<TcpTriggerInterface> _tcpTriggerInterfaces;

        public tcpTrigger()
        {
            InitializeComponent();

            EventLog.Log = "Application";
        }

        protected override void OnStart(string[] args)
        {
            // The tcpTrigger service is starting.

            // Read and parse the tcpTrigger configuration file.
            try
            {
                Settings.Load();
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    "Failed to start the tcpTrigger service."
                    + Environment.NewLine + Environment.NewLine
                    + ex.Message,
                    Logger.EventCode.Error);
                Environment.Exit(1);
                return;
            }

            // Validate log file path.
            if (Settings.IsLogEnabled)
            {
                if (string.IsNullOrEmpty(Settings.LogPath) || !Directory.Exists(Path.GetDirectoryName(Settings.LogPath)))
                {
                    Settings.IsLogEnabled = false;
                    Logger.WriteError(
                        $"Log file path '{Settings.LogPath}' is inaccessible. Logging has been disabled. Update your tcpTrigger configuration with a valid path.",
                        Logger.EventCode.Error);
                }
            }

            // Validate external app path.
            if (Settings.IsExternalAppEnabled)
            {
                if (string.IsNullOrEmpty(Settings.ExternalAppPath))
                {
                    Logger.WriteError(
                        $"You have enabled the action to launch an external application, but no path is set. Update your tcpTrigger configuration to point to a valid executable.",
                        Logger.EventCode.Error);
                }
            }

            // Write final applied configuration to event log.
            Logger.Write(
                "The tcpTrigger service is starting."
                + Environment.NewLine + Environment.NewLine
                + Settings.DumpToString(),
                Logger.EventCode.ConfigurationApplied);

            // Retrieve network interfaces and start IP listener threads.
            // This is done on a timer so that the listener thread can automatically restart
            // if any changes to network interfaces are detected.
            var networkInterfaceInitializeTimer = new System.Timers.Timer();
            networkInterfaceInitializeTimer.Interval = TimeSpan.FromMinutes(1).TotalMilliseconds;
            networkInterfaceInitializeTimer.Elapsed += InitializeNetworkListeners_Elapsed;
            networkInterfaceInitializeTimer.Enabled = true;
            InitializeNetworkListeners_Elapsed(null, null);

            Logger.Write("The tcpTrigger service started successfully.", Logger.EventCode.ServiceStarted);
        }

        protected override void OnStop()
        {
            // The tcpTrigger service is stopping.

            // Close (and dispose) all existing listeners.
            for (int i = 0; i < _tcpTriggerInterfaces.Count; i++)
            {
                _tcpTriggerInterfaces[i].NetworkSocket.Close();
            }
            Logger.Write("The tcpTrigger service stopped successfully.", Logger.EventCode.ServiceStopped);
        }

        private void InitializeNetworkListeners_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Get network interfaces that support listening.
            List<TcpTriggerInterface> networkInterfaces = TcpTriggerInterface.GetInterfaces();

            // Check if tcpTrigger service is already running (_tcpTriggerInterfaces won't be null).
            if (_tcpTriggerInterfaces != null)
            {
                // Compare list of network interfaces to previously stored list of network interfaces.
                if (networkInterfaces.SequenceEqual(_tcpTriggerInterfaces, new TcpTriggerInterfaceComparer()))
                {
                    // Lists are equal. Nothing to do; quit.
                    return;
                }
                else
                {
                    // Network interfaces have changed. Record to event log that a change was detected.
                    Logger.Write(
                        "tcpTrigger detected a change in the network interface configuration in Windows. Restarting listeners.",
                        Logger.EventCode.NetworkChangeDetected);

                    // Close (and dispose) all existing listeners.
                    for (int i = 0; i < _tcpTriggerInterfaces.Count; i++)
                    {
                        _tcpTriggerInterfaces[i].NetworkSocket.Close();
                    }
                }
            }

            // Clone retrieved list of network interfaces to global var.
            _tcpTriggerInterfaces = new List<TcpTriggerInterface>(networkInterfaces);

            // Start listeners.
            StartIpListeners();
        }

        private void StartIpListeners()
        {
            // Log network interfaces.
            StringBuilder sb = new StringBuilder();
            foreach (TcpTriggerInterface adapter in _tcpTriggerInterfaces)
            {
                sb.AppendLine($"{adapter.IP} => {adapter.Description}");
            }
            Logger.Write(
                "The tcpTrigger service is monitoring the following network interfaces:"
                + Environment.NewLine + Environment.NewLine
                + sb.ToString(),
                Logger.EventCode.NetworkInterfaces);

            // Start listeners.
            for (int i = 0; i < _tcpTriggerInterfaces.Count; i++)
            {
                RawSocketListener(_tcpTriggerInterfaces[i]);
            }
        }
    }
}