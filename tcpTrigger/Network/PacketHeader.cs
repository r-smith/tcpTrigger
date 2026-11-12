using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace tcpTrigger
{
    // IP protocol numbers, as found in byte 9 of the IP header.
    internal enum Protocol
    {
        Icmp = 1,
        Tcp = 6,
        Udp = 17,
        Unknown = -1
    }

    // The detection rule a packet matched.
    internal enum PacketMatch
    {
        IcmpRequest,
        TcpConnect,
        UdpCommunication,
        None
    }

    // ICMP message types that tcpTrigger detects.
    internal enum IcmpTypeCode
    {
        Ping = 8,
        Timestamp = 13,
        Netmask = 17
    }

    // The fields tcpTrigger reads from a captured packet's IP/TCP/UDP/ICMP headers.
    internal class PacketHeader
    {
        public IPAddress SourceIP { get; set; }
        public IPAddress DestinationIP { get; set; }
        public ushort SourcePort { get; set; }
        public ushort DestinationPort { get; set; }
        public PhysicalAddress DestinationMac { get; set; }
        public string DestinationMacAsString => BitConverter.ToString(DestinationMac.GetAddressBytes());
        public byte Ttl { get; set; }
        public byte TcpFlags { get; set; }
        public byte IcmpType { get; set; }
        public Protocol ProtocolType { get; set; }
        public PacketMatch MatchType { get; set; }

        // The time the packet was captured.
        public DateTime Timestamp { get; } = DateTime.Now;

        // The names of the TCP flags that are set, such as "SYN,ECE".
        public string TcpFlagNames
        {
            get
            {
                StringBuilder flags = new StringBuilder();

                if ((TcpFlags & 0x01) != 0) flags.Append("FIN,");
                if ((TcpFlags & 0x02) != 0) flags.Append("SYN,");
                if ((TcpFlags & 0x04) != 0) flags.Append("RST,");
                if ((TcpFlags & 0x08) != 0) flags.Append("PSH,");
                if ((TcpFlags & 0x10) != 0) flags.Append("ACK,");
                if ((TcpFlags & 0x20) != 0) flags.Append("URG,");
                if ((TcpFlags & 0x40) != 0) flags.Append("ECE,");
                if ((TcpFlags & 0x80) != 0) flags.Append("CWR,");

                return flags.ToString().TrimEnd(',');
            }
        }

        // The protocol of the rule the packet matched: "ICMP", "TCP", "UDP", or "None" if it didn't match.
        public string MatchTypeName
        {
            get
            {
                switch (MatchType)
                {
                    case PacketMatch.IcmpRequest:
                        return "ICMP";
                    case PacketMatch.TcpConnect:
                        return "TCP";
                    case PacketMatch.UdpCommunication:
                        return "UDP";
                    default:
                        return "None";
                }
            }
        }

        // The name of the ICMP type, such as "ping", or its type code if it has no name.
        public string IcmpTypeName => Enum.GetName(typeof(IcmpTypeCode), IcmpType)?.ToLowerInvariant() ?? IcmpType.ToString();

        // Reads the header fields from a captured packet. The buffer starts with the IP header.
        public PacketHeader(byte[] buffer)
        {
            MatchType = PacketMatch.None;

            // Read the protocol number - byte 9.
            ProtocolType = GetProtocolFromByte(buffer[9]);

            // Only TCP, UDP, and ICMP packets are read.
            if (ProtocolType == Protocol.Unknown)
            {
                return;
            }

            // Read the IP header length from the low 4 bits of byte 0 (the high 4 bits are the IP version).
            // The value is a count of 32-bit words, so multiply by 4 for the length in bytes (at most 60).
            int headerLength = (buffer[0] & 0x0F) * 4;

            // Read the time to live - byte 8 (8 bits).
            Ttl = buffer[8];

            // Read the source IP address - starting at byte 12 (32 bits).
            SourceIP = new IPAddress(BitConverter.ToUInt32(buffer, 12));

            // Read the destination IP address - starting at byte 16 (32 bits).
            DestinationIP = new IPAddress(BitConverter.ToUInt32(buffer, 16));

            switch (ProtocolType)
            {
                case Protocol.Icmp:
                    // Read the ICMP Type field - starting at byte [IP header length] + 0 (8 bits).
                    IcmpType = buffer[headerLength];
                    break;

                case Protocol.Tcp:
                    // Read the TCP source port - starting at byte [IP header length] + 0 (16 bits).
                    SourcePort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength));

                    // Read the TCP destination port - starting at byte [IP header length] + 2 (16 bits).
                    DestinationPort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength + 2));

                    // Read the TCP flag fields - eight 1-bit flags starting at byte [IP header length] + 13
                    // (The ECN nonce (NS) flag in the previous byte isn't read.)
                    TcpFlags = buffer[headerLength + 13];
                    break;

                case Protocol.Udp:
                    // Read the UDP source port - starting at byte [IP header length] + 0 (16 bits).
                    SourcePort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength));

                    // Read the UDP destination port - starting at byte [IP header length] + 2 (16 bits).
                    DestinationPort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength + 2));
                    break;
            }
        }

        // Returns the protocol for an IP protocol number, or Unknown if it isn't ICMP, TCP or UDP.
        private Protocol GetProtocolFromByte(byte b)
        {
            switch (b)
            {
                case (int)Protocol.Icmp:
                    return Protocol.Icmp;
                case (int)Protocol.Tcp:
                    return Protocol.Tcp;
                case (int)Protocol.Udp:
                    return Protocol.Udp;
                default:
                    return Protocol.Unknown;
            }
        }
    }
}