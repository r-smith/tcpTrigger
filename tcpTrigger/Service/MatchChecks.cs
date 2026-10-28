using System.Collections.Generic;
using System.Net;
using System.ServiceProcess;

namespace tcpTrigger
{
    partial class tcpTrigger : ServiceBase
    {
        private bool DoesPacketMatchICMP(PacketHeader header, IPAddress ip)
        {
            if (Settings.IsMonitorIcmpEnabled
                && header.ProtocolType == Protocol.ICMP
                && header.DestinationIP.Equals(ip)
                && (header.IcmpType == (int)IcmpTypeCode.ping
                    || header.IcmpType == (int)IcmpTypeCode.timestamp
                    || header.IcmpType == (int)IcmpTypeCode.netmask))
            {
                // So far match is good. Now check if source IP is in ignore list.
                if (Settings.IgnoredEndpoints.TryGetValue(header.SourceIP, out HashSet<int> ports))
                {
                    // Source IP is in ignore list. Now check if source IP also ignores ICMP or all.
                    if (ports.Contains(Settings.IgnoreAll) || ports.Contains(Settings.IgnoreIcmp))
                    {
                        // Source IP with destination ICMP found in ignore list. Return false / no match.
                        return false;
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool DoesPacketMatchTCP(PacketHeader header, IPAddress ip)
        {
            if (Settings.IsMonitorTcpEnabled
               && header.ProtocolType == Protocol.TCP
               && header.DestinationIP.Equals(ip)
               && Settings.TcpPortsToMonitor.Contains(header.DestinationPort)
               && IsConnectionAttempt(header.TcpFlags))
            {
                // So far match is good. Now check if source IP is in ignore list.
                if (Settings.IgnoredEndpoints.TryGetValue(header.SourceIP, out HashSet<int> ports))
                {
                    // Source IP is in ignore list. Now check if source IP has matching destination port in ignore list.
                    if (ports.Contains(Settings.IgnoreAll) || ports.Contains(header.DestinationPort))
                    {
                        // Source IP and destination port combo found in ignore list. Return false / no match.
                        return false;
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
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

        private bool DoesPacketMatchUDP(PacketHeader header, IPAddress ip)
        {
            if (Settings.IsMonitorUdpEnabled
                && header.ProtocolType == Protocol.UDP
                && header.DestinationIP.Equals(ip)
                && Settings.UdpPortsToMonitor.Contains(header.DestinationPort))
            {
                // So far match is good. Now check if source IP is in ignore list.
                if (Settings.IgnoredEndpoints.TryGetValue(header.SourceIP, out HashSet<int> ports))
                {
                    // Source IP is in ignore list. Now check if destination port is also in ignore list for this source IP.
                    if (ports.Contains(Settings.IgnoreAll) || ports.Contains(header.DestinationPort))
                    {
                        // Source IP and destination port combo found in ignore list. Return false / no match.
                        return false;
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}