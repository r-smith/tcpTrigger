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
            // The tcpTrigger Windows service is starting.

            // Locate and read tcpTrigger configuration file.
            if (Settings.Load() == false)
            {
                // An error was encountered while reading the configuration file. Stop the service and quit.
                Environment.Exit(1);
                return;
            }

            // Validate log file path. Disable logging if inaccessible.
            if (Settings.IsLogEnabled)
            {
                if (string.IsNullOrEmpty(Settings.LogPath) || !Directory.Exists(Path.GetDirectoryName(Settings.LogPath)))
                {
                    Settings.IsLogEnabled = false;
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Log file path '{Settings.LogPath}' is inaccessible. Logging has been disabled. Update your tcpTrigger configuration with a valid path.",
                        EventLogEntryType.Error,
                        401);
                }
            }

            // If enabled, validate external app path and disable if not found.
            if (Settings.IsExternalAppEnabled)
            {
                if (string.IsNullOrEmpty(Settings.ExternalAppPath) || !File.Exists(Settings.ExternalAppPath))
                {
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"The specified external application path '{Settings.ExternalAppPath}' was not found. Update your tcpTrigger configuration to point to a valid executable.",
                        EventLogEntryType.Error,
                        401);
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
        }

        protected override void OnStop()
        {
            // The tcpTrigger Windows service is stopping.

            // Close (and dispose) all existing listeners.
            for (int i = 0; i < _tcpTriggerInterfaces.Count; i++)
            {
                _tcpTriggerInterfaces[i].NetworkSocket.Close();
            }
        }

        private void InitializeNetworkListeners_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var networkInterfaces = new List<TcpTriggerInterface>();

            // Enumerate network interfaces. Determine and record which interfaces to listen on.
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (Settings.ExcludedNetworkInterfaces.Contains(networkInterface.Id))
                {
                    continue;
                }
                if (networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    networkInterface.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            networkInterfaces.Add(
                                new TcpTriggerInterface(
                                    address: address.Address,
                                    description: networkInterface.Description,
                                    macAddress: networkInterface.GetPhysicalAddress(),
                                    guid: networkInterface.Id)
                                );
                        }
                    }
                }
            }

            // Check if tcpTrigger service is already running (_tcpTriggerInterfaces won't be null).
            if (_tcpTriggerInterfaces != null)
            {
                // Compares list of network interfaces to previously stored list of network interfaces.
                if (networkInterfaces.SequenceEqual(_tcpTriggerInterfaces, new TcpTriggerInterfaceComparer()))
                {
                    // Lists are equal. Nothing to do; quit.
                    return;
                }
                else
                {
                    // Network interfaces have changed. Record to event log that a change was detected.
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        "tcpTrigger detected changes to network interfaces in Windows. Restarting listeners.",
                        EventLogEntryType.Information,
                        101);

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
            sb.AppendLine("[Starting listeners]");
            foreach (TcpTriggerInterface ipInterface in _tcpTriggerInterfaces)
            {
                sb.AppendLine($"Listening on: {ipInterface.Description} [{ipInterface.IP}]");
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
            sb.AppendLine("[Monitoring configuration]");
            sb.AppendLine("Detect incoming ICMP pings: " + (Settings.IsMonitorIcmpEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Detect incoming TCP connections: " + (Settings.IsMonitorTcpEnabled ? "Enabled" : "Disabled"));
            if (Settings.IsMonitorTcpEnabled)
            {
                sb.AppendLine($"[+] Including TCP port(s): {Settings.TcpPortsToIncludeAsString}");
                sb.AppendLine($"[+] Excluding TCP port(s): {Settings.TcpPortsToExcludeAsString}");
            }
            sb.AppendLine("Detect rogue DHCP servers: " + (Settings.IsMonitorDhcpEnabled ? "Enabled" : "Disabled"));

            // Log DHCP server ignore list.
            if (Settings.IgnoredDhcpServers.Count > 0)
            {
                foreach (IPAddress ip in Settings.IgnoredDhcpServers)
                {
                    sb.AppendLine("[+] Ignore DHCP server: " + ip.ToString());
                }
            }

            // Log endpoint ignore list.
            if (Settings.IgnoredEndpoints.Count > 0)
            {
                foreach (IPAddress ip in Settings.IgnoredEndpoints)
                {
                    sb.AppendLine("[+] Ignore source IP: " + ip.ToString());
                }
            }

            // Log enabled actions and settings.
            sb.AppendLine();
            sb.AppendLine("[Actions]");
            sb.AppendLine("Write to text log: " + (Settings.IsLogEnabled ? "Enabled" : "Disabled"));
            if (Settings.IsLogEnabled)
                sb.AppendLine($"[+] Log path: {Settings.LogPath}");
            sb.AppendLine("Write to Windows event log: " + (Settings.IsEventLogEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Email notifications: " + (Settings.IsEmailNotificationEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Launch external application: " + (Settings.IsExternalAppEnabled ? "Enabled" : "Disabled"));
            if (Settings.IsExternalAppEnabled)
            {
                sb.AppendLine($"[+] App path: {Settings.ExternalAppPath}");
                sb.AppendLine($"[+] App args: {Settings.ExternalAppArguments}");
            }

            // Log email configuration.
            if (Settings.IsEmailNotificationEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("[Email configuration]");
                sb.AppendLine($"Server: {Settings.EmailServer}");
                sb.AppendLine($"Port: {Settings.EmailServerPort}");
                sb.AppendLine("Use authentication? " + (Settings.IsEmailAuthRequired ? "Yes" : "No"));
                sb.AppendLine("Recipient(s): " + string.Join(", ", Settings.EmailRecipients.ToArray()));
                sb.AppendLine($"Sender address: {Settings.EmailSender}");
                sb.AppendLine($"Sender display name: {Settings.EmailSenderDisplayName}");
            }

            // Write to event log.
            EventLog.WriteEntry(
                "tcpTrigger",
                sb.ToString(),
                EventLogEntryType.Information,
                100);

            // Start listeners.
            for (int i = 0; i < _tcpTriggerInterfaces.Count; i++)
            {
                RawSocketListener(_tcpTriggerInterfaces[i]);
            }
        }
    }
}