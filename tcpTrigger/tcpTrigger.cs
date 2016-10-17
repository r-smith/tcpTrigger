using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Mail;
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
        }

        protected override void OnStop()
        {
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
            var IPv4Addresses = Dns.GetHostEntry(Dns.GetHostName())
                .AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork)
                .AsEnumerable();
            var sb = new StringBuilder();

            foreach (IPAddress ip in IPv4Addresses)
            {
                sb.AppendLine($"Listening on interface: {ip}");
                RawSocketListener(ip);
            }

            if (_Configuration.EnableMonitorTcpPort) sb.AppendLine($"Monitoring TCP port {_Configuration.TcpPortsToListenOnAsString}");
            if (_Configuration.EnableMonitorIcmpPing) sb.AppendLine("Monitoring ICMP ping requests");

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

            // Create a buffer for incoming data.  We need enough to store the IP header plus at least the first 14 bytes
            // of a TCP header.  An IP header can be 20 - 60 bytes (typically 20), plus 14 bytes equals 74 bytes total.
            byte[] buffer = new byte[74];

            // Callback method for processing captured packets.
            Action<IAsyncResult> OnReceive = null;
            OnReceive = (ar) =>
            {
                // Packet received.  Extract header information and determine if the packet matches our trigger rules.
                var packetHeader = new PacketHeader(buffer);

                if (DoesPacketMatchTrigger(packetHeader, ip))
                {
                    if (_Configuration.EnableEventLogAction) WriteEventLog(packetHeader);

                    if (_Configuration.EnableRunApplicationAction ||
                        _Configuration.EnableEmailNotificationAction)
                    {
                        if (_Configuration.ActionRateLimitMinutes > 0)
                        {
                            RateLimitDictionaryCleanup();
                            if (!(_RateLimitDictionary.ContainsKey(packetHeader.SourceIP)))
                            {
                                _RateLimitDictionary.Add(packetHeader.SourceIP, DateTime.Now);
                                if (_Configuration.EnableRunApplicationAction) LaunchApplication(packetHeader);
                                if (_Configuration.EnableEmailNotificationAction) SendEmail(packetHeader);
                            }
                        }
                        else
                        {
                            if (_Configuration.EnableRunApplicationAction) LaunchApplication(packetHeader);
                            if (_Configuration.EnableEmailNotificationAction) SendEmail(packetHeader);
                        }
                    }
                }

                // Reset buffer and continue listening for new data.
                buffer = new byte[74];
                socket.BeginReceive(buffer, 0, 74, SocketFlags.None, new AsyncCallback(OnReceive), null);
            };

            // Begin listening for data.
            socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), null);
        }

        private bool DoesPacketMatchTrigger(PacketHeader header, IPAddress ip)
        {
            if (_Configuration.EnableMonitorIcmpPing &&
                header.ProtocolType == Protocol.ICMP &&
                header.DestinationIP.Equals(ip) &&
                header.IcmpType == 8)
            {
                return true;
            }

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
            if (packetHeader.ProtocolType == Protocol.TCP)
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"A captured packet has matched trigger rules.{Environment.NewLine}{Environment.NewLine}" +
                    $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}" +
                    $"Destination IP: {packetHeader.DestinationIP}{Environment.NewLine}" +
                    $"Destination port: {packetHeader.DestinationPort}{Environment.NewLine}{Environment.NewLine}" +
                    $"TCP flags: {packetHeader.TcpFlagsAsString}",
                    EventLogEntryType.Information,
                    200);
            else if (packetHeader.ProtocolType == Protocol.ICMP)
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"A captured ICMP ping request has matched trigger rules.{Environment.NewLine}{Environment.NewLine}" +
                    $"Source IP: {packetHeader.SourceIP}{Environment.NewLine}" +
                    $"Destination IP: {packetHeader.DestinationIP}",
                    EventLogEntryType.Information,
                    201);
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
                    message.Body = UserVariableExpansion.GetExpandedString(_Configuration.EmailBody, packetHeader);

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
    }
}