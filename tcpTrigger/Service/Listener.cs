using System;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;

namespace tcpTrigger
{
    partial class TcpTriggerService : ServiceBase
    {
        private void RawSocketListener(TcpTriggerInterface ipInterface)
        {
            // Buffer for incoming data.
            byte[] buffer = new byte[512];

            // Bind socket to IP endpoint.
            try
            {
                ipInterface.NetworkSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                ipInterface.NetworkSocket.Bind(new IPEndPoint(ipInterface.IP, 0));
                ipInterface.NetworkSocket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), null);
            }
            catch
            {
                throw new SocketException();
            }

            // Callback method for processing captured packets.
            void OnReceive(IAsyncResult ar)
            {
                // Packet received.
                try
                {
                    ProcessPacket(buffer, ipInterface);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        $"tcpTrigger failed to process a packet on network interface {ipInterface.IP} ({ipInterface.Description})."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        Logger.EventCode.Error);
                }

                // Reset buffer and continue listening for new data.
                buffer = new byte[512];
                try
                {
                    ipInterface.NetworkSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, OnReceive, null);
                }
                catch (ObjectDisposedException)
                {
                    // Listener has been disposed by calling thread.
                    // This is expected behavior for cleanup. Do nothing.
                }
                catch (SocketException ex)
                {
                    Logger.WriteError(
                        $"tcpTrigger stopped monitoring network interface {ipInterface.IP} ({ipInterface.Description}) because of an error accessing the interface."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        Logger.EventCode.Error);
                }
            }

            // Begin listening for data.
            ipInterface.NetworkSocket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, OnReceive, null);
        }

        private void ProcessPacket(byte[] buffer, TcpTriggerInterface ipInterface)
        {
            // Extract header information.
            var packetHeader = new PacketHeader(buffer);

            // Determine if the packet matches our trigger rules.
            if (DoesPacketMatchICMP(packetHeader, ipInterface.IP))
                packetHeader.MatchType = PacketMatch.IcmpRequest;
            else if (DoesPacketMatchTCP(packetHeader, ipInterface.IP))
                packetHeader.MatchType = PacketMatch.TcpConnect;
            else if (DoesPacketMatchUDP(packetHeader, ipInterface.IP))
                packetHeader.MatchType = PacketMatch.UdpCommunication;

            // Process actions.
            if (packetHeader.MatchType != PacketMatch.None)
            {
                // A match was detected. Perform enabled actions.
                try
                {
                    packetHeader.DestinationMac = ipInterface.MacAddress;
                    if (Settings.IsLogEnabled)
                        WriteLog(packetHeader);
                    if (Settings.IsEventLogEnabled)
                        WriteEventLog(packetHeader);
                    if (Settings.IsExternalAppEnabled)
                        LaunchApplication(packetHeader);
                    if (Settings.IsEmailNotificationEnabled)
                        TriggerEmail(packetHeader, ipInterface);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        "A packet matched detection rules but tcpTrigger failed to perform configured actions. "
                        + ex.Message,
                        Logger.EventCode.Error);
                }
            }
        }
    }
}