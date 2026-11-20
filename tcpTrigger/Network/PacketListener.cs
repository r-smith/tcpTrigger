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
            // Buffer for incoming data, reused for every packet.
            byte[] buffer = new byte[512];

            // Bind socket to IP endpoint.
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            _socket.Bind(new IPEndPoint(Interface.IP, 0));
            _socket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), null);

            // Callback method for processing captured packets.
            void OnReceive(IAsyncResult ar)
            {
                // Complete the receive, as required for every BeginReceive.
                SocketError error;
                int bytesReceived;
                try
                {
                    bytesReceived = _socket.EndReceive(ar, out error);
                }
                catch (ObjectDisposedException)
                {
                    // The listener was stopped. Stop receiving.
                    return;
                }

                // Stop on any error except MessageSize, which only means the packet was larger than the buffer.
                if (error != SocketError.Success && error != SocketError.MessageSize)
                {
                    SetFailed(new SocketException((int)error).Message);
                    return;
                }

                // A packet larger than the buffer fills all of it.
                if (error == SocketError.MessageSize)
                {
                    bytesReceived = buffer.Length;
                }

                // Process the packet. Log any error and keep listening.
                try
                {
                    ProcessPacket(buffer, bytesReceived);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        $"tcpTrigger failed to read a packet on network interface {Interface.Name} ({Interface.IP}). "
                        + "Monitoring continues."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.GetType().Name}: {ex.Message}",
                        Logger.EventCode.PacketProcessingFailed);
                }

                // Continue listening for new data.
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
                    SetFailed(ex.Message);
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

        // Marks the listener as failed, so the network check restarts it, and logs the error.
        private void SetFailed(string errorMessage)
        {
            _hasFailed = true;
            Logger.WriteError(
                $"tcpTrigger stopped monitoring network interface {Interface.Name} ({Interface.IP}) because of an error. "
                + "Monitoring will be retried shortly."
                + $"{Environment.NewLine}{Environment.NewLine}{errorMessage}",
                Logger.EventCode.InterfaceMonitoringStopped);
        }

        // Reads the packet header and queues the packet as a detection if it matches a detection rule.
        private void ProcessPacket(byte[] buffer, int length)
        {
            PacketHeader packetHeader = new PacketHeader(buffer, length);

            packetHeader.MatchType = PacketMatcher.GetMatchType(packetHeader, Interface.IP);

            // Queue detections for the detection queue thread.
            if (packetHeader.MatchType != PacketMatch.None)
            {
                packetHeader.Timestamp = DateTime.Now;
                packetHeader.DestinationMac = Interface.MacAddress;
                _detectionQueue.Add(packetHeader);
            }
        }
    }
}