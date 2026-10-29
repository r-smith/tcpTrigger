using System;
using System.Net;
using System.Net.Sockets;

namespace tcpTrigger
{
    // Captures incoming packets on a network interface and runs the configured actions for any that match.
    internal class PacketListener
    {
        // The raw socket capturing packets. Created by Start and closed by Stop.
        private Socket _socket;

        // The network interface this listener captures packets on.
        public TcpTriggerInterface Interface { get; }

        public PacketListener(TcpTriggerInterface ipInterface)
        {
            Interface = ipInterface;
        }

        // Starts capturing packets on the interface. Throws a SocketException if the listener can't start.
        public void Start()
        {
            // Buffer for incoming data.
            byte[] buffer = new byte[512];

            // Bind socket to IP endpoint.
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                _socket.Bind(new IPEndPoint(Interface.IP, 0));
                _socket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), null);
            }
            catch
            {
                throw new SocketException();
            }

            // Callback method for processing captured packets.
            void OnReceive(IAsyncResult ar)
            {
                // Complete the receive, as required for every BeginReceive.
                // Errors and the number of bytes received are ignored.
                try
                {
                    _socket.EndReceive(ar, out SocketError _);
                }
                catch (ObjectDisposedException)
                {
                    // The listener was stopped. Stop receiving.
                    return;
                }

                // Process the packet. Log any error and keep listening.
                try
                {
                    ProcessPacket(buffer, Interface);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        $"tcpTrigger failed to process a packet on network interface {Interface.IP} ({Interface.Description})."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        Logger.EventCode.Error);
                }

                // Reset buffer and continue listening for new data.
                buffer = new byte[512];
                try
                {
                    _socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, OnReceive, null);
                }
                catch (ObjectDisposedException)
                {
                    // Listener has been disposed by calling thread.
                    // This is expected behavior for cleanup. Do nothing.
                }
                catch (SocketException ex)
                {
                    Logger.WriteError(
                        $"tcpTrigger stopped monitoring network interface {Interface.IP} ({Interface.Description}) because of an error accessing the interface."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        Logger.EventCode.Error);
                }
            }

            // Begin listening for data.
            _socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, OnReceive, null);
        }

        // Stops capturing packets on the interface.
        public void Stop()
        {
            _socket?.Close();
        }

        private static void ProcessPacket(byte[] buffer, TcpTriggerInterface ipInterface)
        {
            // Extract header information.
            var packetHeader = new PacketHeader(buffer);

            // Determine if the packet matches our trigger rules.
            packetHeader.MatchType = PacketMatcher.GetMatchType(packetHeader, ipInterface.IP);

            // Process actions.
            if (packetHeader.MatchType != PacketMatch.None)
            {
                try
                {
                    packetHeader.DestinationMac = ipInterface.MacAddress;
                    ActionRunner.Run(packetHeader, ipInterface);
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
