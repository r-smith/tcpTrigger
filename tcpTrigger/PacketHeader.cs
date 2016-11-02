using System;
using System.Net;
using System.Text;

namespace tcpTrigger
{
    public enum Protocol
    {
        ICMP = 1,
        TCP = 6,
        UDP = 17,
        Unknown = -1
    };

    public enum PacketMatch
    {
        PingRequest,
        TcpConnect,
        NamePoison,
        None
    }

    class PacketHeader
    {
        public IPAddress SourceIP { get; set; }
        public IPAddress DestinationIP { get; set; }
        public ushort SourcePort { get; set; }
        public ushort DestinationPort { get; set; }
        public byte TcpFlags { get; set; }
        public byte IcmpType { get; set; }
        public Protocol ProtocolType { get; set; }
        public ushort NetbiosTransactionId { get; set; }
        public bool IsNameQueryResponse { get; set; }
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

            // Read the first byte from the buffer.
            // This should include the Version (4 bits) and Internet Header Length (4 bits) fields.
            var headerLength = buffer[0];

            // We just want the header length in bytes, so drop the first 4 bits.
            headerLength <<= 4;
            headerLength >>= 4;

            // The value of the IHL field is the number of 32-bit words.  Multiplying by 4 gives us the number of bytes.
            headerLength *= 4;

            // Safety check.  IP protocol defines a max header length of 60 bytes.
            if (headerLength > 60)
                return;

            // Read the source IP address - starting at byte 12 (32 bits).
            SourceIP = new IPAddress(BitConverter.ToUInt32(buffer, 12));

            // Read the destination IP address - starting at byte 16 (32 bits).
            DestinationIP = new IPAddress(BitConverter.ToUInt32(buffer, 16));

            switch (ProtocolType)
            {
                case (Protocol.ICMP):
                    // Read the ICMP Type field - starting at byte [IP header length] + 0 (8 bits).
                    IcmpType = buffer[headerLength];
                    break;
                case (Protocol.TCP):
                    // Read the TCP source port - starting at byte [IP header length] + 0 (16 bits).
                    SourcePort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength));

                    // Read the TCP destination port - starting at byte [IP header length] + 2 (16 bits).
                    DestinationPort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength + 2));

                    // Read the TCP flag fields - eight 1-bit flags starting at byte [IP header length] + 13
                    // (I'm skipping over the ECN nonce (NS) flag).
                    TcpFlags = buffer[headerLength + 13];
                    break;
                case (Protocol.UDP):
                    // Read the UDP source port - starting at byte [IP header length] + 0 (16 bits).
                    SourcePort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength));

                    // Read the UDP destination port - starting at byte [IP header length] + 2 (16 bits).
                    DestinationPort = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength + 2));
                    
                    // If NetBIOS or LLMNR packet, read the Transaction ID and query response flag.
                    if (DestinationPort == 137 || DestinationPort == 5355 || SourcePort == 5355)
                    {
                        NetbiosTransactionId = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, headerLength + 8));
                        var queryFlag = buffer[headerLength + 10];
                        queryFlag >>= 7;
                        if (queryFlag == 1) IsNameQueryResponse = true; else IsNameQueryResponse = false;
                    }

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
