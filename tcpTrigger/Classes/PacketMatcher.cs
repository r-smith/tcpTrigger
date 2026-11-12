using System.Net;

namespace tcpTrigger
{
    // Decides whether a captured packet matches the configured detection rules.
    internal static class PacketMatcher
    {
        // Returns the type of match for a packet sent to the given interface IP,
        // or PacketMatch.None if the packet doesn't match any rule.
        public static PacketMatch GetMatchType(PacketHeader header, IPAddress ip)
        {
            if (DoesPacketMatchIcmp(header, ip))
            {
                return PacketMatch.IcmpRequest;
            }
            if (DoesPacketMatchTcp(header, ip))
            {
                return PacketMatch.TcpConnect;
            }
            if (DoesPacketMatchUdp(header, ip))
            {
                return PacketMatch.UdpCommunication;
            }
            return PacketMatch.None;
        }

        private static bool DoesPacketMatchIcmp(PacketHeader header, IPAddress ip)
        {
            return Settings.IsDetectIcmpEnabled
                && header.ProtocolType == Protocol.Icmp
                && header.DestinationIP.Equals(ip)
                && (header.IcmpType == (int)IcmpTypeCode.Ping
                    || header.IcmpType == (int)IcmpTypeCode.Timestamp
                    || header.IcmpType == (int)IcmpTypeCode.Netmask)
                && !IsIgnored(header);
        }

        private static bool DoesPacketMatchTcp(PacketHeader header, IPAddress ip)
        {
            return Settings.IsDetectTcpEnabled
                && header.ProtocolType == Protocol.Tcp
                && header.DestinationIP.Equals(ip)
                && Settings.TcpPortsToMonitor.Contains(header.DestinationPort)
                && IsConnectionAttempt(header.TcpFlags)
                && !IsIgnored(header);
        }

        private static bool DoesPacketMatchUdp(PacketHeader header, IPAddress ip)
        {
            return Settings.IsDetectUdpEnabled
                && header.ProtocolType == Protocol.Udp
                && header.DestinationIP.Equals(ip)
                && Settings.UdpPortsToMonitor.Contains(header.DestinationPort)
                && !IsIgnored(header);
        }

        // Returns true if the packet matches an ignore rule.
        private static bool IsIgnored(PacketHeader header)
        {
            foreach (IgnoreRule rule in Settings.IgnoreRules)
            {
                if (rule.Matches(header))
                {
                    return true;
                }
            }
            return false;
        }

        // Returns true if the TCP flags indicate a connection attempt:
        // SYN is set, and ACK, RST, FIN are not.
        private static bool IsConnectionAttempt(byte tcpFlags)
        {
            const byte FIN = 0x01;
            const byte SYN = 0x02;
            const byte RST = 0x04;
            const byte ACK = 0x10;

            return (tcpFlags & (SYN | ACK | RST | FIN)) == SYN;
        }
    }

}