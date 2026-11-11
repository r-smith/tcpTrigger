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
                string hostname = string.Empty;
                try
                {
                    hostname = Dns.GetHostEntry(header.SourceIP).HostName;
                }
                catch
                {
                    // Leave the hostname empty if the lookup fails.
                }
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
                {
                    // Leave the hostname empty if the lookup fails.
                }
                message = message.Replace(DestinationHostname, hostname);
            }

            return message;
        }

        // Replaces the interface variables and {CONNECTION_LOG} in the text.
        public static string GetExpandedString(string message, TcpTriggerInterface ipInterface, string connectionLog)
        {
            message = message.Replace(InterfaceIp, ipInterface.IP?.ToString());
            message = message.Replace(InterfaceMac, ipInterface.MacAddressAsString);
            message = message.Replace(InterfaceDescription, ipInterface.Description);
            message = message.Replace(ConnectionLog, connectionLog);

            return message;
        }
    }
}