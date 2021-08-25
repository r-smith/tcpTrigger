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
               && (header.TcpFlags == 0x2           /*           SYN */
                   || header.TcpFlags == 0xc2       /* CWR, ECE, SYN */
                   || header.TcpFlags == 0x82       /* CWR,      SYN */
                   || header.TcpFlags == 0x42))     /*      ECE, SYN */
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

        private bool DoesPacketMatchDHCP(PacketHeader header, TcpTriggerInterface iface)
        {
            if (Settings.IsMonitorDhcpEnabled
                && header.DhcpServerAddress != null
                && !Settings.IgnoredDhcpServers.Contains(header.DhcpServerAddress)
                && header.DhcpTransactionId != iface.DhcpLastTransactionId)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}