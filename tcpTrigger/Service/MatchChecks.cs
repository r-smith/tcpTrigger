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
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchTCP(PacketHeader header, IPAddress ip)
        {
            if (Settings.IsMonitorTcpEnabled &&
                header.ProtocolType == Protocol.TCP &&
                header.TcpFlags == 0x2 &&
                header.DestinationIP.Equals(ip) &&
                Settings.TcpPortsToMonitor.Contains(header.DestinationPort))
            {
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchUDP(PacketHeader header, IPAddress ip)
        {
            if (Settings.IsMonitorUdpEnabled &&
                header.ProtocolType == Protocol.UDP &&
                header.DestinationIP.Equals(ip) &&
                Settings.UdpPortsToMonitor.Contains(header.DestinationPort))
            {
                return true;
            }

            return false;
        }

        private bool DoesPacketMatchDHCP(PacketHeader header, IPAddress ip)
        {
            if (Settings.IsMonitorDhcpEnabled &&
                header.DhcpServerAddress != null)
            {
                return true;
            }

            return false;
        }
    }
}