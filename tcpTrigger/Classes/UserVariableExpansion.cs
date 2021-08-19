using System.Net;

namespace tcpTrigger
{
    internal static class UserVariableExpansion
    {
        public const string InterfaceIp = "{INTERFACE_IP}";
        public const string InterfaceMac = "{INTERFACE_MAC}";
        public const string InterfaceDescription = "{INTERFACE_DESCRIPTION}";
        public const string ConnectionLog = "{CONNECTION_LOG}";
        public const string SourceIp = "{SOURCE_IP}";
        public const string DestinationIp = "{DESTINATION_IP}";
        public const string SourcePort = "{SOURCE_PORT}";
        public const string DestinationPort = "{DESTINATION_PORT}";
        public const string SourceHostname = "{SOURCE_HOSTNAME}";
        public const string DestinationHostname = "{DESTINATION_HOSTNAME}";
        public const string DestinationMac = "{DESTINATION_MAC}";
        public const string DhcpServerIp = "{DHCP_SERVER_IP}";
        public const string TcpFlags = "{TCP_FLAGS}";
        
        public static string GetExpandedString(string message, PacketHeader header)
        {
            if (message.Contains(SourceIp))
                message = message.Replace(SourceIp, header.SourceIP?.ToString());

            if (message.Contains(DestinationIp))
                message = message.Replace(DestinationIp, header.DestinationIP?.ToString());

            if (message.Contains(SourcePort))
                message = message.Replace(SourcePort, header.SourcePort.ToString());

            if (message.Contains(DestinationPort))
                message = message.Replace(DestinationPort, header.DestinationPort.ToString());

            if (message.Contains(DestinationMac))
                message = message.Replace(DestinationMac, header.DestinationMacAsString);

            if (message.Contains(DhcpServerIp))
                message = message.Replace(DhcpServerIp, header.DhcpServerAddress?.ToString());

            if (message.Contains(TcpFlags))
                message = message.Replace(TcpFlags, header.TcpFlagsAsString);

            if (message.Contains(SourceHostname))
            {
                string hostname = string.Empty;
                try
                {
                    hostname = Dns.GetHostEntry(header.SourceIP).HostName;
                }
                catch
                { }
                message = message.Replace(SourceHostname, hostname);
            }

            if (message.Contains(DestinationHostname))
            {
                string hostname = string.Empty;
                try
                {
                    hostname = Dns.GetHostEntry(header.DestinationIP).HostName;
                }
                catch
                { }
                message = message.Replace(DestinationHostname, hostname);
            }

            return message;
        }

        public static string GetExpandedString(string message, TcpTriggerInterface ipInterface)
        {
            if (message.Contains(InterfaceIp))
                message = message.Replace(InterfaceIp, ipInterface.IP?.ToString());
            if (message.Contains(InterfaceMac))
                message = message.Replace(InterfaceMac, ipInterface.MacAddressAsString);
            if (message.Contains(InterfaceDescription))
                message = message.Replace(InterfaceDescription, ipInterface.Description);
            if (message.Contains(ConnectionLog))
                message = message.Replace(ConnectionLog, ipInterface.EmailLogBuffer);

            return message;
        }
    }
}
