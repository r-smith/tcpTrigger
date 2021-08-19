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

                if (DoesPacketMatchICMP(packetHeader, ipInterface.IP))
                    packetHeader.MatchType = PacketMatch.PingRequest;
                else if (DoesPacketMatchTCP(packetHeader, ipInterface.IP))
                    packetHeader.MatchType = PacketMatch.TcpConnect;
                else if (DoesPacketMatchUDP(packetHeader, ipInterface.IP))
                    packetHeader.MatchType = PacketMatch.UdpCommunication;
                else if (DoesPacketMatchDHCP(packetHeader, ipInterface.IP))
                {
                    if (!Settings.IgnoredDhcpServers.Contains(packetHeader.DhcpServerAddress)
                        && packetHeader.DhcpTransactionId != ipInterface.DhcpLastTransactionId)
                    {
                        packetHeader.DestinationIP = ipInterface.IP;
                        packetHeader.SourceIP = packetHeader.DhcpServerAddress;
                        packetHeader.MatchType = PacketMatch.RogueDhcp;
                        ipInterface.DhcpLastTransactionId = packetHeader.DhcpTransactionId;
                    }
                }

                // Process actions.
                if (packetHeader.MatchType != PacketMatch.None)
                {
                    // A match was detected. Perform enabled actions.

                    packetHeader.DestinationMac = ipInterface.MacAddress;
                    if (!Settings.IgnoredEndpoints.Contains(packetHeader.SourceIP))
                    {
                        if (Settings.IsLogEnabled)
                            WriteLog(packetHeader);
                        if (Settings.IsEventLogEnabled)
                            WriteEventLog(packetHeader);
                        if (Settings.IsExternalAppEnabled)
                            LaunchApplication(packetHeader);
                        if (Settings.IsEmailNotificationEnabled)
                            TriggerEmail(packetHeader, ipInterface);
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