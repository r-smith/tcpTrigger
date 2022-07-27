using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

            // Retrieve network interfaces and start IP listener threads.
            // This is done on a timer so that the listener thread can automatically restart
            // if any changes to network interfaces are detected.
            var networkInterfaceInitializeTimer = new System.Timers.Timer();
            networkInterfaceInitializeTimer.Interval = TimeSpan.FromMinutes(1).TotalMilliseconds;
            networkInterfaceInitializeTimer.Elapsed += InitializeNetworkListeners_Elapsed;
            networkInterfaceInitializeTimer.Enabled = true;
            InitializeNetworkListeners_Elapsed(null, null);

            Logger.Write("tcpTrigger service started successfully.", Logger.EventCode.ServiceStarted);
        }

        protected override void OnStop()
        {
            // The tcpTrigger service is stopping.

            // Close (and dispose) all existing listeners.
            for (int i = 0; i < _tcpTriggerInterfaces.Count; i++)
            {
                _tcpTriggerInterfaces[i].NetworkSocket.Close();
            }
            Logger.Write("tcpTrigger service stopped successfully.", Logger.EventCode.ServiceStopped);
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
            var sb = new StringBuilder();

            // Log interfaces.
            sb.AppendLine($"Using configuration file: '{Settings.Path}'");
            sb.AppendLine();
            sb.AppendLine("# Network interfaces");
            foreach (TcpTriggerInterface ipInterface in _tcpTriggerInterfaces)
            {
                sb.AppendLine($"Listening on: {ipInterface.IP} ({ipInterface.Description})");
            }
            if (Settings.ExcludedNetworkInterfaces.Count > 0)
            {
                foreach (string guid in Settings.ExcludedNetworkInterfaces)
                {
                    sb.AppendLine("Exclude network interface: " + guid);
                }
            }

            // Log monitoring configuration.
            sb.AppendLine();
            sb.AppendLine("# Rules");
            sb.AppendLine("Detect incoming ICMP: " + (Settings.IsMonitorIcmpEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Detect incoming TCP: " + (Settings.IsMonitorTcpEnabled ? "Enabled" : "Disabled"));
            if (Settings.IsMonitorTcpEnabled)
            {
                sb.AppendLine($"Including TCP port(s): {Settings.TcpPortsToIncludeAsString}");
                sb.AppendLine($"Excluding TCP port(s): {Settings.TcpPortsToExcludeAsString}");
            }
            sb.AppendLine("Detect incoming UDP: " + (Settings.IsMonitorUdpEnabled ? "Enabled" : "Disabled"));
            if (Settings.IsMonitorUdpEnabled)
            {
                sb.AppendLine($"Including UDP port(s): {Settings.UdpPortsToIncludeAsString}");
                sb.AppendLine($"Excluding UDP port(s): {Settings.UdpPortsToExcludeAsString}");
            }
            sb.AppendLine("Detect rogue DHCP: " + (Settings.IsMonitorDhcpEnabled ? "Enabled" : "Disabled"));

            // Log DHCP server ignore list.
            if (Settings.IgnoredDhcpServers.Count > 0)
            {
                foreach (IPAddress ip in Settings.IgnoredDhcpServers)
                {
                    sb.AppendLine("Ignore DHCP server: " + ip.ToString());
                }
            }

            // Log endpoint ignore list.
            if (Settings.IgnoredEndpoints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# Whitelist");
                foreach (KeyValuePair<IPAddress, HashSet<int>> pair in Settings.IgnoredEndpoints)
                {
                    foreach (int port in pair.Value)
                    {
                        sb.Append($"Ignore IP: {pair.Key}");
                        if (port == Settings.IgnoreIcmp)
                            sb.AppendLine(":icmp");
                        else if (port == Settings.IgnoreAll)
                            sb.AppendLine();
                        else
                            sb.AppendLine($":{port}");
                    }
                }
            }

            // Log enabled actions and settings.
            sb.AppendLine();
            sb.AppendLine("# Actions");
            sb.AppendLine("Write to text log: " + (Settings.IsLogEnabled ? "Enabled" : "Disabled"));
            if (Settings.IsLogEnabled)
                sb.AppendLine($"Log path: '{Settings.LogPath}'");
            sb.AppendLine("Write to Windows event log: " + (Settings.IsEventLogEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Email notifications: " + (Settings.IsEmailNotificationEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Launch external application: " + (Settings.IsExternalAppEnabled ? "Enabled" : "Disabled"));
            if (Settings.IsExternalAppEnabled)
            {
                sb.AppendLine($"Launch app path: '{Settings.ExternalAppPath}'");
                sb.AppendLine($"Launch app args: '{Settings.ExternalAppArguments}'");
            }

            // Log email settings.
            if (Settings.IsEmailNotificationEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("# Email");
                sb.AppendLine($"Server: {Settings.EmailServer}");
                sb.AppendLine($"Port: {Settings.EmailServerPort}");
                sb.AppendLine($"SSL / TLS: {(Settings.IsEmailTlsEnabled ? "Enabled" : "Disabled")}");
                sb.AppendLine("Use authentication? " + (Settings.IsEmailAuthRequired ? "Yes" : "No"));
                sb.AppendLine("Recipient(s): " + string.Join(", ", Settings.EmailRecipients.ToArray()));
                sb.AppendLine($"Sender address: {Settings.EmailSender}");
                sb.AppendLine($"Sender display name: {Settings.EmailSenderDisplayName}");
                sb.AppendLine($"Message subject: {Settings.EmailSubject}");
                sb.AppendLine($"Rate limit: " + (Settings.EmailRateLimitSeconds > 0 ? Settings.EmailRateLimitSeconds.ToString() + " seconds" : "Disabled"));
                sb.AppendLine($"Buffer: " + (Settings.EmailBufferSeconds > 0 ? Settings.EmailBufferSeconds.ToString() + " seconds" : "Disabled"));
            }

            // Write to event log.
            Logger.Write(sb.ToString(), Logger.EventCode.ConfigurationApplied);

            // Start listeners.
            for (int i = 0; i < _tcpTriggerInterfaces.Count; i++)
            {
                RawSocketListener(_tcpTriggerInterfaces[i]);
            }
        }
    }
}