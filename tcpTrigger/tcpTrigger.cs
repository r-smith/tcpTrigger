using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private Configuration _configuration = new Configuration();
        private List<NetInterface> _tcpTriggerInterfaces;
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
            if (_configuration.Load() == false)
            {
                // An error was encountered while reading the configuration file. Stop the service and quit.
                Environment.Exit(1);
                return;
            }

            // If enabled, start name poison detection.
            if (_configuration.IsMonitorPoisonEnabled)
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
            var networkInterfaces = new List<NetInterface>();

            // Enumerate network interfaces. Determine and record which interfaces to listen on.
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    networkInterface.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            networkInterfaces.Add(
                                new NetInterface(
                                    address.Address,
                                    address.IPv4Mask,
                                    networkInterface.Description,
                                    networkInterface.GetPhysicalAddress()));
                        }
                    }
                }
            }

            // Check if tcpTrigger service is already running (_tcpTriggerInterfaces won't be null).
            if (_tcpTriggerInterfaces != null)
            {
                // Compares list of network interfaces to previously stored list of network interfaces.
                if (networkInterfaces.SequenceEqual(_tcpTriggerInterfaces, new NetInterfaceComparer()))
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
            _tcpTriggerInterfaces = new List<NetInterface>(networkInterfaces);

            // Start listeners.
            StartIpListeners();
        }

        private void StartIpListeners()
        {
            var sb = new StringBuilder();

            foreach (var netInterface in _tcpTriggerInterfaces)
            {
                sb.AppendLine($"Listening on interface: {netInterface.IP} [{netInterface.MacAddressAsString}]");
                RawSocketListener(netInterface);
            }

            sb.AppendLine();
            if (_configuration.IsMonitorTcpEnabled) sb.AppendLine($"Monitoring TCP port(s): {_configuration.TcpPortsToMonitorAsString}");
            if (_configuration.IsMonitorIcmpEnabled) sb.AppendLine("Monitoring ICMP ping requests");
            if (_configuration.IsMonitorPoisonEnabled) sb.AppendLine("Name poisoning detection is enabled");
            if (_configuration.IsMonitorDhcpEnabled) sb.Append("Rogue DHCP server detection is enabled");

            EventLog.WriteEntry(
                "tcpTrigger",
                sb.ToString(),
                EventLogEntryType.Information,
                100);
        }

        private void RawSocketListener(NetInterface netInterface)
        {
            // Buffer for incoming data.
            byte[] buffer = new byte[512];

            // Bind socket to IP endpoint.
            netInterface.NetworkSocket.Bind(new IPEndPoint(netInterface.IP, 0));
            netInterface.NetworkSocket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), null);

            // Callback method for processing captured packets.
            Action<IAsyncResult> OnReceive = null;
            OnReceive = (ar) =>
            {
                // Packet received.  Extract header information and determine if the packet matches our trigger rules.
                var packetHeader = new PacketHeader(buffer);

                if (DoesPacketMatchPingRequest(packetHeader, netInterface.IP))
                    packetHeader.MatchType = PacketMatch.PingRequest;
                else if (DoesPacketMatchMonitoredPort(packetHeader, netInterface.IP))
                    packetHeader.MatchType = PacketMatch.TcpConnect;
                else if (DoesPacketMatchNamePoison(packetHeader, netInterface.IP))
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
                else if (DoesPacketMatchDhcpServer(packetHeader, netInterface.IP))
                {
                    // If no DHCP servers are specified by the user, we will do automatic detection.
                    // Auto rogue DHCP detection alerts if more than one DHCP server is discovered.
                    if (_configuration.DhcpSafeServerList.Count == 0)
                    {
                        if (!(netInterface.DiscoveredDhcpServerList.Contains(packetHeader.DhcpServerAddress)))
                        {
                            packetHeader.DestinationIP = netInterface.IP;
                            packetHeader.SourceIP = packetHeader.DhcpServerAddress;
                            netInterface.DiscoveredDhcpServerList.Add(packetHeader.DhcpServerAddress);
                            if (netInterface.DiscoveredDhcpServerList.Count > 1)
                                packetHeader.MatchType = PacketMatch.RogueDhcp;
                        }
                    }
                    else if (!(_configuration.DhcpSafeServerList.Contains(packetHeader.DhcpServerAddress)) &&
                        !(netInterface.DiscoveredDhcpServerList.Contains(packetHeader.DhcpServerAddress)))
                    {
                        packetHeader.DestinationIP = netInterface.IP;
                        packetHeader.SourceIP = packetHeader.DhcpServerAddress;
                        netInterface.DiscoveredDhcpServerList.Add(packetHeader.DhcpServerAddress);
                        packetHeader.MatchType = PacketMatch.RogueDhcp;
                    }
                }

                // Process actions.
                if (packetHeader.MatchType != PacketMatch.None)
                {
                    packetHeader.DestinationMac = netInterface.MacAddress;

                    if (_configuration.IsEventLogEnabled)
                        WriteEventLog(packetHeader);

                    netInterface.RateLimitDictionaryCleanup(_configuration.ActionRateLimitMinutes);

                    if (!(netInterface.RateLimitDictionary.ContainsKey(packetHeader.SourceIP)) || _configuration.ActionRateLimitMinutes <= 0)
                    {
                        if (_configuration.ActionRateLimitMinutes > 0)
                            netInterface.RateLimitDictionary.Add(packetHeader.SourceIP, DateTime.Now);

                        if (_configuration.IsExternalAppEnabled) LaunchApplication(packetHeader);
                        if (_configuration.IsEmailNotificationEnabled) SendEmail(packetHeader);
                        if (_configuration.IsPopupMessageEnabled) DisplayPopupMessage(packetHeader);
                    }
                }
                // Reset buffer and continue listening for new data.
                buffer = new byte[512];
                try
                {
                    netInterface.NetworkSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), null);
                }
                catch (ObjectDisposedException)
                {
                    // Listener has been disposed by calling thread.
                    // This is expected behavior for cleanup. Do nothing.
                }
            };

            // Begin listening for data.
            netInterface.NetworkSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), null);
        }

        private bool DoesPacketMatchPingRequest(PacketHeader header, IPAddress ip)
        {
            if (_configuration.IsMonitorIcmpEnabled &&
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
            if (_configuration.IsMonitorTcpEnabled &&
                header.ProtocolType == Protocol.TCP &&
                header.TcpFlags == 0x2 &&
                header.DestinationIP.Equals(ip) &&
                (_configuration.TcpPortsToMonitor.Contains(header.DestinationPort) ||
                _configuration.TcpPortsToMonitor[0] == 0))
            {
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchNamePoison(PacketHeader header, IPAddress ip)
        {
            if (_configuration.IsMonitorPoisonEnabled &&
                _isNamePoisonDetectionInProgress &&
                header.IsNameQueryResponse &&
                header.DestinationIP.Equals(ip) &&
                (header.NetbiosTransactionId >= _netbiosTransactionId &&
                header.NetbiosTransactionId <= _netbiosTransactionId + 3))
            {
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchDhcpServer(PacketHeader header, IPAddress ip)
        {
            if (_configuration.IsMonitorDhcpEnabled &&
                header.DhcpServerAddress != null)
            {
                return true;
            }

            return false;
        }

        private void WriteEventLog(PacketHeader packetHeader)
        {
            switch (packetHeader.MatchType)
            {
                case PacketMatch.TcpConnect:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"A captured packet has matched trigger rules.{Environment.NewLine}{Environment.NewLine}" +
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
                        $"A captured ICMP ping request has matched trigger rules.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}" +
                        $"Destination IP: {packetHeader.DestinationIP}",
                        EventLogEntryType.Information,
                        201);
                    break;
                case PacketMatch.NamePoison:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"NetBIOS name poisoning detected.{Environment.NewLine}{Environment.NewLine}" +
                        $"Source IP: {packetHeader.SourceIP}",
                        EventLogEntryType.Information,
                        202);
                    break;
                case PacketMatch.RogueDhcp:
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Possible rogue DHCP server discovered.{Environment.NewLine}{Environment.NewLine}" +
                        $"DHCP Server IP: {packetHeader.DhcpServerAddress}{Environment.NewLine}" +
                        $"Interface: {packetHeader.DestinationIP}",
                        EventLogEntryType.Information,
                        202);
                    break;
            }
        }

        private void LaunchApplication(PacketHeader packetHeader)
        {
            if (_configuration.TriggeredApplicationPath.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "An application has been triggered to launch, but no application path has been specified.",
                    EventLogEntryType.Warning,
                    401);
                return;
            }

            try
            {
                Process.Start(
                    _configuration.TriggeredApplicationPath,
                    UserVariableExpansion.GetExpandedString(_configuration.TriggeredApplicationArguments, packetHeader));
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error launching triggered application: {ex.Message}",
                    EventLogEntryType.Error,
                    401);
            }
        }

        private void DisplayPopupMessage(PacketHeader packetHeader)
        {
            try
            {
                Process.Start("msg", $"* /time:0 {UserVariableExpansion.GetExpandedString(GetMessageBody(packetHeader), packetHeader)}");
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error displaying popup message: {ex.Message}",
                    EventLogEntryType.Error,
                    404);
            }
        }

        private void SendEmail(PacketHeader packetHeader)
        {
            if (_configuration.EmailRecipientAddress.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no recipient address is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (_configuration.EmailSenderAddress.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no sender address is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (_configuration.EmailServer.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no mail server is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (_configuration.EmailSubject.Length == 0)
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
                    smtpClient.Host = _configuration.EmailServer;
                    smtpClient.Port = _configuration.EmailServerPort;
                    message.From = _configuration.EmailSenderDisplayName.Length > 0 ?
                        new MailAddress(_configuration.EmailSenderAddress, _configuration.EmailSenderDisplayName)
                        : new MailAddress(_configuration.EmailSenderAddress);
                    message.To.Add(_configuration.EmailRecipientAddress);
                    message.Subject = UserVariableExpansion.GetExpandedString(_configuration.EmailSubject, packetHeader);
                    message.Body = UserVariableExpansion.GetExpandedString(GetMessageBody(packetHeader), packetHeader);

                    //Send the email.
                    smtpClient.Send(message);
                }
                catch (Exception ex)
                {
                    EventLog.WriteEntry(
                        "tcpTrigger",
                        $"Email event triggered, but the message failed to send. {ex.Message}",
                        EventLogEntryType.Warning,
                        403);
                    return;
                }
            }
        }

        private string GetMessageBody(PacketHeader packetHeader)
        {
            var messageBody = string.Empty;

            switch (packetHeader.MatchType)
            {
                case PacketMatch.PingRequest:
                    messageBody = _configuration.MessageBodyPing;
                    break;
                case PacketMatch.TcpConnect:
                    messageBody = _configuration.MessageBodyTcpConnect;
                    break;
                case PacketMatch.NamePoison:
                    messageBody = _configuration.MessageBodyNamePoison;
                    break;
                case PacketMatch.RogueDhcp:
                    messageBody = _configuration.MessageBodyRogueDhcp;
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

            foreach (var netInterface in _tcpTriggerInterfaces)
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
                        PacketGenerator.SendLlmnrQuery(netInterface, (ushort)(_netbiosTransactionId + j), randomHostnames[j]);
                    Thread.Sleep(750);
                }

                // Send NetBIOS name queries.
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = 0; j < randomHostnames.Length; ++j)
                        PacketGenerator.SendNetbiosQuery(netInterface, (ushort)(_netbiosTransactionId + j), randomHostnames[j]);
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