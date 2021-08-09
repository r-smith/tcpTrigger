using System;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;

namespace tcpTrigger
{
    partial class tcpTrigger : ServiceBase
    {
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
                else if (DoesPacketMatchDhcpServer(packetHeader, ipInterface.IP))
                {
                    // If no DHCP servers are specified by the user, we will do automatic detection.
                    // Auto rogue DHCP detection alerts if more than one DHCP server is discovered.
                    if (Settings.IgnoredDhcpServers.Count == 0)
                    {
                        if (!ipInterface.DiscoveredDhcpServers.Contains(packetHeader.DhcpServerAddress))
                        {
                            packetHeader.DestinationIP = ipInterface.IP;
                            packetHeader.SourceIP = packetHeader.DhcpServerAddress;
                            ipInterface.DiscoveredDhcpServers.Add(packetHeader.DhcpServerAddress);
                            if (ipInterface.DiscoveredDhcpServers.Count > 1)
                                packetHeader.MatchType = PacketMatch.RogueDhcp;
                        }
                    }
                    else if (!Settings.IgnoredDhcpServers.Contains(packetHeader.DhcpServerAddress) &&
                        !ipInterface.DiscoveredDhcpServers.Contains(packetHeader.DhcpServerAddress))
                    {
                        packetHeader.DestinationIP = ipInterface.IP;
                        packetHeader.SourceIP = packetHeader.DhcpServerAddress;
                        ipInterface.DiscoveredDhcpServers.Add(packetHeader.DhcpServerAddress);
                        packetHeader.MatchType = PacketMatch.RogueDhcp;
                    }
                }

                // Process actions.
                if (packetHeader.MatchType != PacketMatch.None)
                {
                    packetHeader.DestinationMac = ipInterface.MacAddress;

                    if (Settings.IsLogEnabled)
                        WriteLog(packetHeader);
                    if (Settings.IsEventLogEnabled)
                        WriteEventLog(packetHeader);

                    if (!Settings.IgnoredEndpoints.Contains(packetHeader.SourceIP))
                    {
                        ipInterface.RateLimitDictionaryCleanup(Settings.ActionRateLimitSeconds);

                        if (!ipInterface.RateLimitDictionary.ContainsKey(packetHeader.SourceIP) || Settings.ActionRateLimitSeconds <= 0)
                        {
                            if (Settings.ActionRateLimitSeconds > 0)
                                ipInterface.RateLimitDictionary.Add(packetHeader.SourceIP, DateTime.Now);

                            if (Settings.IsExternalAppEnabled) LaunchApplication(packetHeader);
                            if (Settings.IsEmailNotificationEnabled) SendEmail(packetHeader);
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
    }
}