using System.Collections.Generic;
using System.Net;
using System.ServiceProcess;

namespace tcpTrigger
{
    partial class TcpTriggerService : ServiceBase
    {
        private bool DoesPacketMatchICMP(PacketHeader header, IPAddress ip)
        {
            return Settings.IsMonitorIcmpEnabled
                && header.ProtocolType == Protocol.ICMP
                && header.DestinationIP.Equals(ip)
                && (header.IcmpType == (int)IcmpTypeCode.ping
                    || header.IcmpType == (int)IcmpTypeCode.timestamp
                    || header.IcmpType == (int)IcmpTypeCode.netmask)
                && !IsIgnored(header.SourceIP, Settings.IgnoreIcmp);
        }

        private bool DoesPacketMatchTCP(PacketHeader header, IPAddress ip)
        {
            return Settings.IsMonitorTcpEnabled
                && header.ProtocolType == Protocol.TCP
                && header.DestinationIP.Equals(ip)
                && Settings.TcpPortsToMonitor.Contains(header.DestinationPort)
                && IsConnectionAttempt(header.TcpFlags)
                && !IsIgnored(header.SourceIP, header.DestinationPort);
        }

        private bool DoesPacketMatchUDP(PacketHeader header, IPAddress ip)
        {
            return Settings.IsMonitorUdpEnabled
                && header.ProtocolType == Protocol.UDP
                && header.DestinationIP.Equals(ip)
                && Settings.UdpPortsToMonitor.Contains(header.DestinationPort)
                && !IsIgnored(header.SourceIP, header.DestinationPort);
        }

        // Returns true if the source IP is in the ignore list for all traffic, or for this port.
        // For ICMP, port is Settings.IgnoreIcmp.
        private static bool IsIgnored(IPAddress sourceIp, int port)
        {
            return Settings.IgnoredEndpoints.TryGetValue(sourceIp, out HashSet<int> ports)
                && (ports.Contains(Settings.IgnoreAll) || ports.Contains(port));
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