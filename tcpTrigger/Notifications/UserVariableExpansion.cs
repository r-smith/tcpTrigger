using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace tcpTrigger
{
    // Replaces {VARIABLE} placeholders in user-configured text with details of a match.
    internal static class UserVariableExpansion
    {
        public const string InterfaceIp = "{INTERFACE_IP}";
        public const string InterfaceMac = "{INTERFACE_MAC}";
        public const string InterfaceDescription = "{INTERFACE_DESCRIPTION}";
        public const string ConnectionLog = "{CONNECTION_LOG}";

        public const string SourceIp = "{SOURCE_IP}";
        public const string SourcePort = "{SOURCE_PORT}";
        public const string SourceHostname = "{SOURCE_HOSTNAME}";
        public const string DestinationIp = "{DESTINATION_IP}";
        public const string DestinationPort = "{DESTINATION_PORT}";
        public const string DestinationHostname = "{DESTINATION_HOSTNAME}";
        public const string DestinationMac = "{DESTINATION_MAC}";
        public const string Timestamp = "{TIMESTAMP}";
        public const string MatchType = "{MATCH_TYPE}";
        public const string TcpFlags = "{TCP_FLAGS}";

        // Replaces the packet variables in the text, such as {SOURCE_IP}.
        public static string GetExpandedString(string message, PacketHeader header)
        {
            message = message.Replace(SourceIp, header.SourceIP?.ToString());
            message = message.Replace(SourcePort, header.SourcePort.ToString());
            message = message.Replace(DestinationIp, header.DestinationIP?.ToString());
            message = message.Replace(DestinationPort, header.DestinationPort.ToString());
            message = message.Replace(DestinationMac, header.DestinationMacAsString);
            message = message.Replace(TcpFlags, header.TcpFlagNames);
            message = message.Replace(Timestamp, header.Timestamp.ToString(Settings.TimestampFormat));
            message = message.Replace(MatchType, header.MatchTypeName);

            if (message.Contains(SourceHostname))
            {
                message = message.Replace(SourceHostname, GetHostname(header.SourceIP));
            }
            if (message.Contains(DestinationHostname))
            {
                message = message.Replace(DestinationHostname, GetHostname(header.DestinationIP));
            }

            return message;
        }

        // Replaces the email variables in the text: {SOURCE_IP}, {SOURCE_HOSTNAME}, the interface variables,
        // and {CONNECTION_LOG}. Each interface variable lists every interface, separated by commas.
        public static string GetExpandedString(string message, IPAddress sourceIp, List<TcpTriggerInterface> interfaces, string connectionLog)
        {
            message = message.Replace(SourceIp, sourceIp?.ToString());
            if (message.Contains(SourceHostname))
            {
                message = message.Replace(SourceHostname, GetHostname(sourceIp));
            }
            message = message.Replace(InterfaceIp, string.Join(", ", interfaces.Select(i => i.IP)));
            message = message.Replace(InterfaceMac, string.Join(", ", interfaces.Select(i => i.MacAddressAsString)));
            message = message.Replace(InterfaceDescription, string.Join(", ", interfaces.Select(i => i.Description)));
            message = message.Replace(ConnectionLog, connectionLog);

            return message;
        }

        // Looks up the address's hostname in DNS. Returns an empty string if the lookup fails.
        private static string GetHostname(IPAddress address)
        {
            try
            {
                return Dns.GetHostEntry(address).HostName;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}