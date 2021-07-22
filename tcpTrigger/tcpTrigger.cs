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
        private Thread _ListenerThread = null;
        private Configuration _Configuration = new Configuration();
        private System.Timers.Timer _NamePoisionDetectionTimer;
        private ushort _NetbiosTransactionId = 0x8000;
        private bool _IsNamePoisonDetectionInProgress = false;
        private int _NamePoisonTransactionIdResponse = int.MinValue;
        private List<NetInterface> _NetInterfaces = new List<NetInterface>();

        public tcpTrigger()
        {
            InitializeComponent();

            EventLog.Log = "Application";
        }

        protected override void OnStart(string[] args)
        {
            _Configuration.Load();
            
            if (_Configuration.IsMonitorPoisonEnabled)
            {
                _NamePoisionDetectionTimer = new System.Timers.Timer();
                _NamePoisionDetectionTimer.Interval = TimeSpan.FromMinutes(4).TotalMilliseconds;
                _NamePoisionDetectionTimer.Elapsed += _NamePoisionDetectionTimer_Elapsed;
                _NamePoisionDetectionTimer.Enabled = true;
            }
            
            // Get network settings for all connected IPv4 network adapters.
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (_Configuration.DoNotMonitorVMwareVirtualHostAdapters &&
                    networkInterface.Description.StartsWith("VMware Virtual Ethernet Adapter for "))
                    continue;
                if (networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    networkInterface.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            _NetInterfaces.Add(
                                new NetInterface(
                                    address.Address,
                                    address.IPv4Mask,
                                    networkInterface.Description,
                                    networkInterface.GetPhysicalAddress()));
                        }
                    }
                }
            }

            _ListenerThread = new Thread(StartIpListeners);
            _ListenerThread.Start();
        }

        private void _NamePoisionDetectionTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _IsNamePoisonDetectionInProgress = true;
            _NamePoisonTransactionIdResponse = int.MinValue;

            foreach (var netInterface in _NetInterfaces)
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
                        PacketGenerator.SendLlmnrQuery(netInterface, (ushort)(_NetbiosTransactionId + j), randomHostnames[j]);
                    Thread.Sleep(750);
                }

                // Send NetBIOS name queries.
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = 0; j < randomHostnames.Length; ++j)
                        PacketGenerator.SendNetbiosQuery(netInterface, (ushort)(_NetbiosTransactionId + j), randomHostnames[j]);
                    Thread.Sleep(750);
                }
            }

            Thread.Sleep(2000);
            _NetbiosTransactionId += 3;
            if (_NetbiosTransactionId < 0x8000)
                _NetbiosTransactionId += 0x8000;
            _IsNamePoisonDetectionInProgress = true;
        }

        protected override void OnStop()
        {
            if (_NamePoisionDetectionTimer != null)
            {
                _NamePoisionDetectionTimer.Enabled = false;
                _NamePoisionDetectionTimer.Dispose();
                _NamePoisionDetectionTimer = null;
            }

            if (_ListenerThread != null)
            {
                // Wait one second for the thread to stop.
                _ListenerThread.Join(1000);

                // If still alive, get rid of the thread.
                if (_ListenerThread.IsAlive)
                {
                    _ListenerThread.Abort();
                }
                _ListenerThread = null;
            }
        }

        private void StartIpListeners()
        {
            var sb = new StringBuilder();

            foreach (var netInterface in _NetInterfaces)
            {
                sb.AppendLine($"Listening on interface: {netInterface.IP} [{netInterface.MacAddressAsString}]");
                RawSocketListener(netInterface);
            }

            sb.AppendLine();
            if (_Configuration.IsMonitorTcpEnabled) sb.AppendLine($"Monitoring TCP port(s): {_Configuration.TcpPortsToMonitorAsString}");
            if (_Configuration.IsMonitorIcmpEnabled) sb.AppendLine("Monitoring ICMP ping requests");
            if (_Configuration.IsMonitorPoisonEnabled) sb.AppendLine("Name poisoning detection is enabled");
            if (_Configuration.IsMonitorDhcpEnabled) sb.Append("Rogue DHCP server detection is enabled");

            EventLog.WriteEntry(
                "tcpTrigger",
                sb.ToString(),
                EventLogEntryType.Information,
                100);
        }

        private void RawSocketListener(NetInterface netInterface)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            socket.Bind(new IPEndPoint(netInterface.IP, 0));
            socket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), null);

            // Buffer for incoming data.
            byte[] buffer = new byte[512];

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
                    if (_NamePoisonTransactionIdResponse < 0)
                    {
                        _NamePoisonTransactionIdResponse = packetHeader.NetbiosTransactionId;
                    }
                    else if (_NamePoisonTransactionIdResponse != packetHeader.NetbiosTransactionId)
                    {
                        packetHeader.MatchType = PacketMatch.NamePoison;
                    }
                }
                else if (DoesPacketMatchDhcpServer(packetHeader, netInterface.IP))
                {
                    // If no DHCP servers are specified by the user, we will do automatic detection.
                    // Auto rogue DHCP detection alerts if more than one DHCP server is discovered.
                    if (_Configuration.DhcpSafeServerList.Count == 0)
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
                    else if (!(_Configuration.DhcpSafeServerList.Contains(packetHeader.DhcpServerAddress)) &&
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

                    if (_Configuration.IsEventLogEnabled)
                        WriteEventLog(packetHeader);

                    netInterface.RateLimitDictionaryCleanup(_Configuration.ActionRateLimitMinutes);

                    if (!(netInterface.RateLimitDictionary.ContainsKey(packetHeader.SourceIP)) || _Configuration.ActionRateLimitMinutes <= 0)
                    {
                        if (_Configuration.ActionRateLimitMinutes > 0)
                            netInterface.RateLimitDictionary.Add(packetHeader.SourceIP, DateTime.Now);

                        if (_Configuration.IsExternalAppEnabled) LaunchApplication(packetHeader);
                        if (_Configuration.IsEmailNotificationEnabled) SendEmail(packetHeader);
                        if (_Configuration.IsPopupMessageEnabled) DisplayPopupMessage(packetHeader);
                    }
                }

                // Reset buffer and continue listening for new data.
                buffer = new byte[512];
                socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), null);
            };

            // Begin listening for data.
            socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), null);
        }

        private bool DoesPacketMatchPingRequest(PacketHeader header, IPAddress ip)
        {
            if (_Configuration.IsMonitorIcmpEnabled &&
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
            if (_Configuration.IsMonitorTcpEnabled &&
                header.ProtocolType == Protocol.TCP &&
                header.TcpFlags == 0x2 &&
                header.DestinationIP.Equals(ip) &&
                (_Configuration.TcpPortsToMonitor.Contains(header.DestinationPort) ||
                _Configuration.TcpPortsToMonitor[0] == 0))
            {
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchNamePoison(PacketHeader header, IPAddress ip)
        {
            if (_Configuration.IsMonitorPoisonEnabled &&
                _IsNamePoisonDetectionInProgress &&
                header.IsNameQueryResponse &&
                header.DestinationIP.Equals(ip) &&
                (header.NetbiosTransactionId >= _NetbiosTransactionId &&
                header.NetbiosTransactionId <= _NetbiosTransactionId + 3))
            {
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchDhcpServer(PacketHeader header, IPAddress ip)
        {
            if (_Configuration.IsMonitorDhcpEnabled &&
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
            if (_Configuration.TriggeredApplicationPath.Length == 0)
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
                    _Configuration.TriggeredApplicationPath,
                    UserVariableExpansion.GetExpandedString(_Configuration.TriggeredApplicationArguments, packetHeader));
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
            if (_Configuration.EmailRecipientAddress.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no recipient address is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (_Configuration.EmailSenderAddress.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no sender address is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (_Configuration.EmailServer.Length == 0)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    "Email event triggered, but no mail server is configured.",
                    EventLogEntryType.Warning,
                    402);
                return;
            }
            if (_Configuration.EmailSubject.Length == 0)
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
                    smtpClient.Host = _Configuration.EmailServer;
                    smtpClient.Port = _Configuration.EmailServerPort;
                    message.From = _Configuration.EmailSenderDisplayName.Length > 0 ?
                        new MailAddress(_Configuration.EmailSenderAddress, _Configuration.EmailSenderDisplayName)
                        : new MailAddress(_Configuration.EmailSenderAddress);
                    message.To.Add(_Configuration.EmailRecipientAddress);
                    message.Subject = UserVariableExpansion.GetExpandedString(_Configuration.EmailSubject, packetHeader);
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
                    messageBody = _Configuration.MessageBodyPing;
                    break;
                case PacketMatch.TcpConnect:
                    messageBody = _Configuration.MessageBodyTcpConnect;
                    break;
                case PacketMatch.NamePoison:
                    messageBody = _Configuration.MessageBodyNamePoison;
                    break;
                case PacketMatch.RogueDhcp:
                    messageBody = _Configuration.MessageBodyRogueDhcp;
                    break;
                default:
                    messageBody = "Not defined.";
                    break;
            }

            return messageBody;
        }
    }
}