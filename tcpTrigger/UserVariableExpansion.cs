using System.Net;

namespace tcpTrigger
{
    static class UserVariableExpansion
    {
        public const string SourceIp = "#SOURCEIP#";
        public const string DestinationIp = "#DESTINATIONIP#";
        public const string SourcePort = "#SOURCEPORT#";
        public const string DestinationPort = "#DESTINATIONPORT#";
        public const string SourceHostname = "#SOURCEHOSTNAME#";
        public const string DestinationHostname = "#DESTINATIONHOSTNAME#";
        public const string TcpFlags = "#TCPFLAGS#";

        public static string GetExpandedString(string message, PacketHeader header)
        {
            if (message.Contains(SourceIp))
                message = message.Replace(SourceIp, header.SourceIP.ToString());

            if (message.Contains(DestinationIp))
                message = message.Replace(DestinationIp, header.DestinationIP.ToString());

            if (message.Contains(SourcePort))
                message = message.Replace(SourcePort, header.SourcePort.ToString());

            if (message.Contains(DestinationPort))
                message = message.Replace(DestinationPort, header.DestinationPort.ToString());

            if (message.Contains(TcpFlags))
                message = message.Replace(TcpFlags, header.TcpFlagsAsString);

            if (message.Contains(SourceHostname))
            {
                var hostname = string.Empty;
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
                var hostname = string.Empty;
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
    }
}
