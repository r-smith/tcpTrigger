using System;
using System.Net;
using System.Net.Sockets;

namespace tcpTrigger
{
    // Captures incoming packets on a network interface and queues any detections for their notifications.
    internal class PacketListener
    {
        // The raw socket capturing packets. Created by Start and closed by Stop.
        private Socket _socket;

        // Detections are queued here for their notifications to run.
        private readonly DetectionQueue _detectionQueue;

        // The network interface this listener captures packets on.
        public TcpTriggerInterface Interface { get; }

        // Set when the listener stops because of an error, so the network check can restart it.
        private volatile bool _hasFailed;
        public bool HasFailed => _hasFailed;

        public PacketListener(TcpTriggerInterface ipInterface, DetectionQueue detectionQueue)
        {
            Interface = ipInterface;
            _detectionQueue = detectionQueue;
        }

        // Starts capturing packets on the interface. Throws a SocketException if the listener can't start.
        public void Start()
        {
            // Buffer for incoming data.
            byte[] buffer = new byte[512];

            // Bind socket to IP endpoint.
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            _socket.Bind(new IPEndPoint(Interface.IP, 0));
            _socket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), null);

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
                    ProcessPacket(buffer);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        $"tcpTrigger failed to read a packet on network interface {Interface.Name} ({Interface.IP}). "
                        + "Monitoring continues."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.GetType().Name}: {ex.Message}",
                        Logger.EventCode.PacketProcessingFailed);
                }

                // Reset buffer and continue listening for new data.
                buffer = new byte[512];
                try
                {
                    _socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, OnReceive, null);
                }
                catch (ObjectDisposedException)
                {
                    // The listener was stopped.
                }
                catch (SocketException ex)
                {
                    _hasFailed = true;
                    Logger.WriteError(
                        $"tcpTrigger stopped monitoring network interface {Interface.Name} ({Interface.IP}) because of an error. "
                        + "Monitoring will be retried in one minute."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        Logger.EventCode.InterfaceMonitoringStopped);
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

        // Reads the packet header and queues the packet as a detection if it matches a detection rule.
        private void ProcessPacket(byte[] buffer)
        {
            PacketHeader packetHeader = new PacketHeader(buffer);

            packetHeader.MatchType = PacketMatcher.GetMatchType(packetHeader, Interface.IP);

            // Queue detections for the detection queue thread.
            if (packetHeader.MatchType != PacketMatch.None)
            {
                packetHeader.DestinationMac = Interface.MacAddress;
                _detectionQueue.Add(packetHeader, Interface);
            }
        }
    }
}