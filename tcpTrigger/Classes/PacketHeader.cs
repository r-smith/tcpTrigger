using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace tcpTrigger
{
    public enum Protocol
    {
        ICMP = 1,
        TCP = 6,
        UDP = 17,
        Unknown = -1
    }

    public enum PacketMatch
    {
        IcmpRequest,
        TcpConnect,
        UdpCommunication,
        None
    }

    public enum IcmpTypeCode
    {
        ping = 8,
        timestamp = 13,
        netmask = 17
    }

    class PacketHeader
    {
        public IPAddress SourceIP { get; set; }
        public IPAddress DestinationIP { get; set; }
        public ushort SourcePort { get; set; }
        public ushort DestinationPort { get; set; }
        public PhysicalAddress DestinationMac { get; set; }
        public string DestinationMacAsString
        {
            get
            {
                return string.Join("-", (from x in DestinationMac.GetAddressBytes() select x.ToString("X2")).ToArray());
            }
        }
        public byte TcpFlags { get; set; }
        public byte IcmpType { get; set; }
        public Protocol ProtocolType { get; set; }
        public PacketMatch MatchType { get; set; }

        public string TcpFlagsAsString
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendFormat("0x{0:X2} (", TcpFlags);

                if ((TcpFlags & 0x01) != 0)
                    sb.Append("FIN,");
                if ((TcpFlags & 0x02) != 0)
                    sb.Append("SYN,");
                if ((TcpFlags & 0x04) != 0)
                    sb.Append("RST,");
                if ((TcpFlags & 0x08) != 0)
                    sb.Append("PSH,");
                if ((TcpFlags & 0x10) != 0)
                    sb.Append("ACK,");
                if ((TcpFlags & 0x20) != 0)
                    sb.Append("URG,");
                if ((TcpFlags & 0x40) != 0)
                    sb.Append("ECE,");
                if ((TcpFlags & 0x80) != 0)
                    sb.Append("CWR,");

                return sb.ToString().TrimEnd(',') + ")";
            }
        }

        public PacketHeader(byte[] buffer)
        {
            MatchType = PacketMatch.None;

            // Read the protocol number - byte 9.
            ProtocolType = GetProtocolFromByte(buffer[9]);

            // We only want TCP, UDP, or ICMP packets.
            if (ProtocolType == Protocol.Unknown)
                return;

            // Read the IP header length from the low 4 bits of byte 0 (the high 4 bits are the IP version).
            // The value is a count of 32-bit words, so multiply by 4 for the length in bytes (at most 60).
            int headerLength = (buffer[0] & 0x0F) * 4;

            // Read the source IP address - starting at byte 12 (32 bits).
            SourceIP = new IPAddress(BitConverter.ToUInt32(buffer, 12));

            // Read the destination IP address - starting at byte 16 (32 bits).
            DestinationIP = new IPAddress(BitConverter.ToUInt32(buffer, 16));

            switch (ProtocolType)
            {
                case Protocol.ICMP:
                    // Read the ICMP Type field - starting at byte [IP header length] + 0 (8 bits).
                    IcmpType = buffer[headerLength];
                    break;

                case Protocol.TCP:
                    // Read the TCP source port - starting at byte [IP header length] + 0 (16 bits).
                    SourcePort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength));

                    // Read the TCP destination port - starting at byte [IP header length] + 2 (16 bits).
                    DestinationPort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength + 2));

                    // Read the TCP flag fields - eight 1-bit flags starting at byte [IP header length] + 13
                    // (I'm skipping over the ECN nonce (NS) flag).
                    TcpFlags = buffer[headerLength + 13];
                    break;

                case Protocol.UDP:
                    // Read the UDP source port - starting at byte [IP header length] + 0 (16 bits).
                    SourcePort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength));

                    // Read the UDP destination port - starting at byte [IP header length] + 2 (16 bits).
                    DestinationPort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength + 2));
                    break;

                case Protocol.Unknown:
                    break;

                default:
                    break;
            }
        }

        private Protocol GetProtocolFromByte(byte b)
        {
            switch (b)
            {
                case (int)Protocol.ICMP:
                    return Protocol.ICMP;
                case (int)Protocol.TCP:
                    return Protocol.TCP;
                case (int)Protocol.UDP:
                    return Protocol.UDP;
                default:
                    return Protocol.Unknown;
            }
        }
    }
}
