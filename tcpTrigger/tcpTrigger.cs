using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace tcpTrigger
{
    partial class tcpTrigger : ServiceBase
    {
        private List<TcpTriggerInterface> _tcpTriggerInterfaces;
        private System.Timers.Timer _namePoisionDetectionTimer;
        private ushort _netbiosTransactionId = 0x8000;
        private bool _isNamePoisonDetectionInProgress = false;
        private int _namePoisonTransactionIdResponse = int.MinValue;

        public tcpTrigger()
        {
            InitializeComponent();

            EventLog.Log = "Application";
        }

        protected override void OnStart(string[] args)
        {
            // The tcpTrigger Windows service is starting.

            // Locate and read tcpTrigger configuration file.
            if (Configuration.Load() == false)
            {
                // An error was encountered while reading the configuration file. Stop the service and quit.
                Environment.Exit(1);
                return;
            }

            // Validate log file path. Disable logging if inaccessible.
            if (Configuration.IsLogEnabled)
            {
                if (string.IsNullOrEmpty(Configuration.LogPath) || !Directory.Exists(Path.GetDirectoryName(Configuration.LogPath)))
                {
                    Configuration.IsLogEnabled = false;
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Log file path '{Configuration.LogPath}' is inaccessible. Logging has been disabled. Update your tcpTrigger configuration with a valid path.",
                        EventLogEntryType.Error,
                        401);
                }
            }

            // If enabled, start name poison detection.
            if (Configuration.IsMonitorPoisonEnabled)
            {
                _namePoisionDetectionTimer = new System.Timers.Timer();
                _namePoisionDetectionTimer.Interval = TimeSpan.FromMinutes(4).TotalMilliseconds;
                _namePoisionDetectionTimer.Elapsed += NamePoisionDetectionTimer_Elapsed;
                _namePoisionDetectionTimer.Enabled = true;
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

            // Stop and dispose timers and threads.
            if (_namePoisionDetectionTimer != null)
            {
                _namePoisionDetectionTimer.Enabled = false;
                _namePoisionDetectionTimer.Dispose();
                _namePoisionDetectionTimer = null;
            }

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
                if (Configuration.ExcludedNetworkInterfaces.Contains(networkInterface.Id))
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
                                    subnetMask: address.IPv4Mask,
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
            foreach (TcpTriggerInterface ipInterface in _tcpTriggerInterfaces)
            {
                sb.AppendLine($"Listening on interface: {ipInterface.IP} --> [{ipInterface.MacAddressAsString}] --> {ipInterface.Guid}");
            }
            if (Configuration.ExcludedNetworkInterfaces.Count > 0)
            {
                foreach (string guid in Configuration.ExcludedNetworkInterfaces)
                {
                    sb.AppendLine("Exclude network interface: " + guid);
                }
            }

            // Log monitoring configuration.
            sb.AppendLine();
            sb.AppendLine("Monitor TCP: " + (Configuration.IsMonitorTcpEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Monitor ICMP: " + (Configuration.IsMonitorIcmpEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Monitor name poison: " + (Configuration.IsMonitorPoisonEnabled ? "Enabled" : "Disabled"));
            sb.AppendLine("Monitor rogue DHCP: " + (Configuration.IsMonitorDhcpEnabled ? "Enabled" : "Disabled"));
            if (Configuration.IsMonitorTcpEnabled) sb.AppendLine($"Monitoring TCP port(s): {Configuration.TcpPortsToMonitorAsString}");

            // Log endpoint ignore list.
            if (Configuration.IgnoredEndpoints.Count > 0)
            {
                foreach (IPAddress ip in Configuration.IgnoredEndpoints)
                {
                    sb.AppendLine("Ignore source IP: " + ip.ToString());
                }
            }

            // Log DHCP server ignore list.
            if (Configuration.IgnoredDhcpServers.Count > 0)
            {
                foreach (IPAddress ip in Configuration.IgnoredDhcpServers)
                {
                    sb.AppendLine("Ignore DHCP server: " + ip.ToString());
                }
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

        private void RawSocketListener(TcpTriggerInterface ipInterface)
        {
            // Buffer for incoming data.
            byte[] buffer = new byte[512];

            // Bind socket to IP endpoint.
            ipInterface.NetworkSocket.Bind(new IPEndPoint(ipInterface.IP, 0));
            ipInterface.NetworkSocket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), null);

            // Callback method for processing captured packets.
            Action<IAsyncResult> OnReceive = null;
            OnReceive = (ar) =>
            {
                // Packet received.  Extract header information and determine if the packet matches our trigger rules.
                var packetHeader = new PacketHeader(buffer);

                if (DoesPacketMatchPingRequest(packetHeader, ipInterface.IP))
                    packetHeader.MatchType = PacketMatch.PingRequest;
                else if (DoesPacketMatchMonitoredPort(packetHeader, ipInterface.IP))
                    packetHeader.MatchType = PacketMatch.TcpConnect;
                else if (DoesPacketMatchNamePoison(packetHeader, ipInterface.IP))
                {
                    // Ensure at least two unique responses.
                    if (_namePoisonTransactionIdResponse < 0)
                    {
                        _namePoisonTransactionIdResponse = packetHeader.NetbiosTransactionId;
                    }
                    else if (_namePoisonTransactionIdResponse != packetHeader.NetbiosTransactionId)
                    {
                        packetHeader.MatchType = PacketMatch.NamePoison;
                    }
                }
                else if (DoesPacketMatchDhcpServer(packetHeader, ipInterface.IP))
                {
                    // If no DHCP servers are specified by the user, we will do automatic detection.
                    // Auto rogue DHCP detection alerts if more than one DHCP server is discovered.
                    if (Configuration.IgnoredDhcpServers.Count == 0)
                    {
                        if (!ipInterface.DiscoveredDhcpServerList.Contains(packetHeader.DhcpServerAddress))
                        {
                            packetHeader.DestinationIP = ipInterface.IP;
                            packetHeader.SourceIP = packetHeader.DhcpServerAddress;
                            ipInterface.DiscoveredDhcpServerList.Add(packetHeader.DhcpServerAddress);
                            if (ipInterface.DiscoveredDhcpServerList.Count > 1)
                                packetHeader.MatchType = PacketMatch.RogueDhcp;
                        }
                    }
                    else if (!Configuration.IgnoredDhcpServers.Contains(packetHeader.DhcpServerAddress) &&
                        !ipInterface.DiscoveredDhcpServerList.Contains(packetHeader.DhcpServerAddress))
                    {
                        packetHeader.DestinationIP = ipInterface.IP;
                        packetHeader.SourceIP = packetHeader.DhcpServerAddress;
                        ipInterface.DiscoveredDhcpServerList.Add(packetHeader.DhcpServerAddress);
                        packetHeader.MatchType = PacketMatch.RogueDhcp;
                    }
                }

                // Process actions.
                if (packetHeader.MatchType != PacketMatch.None)
                {
                    packetHeader.DestinationMac = ipInterface.MacAddress;

                    if (Configuration.IsLogEnabled)
                        WriteLog(packetHeader);
                    if (Configuration.IsEventLogEnabled)
                        WriteEventLog(packetHeader);

                    if (!Configuration.IgnoredEndpoints.Contains(packetHeader.SourceIP))
                    {
                        ipInterface.RateLimitDictionaryCleanup(Configuration.ActionRateLimitMinutes);

                        if (!ipInterface.RateLimitDictionary.ContainsKey(packetHeader.SourceIP) || Configuration.ActionRateLimitMinutes <= 0)
                        {
                            if (Configuration.ActionRateLimitMinutes > 0)
                                ipInterface.RateLimitDictionary.Add(packetHeader.SourceIP, DateTime.Now);

                            if (Configuration.IsExternalAppEnabled) LaunchApplication(packetHeader);
                            if (Configuration.IsEmailNotificationEnabled) SendEmail(packetHeader);
                        }
                    }
                }
                // Reset buffer and continue listening for new data.
                buffer = new byte[512];
                try
                {
                    ipInterface.NetworkSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), null);
                }
                catch (ObjectDisposedException)
                {
                    // Listener has been disposed by calling thread.
                    // This is expected behavior for cleanup. Do nothing.
                }
            };

            // Begin listening for data.
            ipInterface.NetworkSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), null);
        }

        private bool DoesPacketMatchPingRequest(PacketHeader header, IPAddress ip)
        {
            if (Configuration.IsMonitorIcmpEnabled &&
                header.ProtocolType == Protocol.ICMP &&
                header.DestinationIP.Equals(ip) &&
                header.IcmpType == 8)
            {
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchMonitoredPort(PacketHeader header, IPAddress ip)
        {
            if (Configuration.IsMonitorTcpEnabled &&
                header.ProtocolType == Protocol.TCP &&
                header.TcpFlags == 0x2 &&
                header.DestinationIP.Equals(ip) &&
                (Configuration.TcpPortsToMonitor.Contains(header.DestinationPort) ||
                Configuration.TcpPortsToMonitor[0] == 0))
            {
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchNamePoison(PacketHeader header, IPAddress ip)
        {
            if (Configuration.IsMonitorPoisonEnabled &&
                _isNamePoisonDetectionInProgress &&
                header.IsNameQueryResponse &&
                header.DestinationIP.Equals(ip) &&
                header.NetbiosTransactionId >= _netbiosTransactionId &&
                header.NetbiosTransactionId <= _netbiosTransactionId + 3)
            {
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchDhcpServer(PacketHeader header, IPAddress ip)
        {
            if (Configuration.IsMonitorDhcpEnabled &&
                header.DhcpServerAddress != null)
            {
                return true;
            }

            return false;
        }

        private void WriteLog(PacketHeader packetHeader)
        {
            try
            {
                using (StreamWriter outputFile = new StreamWriter(Configuration.LogPath, true))
                {
                    string logText;
                    switch (packetHeader.MatchType)
                    {
                        case PacketMatch.TcpConnect:
                            logText = $"TCP connection to port {packetHeader.DestinationPort} from {packetHeader.SourceIP}";
                            break;
                        case PacketMatch.PingRequest:
                            logText = $"ICMP ping request from {packetHeader.SourceIP}";
                            break;
                        case PacketMatch.NamePoison:
                            logText = $"Name poison attempt from {packetHeader.SourceIP}";
                            break;
                        case PacketMatch.RogueDhcp:
                            logText = $"Unrecognized DHCP server at {packetHeader.DhcpServerAddress}";
                            break;
                        default:
                            logText = string.Empty;
                            break;
                    }
                    outputFile.WriteLine(
                        DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString()
                        + " [" + packetHeader.DestinationIP + " ] " + logText);
                }
            }
            catch (Exception ex)
            {
                Configuration.IsLogEnabled = false;
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error writing to log file '{Configuration.LogPath}'. Logging has been disabled.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    EventLogEntryType.Error,
                    401);
            }
        }

        private void WriteEventLog(PacketHeader packetHeader)
        {
            switch (packetHeader.MatchType)
            {
                case PacketMatch.TcpConnect:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"TCP connection detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}" +
                        $"Destination IP: {packetHeader.DestinationIP}{Environment.NewLine}" +
                        $"Destination port: {packetHeader.DestinationPort}{Environment.NewLine}{Environment.NewLine}" +
                        $"TCP flags: {packetHeader.TcpFlagsAsString}",
                        EventLogEntryType.Information,
                        200);
                    break;
                case PacketMatch.PingRequest:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"ICMP ping request detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}" +
                        $"Destination IP: {packetHeader.DestinationIP}",
                        EventLogEntryType.Information,
                        201);
                    break;
                case PacketMatch.NamePoison:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Name poison attempt detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}",
                        EventLogEntryType.Information,
                        202);
                    break;
                case PacketMatch.RogueDhcp:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Unrecognized DHCP server detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"DHCP Server IP: {packetHeader.DhcpServerAddress}{Environment.NewLine}" +
                        $"Interface: {packetHeader.DestinationIP}",
                        EventLogEntryType.Information,
                        202);
                    break;
            }
        }

        private void LaunchApplication(PacketHeader packetHeader)
        {
            if (Configuration.TriggeredApplicationPath.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "An external application has been triggered to launch, but no application path has been specified.",
                    EventLogEntryType.Warning,
                    401);
                return;
            }

            try
            {
                Process.Start(
                    Configuration.TriggeredApplicationPath,
                    UserVariableExpansion.GetExpandedString(Configuration.TriggeredApplicationArguments, packetHeader));
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error launching external triggered application.{Environment.NewLine}{ex.Message}",
                    EventLogEntryType.Error,
                    401);
            }
        }

        private void SendEmail(PacketHeader packetHeader)
        {
            if (Configuration.EmailRecipients.Count == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no recipient address is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (Configuration.EmailSender.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no sender address is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (Configuration.EmailServer.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no mail server is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (Configuration.EmailSubject.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no message subject is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }

            using (var message = new MailMessage())
            {

                try
                {
                    var smtpClient = new SmtpClient();
                    smtpClient.Host = Configuration.EmailServer;
                    smtpClient.Port = Configuration.EmailServerPort;
                    if (Configuration.IsEmailAuthRequired)
                    {
                        smtpClient.Credentials = new NetworkCredential(Configuration.EmailUsername, Configuration.EmailPassword);
                    }
                    message.From = Configuration.EmailSenderDisplayName.Length > 0 ?
                        new MailAddress(Configuration.EmailSender, Configuration.EmailSenderDisplayName)
                        : new MailAddress(Configuration.EmailSender);
                    for (int i = 0; i < Configuration.EmailRecipients.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(Configuration.EmailRecipients[i]))
                        {
                            message.To.Add(Configuration.EmailRecipients[i].Trim());
                        }
                    }
                    message.Subject = UserVariableExpansion.GetExpandedString(Configuration.EmailSubject, packetHeader);
                    message.Body = UserVariableExpansion.GetExpandedString(GetMessageBody(packetHeader), packetHeader);

                    //Send the email.
                    smtpClient.Send(message);
                }
                catch (Exception ex)
                {
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Email action triggered, but the message failed to send.{Environment.NewLine}{ex.Message}",
                        EventLogEntryType.Warning,
                        403);
                    return;
                }
            }
        }

        private string GetMessageBody(PacketHeader packetHeader)
        {
            string messageBody;
            switch (packetHeader.MatchType)
            {
                case PacketMatch.PingRequest:
                    messageBody = Configuration.MessageBodyPing;
                    break;
                case PacketMatch.TcpConnect:
                    messageBody = Configuration.MessageBodyTcpConnect;
                    break;
                case PacketMatch.NamePoison:
                    messageBody = Configuration.MessageBodyNamePoison;
                    break;
                case PacketMatch.RogueDhcp:
                    messageBody = Configuration.MessageBodyRogueDhcp;
                    break;
                default:
                    messageBody = "Not defined.";
                    break;
            }

            return messageBody;
        }

        private void NamePoisionDetectionTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _isNamePoisonDetectionInProgress = true;
            _namePoisonTransactionIdResponse = int.MinValue;

            foreach (TcpTriggerInterface ipInterface in _tcpTriggerInterfaces)
            {
                // Generate three random hostnames between 7 and 15 characters to emulate a user opening Chrome.
                // Chromium source: https://cs.chromium.org/chromium/src/chrome/browser/intranet_redirect_detector.cc

                var randomHostnames = new string[3];
                var r = new Random();
                for (int i = 0; i < randomHostnames.Length; ++i)
                {
                    var hostname = string.Empty;
                    int numberOfCharacters = r.Next(7, 16);
                    for (int j = 0; j < numberOfCharacters; ++j)
                        hostname += (char)('a' + r.Next(0, 26));
                    randomHostnames[i] = hostname;
                }

                // Send LLMNR name queries.
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = 0; j < randomHostnames.Length; ++j)
                        PacketGenerator.SendLlmnrQuery(ipInterface, (ushort)(_netbiosTransactionId + j), randomHostnames[j]);
                    Thread.Sleep(750);
                }

                // Send NetBIOS name queries.
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = 0; j < randomHostnames.Length; ++j)
                        PacketGenerator.SendNetbiosQuery(ipInterface, (ushort)(_netbiosTransactionId + j), randomHostnames[j]);
                    Thread.Sleep(750);
                }
            }

            Thread.Sleep(2000);
            _netbiosTransactionId += 3;
            if (_netbiosTransactionId < 0x8000)
                _netbiosTransactionId += 0x8000;
            _isNamePoisonDetectionInProgress = true;
        }
    }
}