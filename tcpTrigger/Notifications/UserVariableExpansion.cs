using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace tcpTrigger
{
    // Replaces {VARIABLE} placeholders in user-configured text with details of a detection.
    internal static class UserVariableExpansion
    {
        public const string SensorName = "{SENSOR_NAME}";
        public const string SourceIp = "{SOURCE_IP}";
        public const string SourcePort = "{SOURCE_PORT}";
        public const string SourceHostname = "{SOURCE_HOSTNAME}";
        public const string DestinationIp = "{DESTINATION_IP}";
        public const string DestinationPort = "{DESTINATION_PORT}";
        public const string DestinationHostname = "{DESTINATION_HOSTNAME}";
        public const string DestinationMac = "{DESTINATION_MAC}";
        public const string Timestamp = "{TIMESTAMP}";
        public const string Protocol = "{PROTOCOL}";
        public const string TcpFlags = "{TCP_FLAGS}";
        public const string DetectionLog = "{DETECTION_LOG}";

        // Names used by version 1.3.5 and earlier. Still replaced so their configurations keep working.
        private const string OldConnectionLog = "{CONNECTION_LOG}";
        private const string OldInterfaceIp = "{INTERFACE_IP}";

        // Replaces the variables for a command: the details of one detection.
        public static string GetExpandedString(string message, PacketHeader header)
        {
            message = message.Replace(SensorName, Environment.MachineName);
            message = message.Replace(SourceIp, header.SourceIP?.ToString());
            message = message.Replace(SourcePort, header.SourcePort.ToString());
            message = message.Replace(DestinationIp, header.DestinationIP?.ToString());
            message = message.Replace(DestinationPort, header.DestinationPort.ToString());
            message = message.Replace(DestinationMac, header.DestinationMacAsString);
            message = message.Replace(TcpFlags, header.TcpFlagNames);
            message = message.Replace(Timestamp, header.Timestamp.ToString(Settings.TimestampFormat));
            message = message.Replace(Protocol, header.MatchTypeName);

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

        // Replaces the variables for an email. Each email is for one source IP.
        // If that source IP hit more than one interface, {DESTINATION_IP} and {DESTINATION_MAC} list each, separated by commas.
        public static string GetExpandedString(string message, IPAddress sourceIp, List<TcpTriggerInterface> interfaces, string detectionLog)
        {
            string destinationIps = string.Join(", ", interfaces.Select(i => i.IP));
            string destinationMacs = string.Join(", ", interfaces.Select(i => i.MacAddressAsString));

            message = message.Replace(SensorName, Environment.MachineName);
            message = message.Replace(SourceIp, sourceIp?.ToString());
            if (message.Contains(SourceHostname))
            {
                message = message.Replace(SourceHostname, GetHostname(sourceIp));
            }
            message = message.Replace(DestinationIp, destinationIps);
            message = message.Replace(OldInterfaceIp, destinationIps);
            message = message.Replace(DestinationMac, destinationMacs);

            // The detection log goes in last, so nothing in it is taken for a variable.
            message = message.Replace(DetectionLog, detectionLog);
            message = message.Replace(OldConnectionLog, detectionLog);

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