namespace tcpTrigger
{
    // Decides whether a captured packet matches the configured detection rules.
    internal static class PacketMatcher
    {
        // Returns the type of match for a packet sent to the given interface address,
        // or PacketMatch.None if the packet doesn't match any rule.
        public static PacketMatch GetMatchType(PacketHeader header, uint interfaceAddress)
        {
            if (DoesPacketMatchIcmp(header, interfaceAddress))
            {
                return PacketMatch.IcmpRequest;
            }
            if (DoesPacketMatchTcp(header, interfaceAddress))
            {
                return PacketMatch.TcpConnect;
            }
            if (DoesPacketMatchUdp(header, interfaceAddress))
            {
                return PacketMatch.UdpCommunication;
            }
            return PacketMatch.None;
        }

        private static bool DoesPacketMatchIcmp(PacketHeader header, uint interfaceAddress)
        {
            return Settings.IsDetectIcmpEnabled
                && header.ProtocolType == Protocol.Icmp
                && header.DestinationAddress == interfaceAddress
                && (header.IcmpType == (int)IcmpTypeCode.Ping
                    || header.IcmpType == (int)IcmpTypeCode.Timestamp
                    || header.IcmpType == (int)IcmpTypeCode.Netmask)
                && !IsIgnored(header);
        }

        private static bool DoesPacketMatchTcp(PacketHeader header, uint interfaceAddress)
        {
            return Settings.IsDetectTcpEnabled
                && header.ProtocolType == Protocol.Tcp
                && header.DestinationAddress == interfaceAddress
                && Settings.TcpPortsToMonitor.Contains(header.DestinationPort)
                && IsConnectionAttempt(header.TcpFlags)
                && !IsIgnored(header);
        }

        private static bool DoesPacketMatchUdp(PacketHeader header, uint interfaceAddress)
        {
            return Settings.IsDetectUdpEnabled
                && header.ProtocolType == Protocol.Udp
                && header.DestinationAddress == interfaceAddress
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

        // Returns true if the TCP flags indicate a connection attempt or probe: ACK and RST are not set.
        // Every packet after the first SYN has ACK set, including the FIN that closes a connection.
        private static bool IsConnectionAttempt(byte tcpFlags)
        {
            const byte RST = 0x04;
            const byte ACK = 0x10;

            return (tcpFlags & (ACK | RST)) == 0;
        }
    }

}