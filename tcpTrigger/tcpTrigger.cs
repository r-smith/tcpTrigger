using System;
using System.Collections.Generic;
using System.Data;
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
        private Dictionary<IPAddress, DateTime> _RateLimitDictionary = new Dictionary<IPAddress, DateTime>();
        private Configuration _Configuration = new Configuration();
        private System.Timers.Timer _NamePoisionDetectionTimer;
        private ushort _NetbiosTransactionId = 0x8000;
        private bool _IsNamePoisonDetectionInProgress = false;
        private int _NamePoisonTransactionIdResponse = int.MinValue;

        public tcpTrigger()
        {
            InitializeComponent();

            EventLog.Log = "Application";
        }

        protected override void OnStart(string[] args)
        {
            _Configuration.Load();

            _ListenerThread = new Thread(StartIpListeners);
            _ListenerThread.Start();

            if (_Configuration.EnableNamePoisonDetection)
            {
                _NamePoisionDetectionTimer = new System.Timers.Timer();
                _NamePoisionDetectionTimer.Interval = TimeSpan.FromMinutes(4).TotalMilliseconds;
                _NamePoisionDetectionTimer.Elapsed += _NamePoisionDetectionTimer_Elapsed;
                _NamePoisionDetectionTimer.Enabled = true;
            }
        }

        private void _NamePoisionDetectionTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _IsNamePoisonDetectionInProgress = true;
            _NamePoisonTransactionIdResponse = int.MinValue;

            var ipAddresses = new List<UnicastIPAddressInformation>();
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    networkInterface.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ipAddresses.Add(address);
                        }
                    }
                }
            }

            foreach (var ip in ipAddresses)
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
                    {
                        SendLlmnrQuery(ip, (ushort)(_NetbiosTransactionId + j), randomHostnames[j]);
                    }
                    Thread.Sleep(750);
                }

                // Send NetBIOS name queries.
                for (int i = 0; i < 3; ++i)
                {
                    for (int j = 0; j < randomHostnames.Length; ++j)
                    {
                        SendNetbiosQuery(ip, (ushort)(_NetbiosTransactionId + j), randomHostnames[j]);
                    }
                    Thread.Sleep(750);
                }
            }

            Thread.Sleep(1000);
            _NetbiosTransactionId += 3;
            _IsNamePoisonDetectionInProgress = true;
        }

        private static void SendLlmnrQuery(UnicastIPAddressInformation ip, ushort transactionId, string queryName)
        {
            if (queryName.Length > 255) queryName = queryName.Substring(0, 255);
            queryName = queryName.ToLower();

            var byteList = new List<byte[]>();
            byteList.Add(BitConverter.GetBytes(transactionId));
            if (BitConverter.IsLittleEndian)
                Array.Reverse(byteList[0]);
            byteList.Add(new byte[] { 0x00, 0x00, 0x00, 0x01,
                                      0x00, 0x00, 0x00, 0x00,
                                      0x00, 0x00 });
            var nameLengthByte = new Byte[1];
            nameLengthByte[0] = Convert.ToByte(queryName.Length);
            byteList.Add(nameLengthByte);
            byteList.Add(Encoding.ASCII.GetBytes(queryName));
            byteList.Add(new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01 });

            var sendBytes = byteList.SelectMany(a => a).ToArray();

            try
            {
                var multicastAddress = IPAddress.Parse("224.0.0.252");
                var remoteEndpoint = new IPEndPoint(multicastAddress, 5355);

                // Since this is a multicast packet, we must specify the local endpoint
                // to ensure it is sent out on every interface on a multi-homed system.
                var localEndpoint = new IPEndPoint(ip.Address, 0);
                var udpClient = new UdpClient(localEndpoint);
                udpClient.Send(sendBytes, sendBytes.Length, remoteEndpoint);
                udpClient.Close();
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error transmitting LLMNR query: {ex.Message}",
                    EventLogEntryType.Error,
                    405);
            }
        }

        private static void SendNetbiosQuery(UnicastIPAddressInformation ip, ushort transactionId, string netbiosName)
        {
            var byteList = new List<byte[]>();
            byteList.Add(BitConverter.GetBytes(transactionId));
            if (BitConverter.IsLittleEndian)
                Array.Reverse(byteList[0]);
            byteList.Add(new byte[] { 0x01, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20 });
            byteList.Add(EncodeNetbiosName(netbiosName));
            byteList.Add(new byte[] { 0x00, 0x00, 0x20, 0x00, 0x01 });

            var sendBytes = byteList.SelectMany(a => a).ToArray();

            try
            {
                var broadcastAddress = GetBroadcastAddress(ip.Address, ip.IPv4Mask);
                var endpoint = new IPEndPoint(broadcastAddress, 137);

                var udpClient = new UdpClient(137);
                udpClient.Send(sendBytes, sendBytes.Length, endpoint);
                udpClient.Close();
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error transmitting NetBIOS query: {ex.Message}",
                    EventLogEntryType.Error,
                    405);
            }
        }

        private static byte[] EncodeNetbiosName(string name)
        {
            // Check here: https://support.microsoft.com/en-us/kb/194203
            // Check here: https://tools.ietf.org/html/rfc1002
            // Windows uses 15 characters for the name (padded with spaces).
            // The 16th character is reserved for the service identifier.

            name = name.ToUpper();
            if (name.Length > 15) name = name.Substring(0, 15);
            name = name.PadRight(16);

            var encodedNameBuffer = new byte[32];
            for (int i = 0; i < name.Length; ++i)
            {
                encodedNameBuffer[i * 2] = Convert.ToByte((name[i] >> 4) + 'A');
                encodedNameBuffer[(i * 2) + 1] = Convert.ToByte((name[i] & 0x0F) + 'A');
            }

            // Append NetBIOS suffix
            encodedNameBuffer[30] = 'A' + 0;
            encodedNameBuffer[31] = 'A' + 0;

            return encodedNameBuffer;
        }

        public static IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
        {
            // Credit: Jean-Paul Mikkers
            // Source: https://dhcpserver.codeplex.com/

            byte[] ipAdressBytes = address.GetAddressBytes();
            byte[] subnetMaskBytes = subnetMask.GetAddressBytes();

            if (ipAdressBytes.Length != subnetMaskBytes.Length)
                throw new ArgumentException("Lengths of IP address and subnet mask do not match.");

            byte[] broadcastAddress = new byte[ipAdressBytes.Length];
            for (int i = 0; i < broadcastAddress.Length; i++)
            {
                broadcastAddress[i] = (byte)(ipAdressBytes[i] | (subnetMaskBytes[i] ^ 255));
            }
            return new IPAddress(broadcastAddress);
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
                // Wait for one second for the the thread to stop.
                _ListenerThread.Join(1000);

                // If still alive; Get rid of the thread.
                if (_ListenerThread.IsAlive)
                {
                    _ListenerThread.Abort();
                }
                _ListenerThread = null;
            }
        }

        private void StartIpListeners()
        {
            // Get all IPv4 addresses to listen on.
            var ipAddresses = Dns.GetHostEntry(Dns.GetHostName())
                .AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork)
                .AsEnumerable();
            var sb = new StringBuilder();

            foreach (IPAddress ip in ipAddresses)
            {
                sb.AppendLine($"Listening on interface: {ip}");
                RawSocketListener(ip);
            }

            if (_Configuration.EnableMonitorTcpPort) sb.AppendLine($"Monitoring TCP port {_Configuration.TcpPortsToListenOnAsString}");
            if (_Configuration.EnableMonitorIcmpPing) sb.AppendLine("Monitoring ICMP ping requests");
            if (_Configuration.EnableNamePoisonDetection) sb.AppendLine("Name poisoning detection is enabled");

            EventLog.WriteEntry(
                "tcpTrigger",
                sb.ToString(),
                EventLogEntryType.Information,
                100);
        }

        private void RawSocketListener(IPAddress ip)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            socket.Bind(new IPEndPoint(ip, 0));
            socket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), null);

            // Buffer for incoming data.
            byte[] buffer = new byte[74];

            // Callback method for processing captured packets.
            Action<IAsyncResult> OnReceive = null;
            OnReceive = (ar) =>
            {
                // Packet received.  Extract header information and determine if the packet matches our trigger rules.
                var packetHeader = new PacketHeader(buffer);

                if (DoesPacketMatchPingRequest(packetHeader, ip))
                    packetHeader.MatchType = PacketMatch.PingRequest;
                else if (DoesPacketMatchMonitoredPort(packetHeader, ip))
                    packetHeader.MatchType = PacketMatch.TcpConnect;
                else if (DoesPacketMatchNamePoison(packetHeader, ip))
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

                if (packetHeader.MatchType != PacketMatch.None)
                {
                    if (_Configuration.EnableEventLogAction)
                        WriteEventLog(packetHeader);

                    RateLimitDictionaryCleanup();

                    if (!(_RateLimitDictionary.ContainsKey(packetHeader.SourceIP)) || _Configuration.ActionRateLimitMinutes <= 0)
                    {
                        if (_Configuration.ActionRateLimitMinutes > 0)
                            _RateLimitDictionary.Add(packetHeader.SourceIP, DateTime.Now);

                        if (_Configuration.EnableRunApplicationAction) LaunchApplication(packetHeader);
                        if (_Configuration.EnableEmailNotificationAction) SendEmail(packetHeader);
                        if (_Configuration.EnablePopupMessageAction) DisplayPopupMessage(packetHeader);
                    }
                }

                // Reset buffer and continue listening for new data.
                buffer = new byte[74];
                socket.BeginReceive(buffer, 0, 74, SocketFlags.None, new AsyncCallback(OnReceive), null);
            };

            // Begin listening for data.
            socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), null);
        }

        private bool DoesPacketMatchPingRequest(PacketHeader header, IPAddress ip)
        {
            if (_Configuration.EnableMonitorIcmpPing &&
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
            if (_Configuration.EnableMonitorTcpPort &&
                header.ProtocolType == Protocol.TCP &&
                header.TcpFlags == 0x2 &&
                header.DestinationIP.Equals(ip) &&
                (_Configuration.TcpPortsToListenOn.Contains(header.DestinationPort) ||
                _Configuration.TcpPortsToListenOn[0] == 0))
            {
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchNamePoison(PacketHeader header, IPAddress ip)
        {
            if (_Configuration.EnableNamePoisonDetection &&
                _IsNamePoisonDetectionInProgress &&
                header.ProtocolType == Protocol.UDP &&
                header.DestinationIP.Equals(ip) &&
                header.IsNameQueryResponse &&
                (header.NetbiosTransactionId >= _NetbiosTransactionId &&
                header.NetbiosTransactionId <= _NetbiosTransactionId + 3))
            {
                return true;
            }

            return false;
        }

        private void RateLimitDictionaryCleanup()
        {
            if (_Configuration.ActionRateLimitMinutes <= 0)
                return;

            var ipAddressToDelete = new List<IPAddress>();
            foreach (KeyValuePair<IPAddress, DateTime> entry in _RateLimitDictionary)
            {
                if (entry.Value <= DateTime.Now.AddMinutes(-_Configuration.ActionRateLimitMinutes))
                {
                    ipAddressToDelete.Add(entry.Key);
                }
            }

            ipAddressToDelete.ForEach(x => _RateLimitDictionary.Remove(x));
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
            }

            return messageBody;
        }
    }
}