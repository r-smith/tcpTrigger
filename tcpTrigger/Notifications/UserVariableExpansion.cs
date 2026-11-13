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
        public const string IcmpType = "{ICMP_TYPE}";
        public const string Ttl = "{TTL}";
        public const string DetectionLog = "{DETECTION_LOG}";
        public const string DetectionCount = "{DETECTION_COUNT}";
        public const string DestinationServices = "{DESTINATION_SERVICES}";

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
            message = message.Replace(IcmpType, header.MatchType == PacketMatch.IcmpRequest ? header.IcmpTypeName : string.Empty);
            message = message.Replace(Ttl, header.Ttl.ToString());
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
        public static string GetExpandedString(string message, IPAddress sourceIp, List<PacketHeader> detections)
        {
            string destinationIps = string.Join(", ", detections.Select(d => d.DestinationIP.ToString()).Distinct());
            string destinationMacs = string.Join(", ", detections.Select(d => d.DestinationMacAsString).Distinct());
            string detectionLog = string.Join(Environment.NewLine, detections.Select(GetDetectionLogLine));

            message = message.Replace(SensorName, Environment.MachineName);
            message = message.Replace(SourceIp, sourceIp?.ToString());
            if (message.Contains(SourceHostname))
            {
                message = message.Replace(SourceHostname, GetHostname(sourceIp));
            }
            message = message.Replace(DestinationIp, destinationIps);
            message = message.Replace(OldInterfaceIp, destinationIps);
            message = message.Replace(DestinationMac, destinationMacs);
            message = message.Replace(DestinationServices, GetDestinationServices(detections));
            message = message.Replace(DetectionCount, detections.Count.ToString());

            // The detection log goes in last, so nothing in it is taken for a variable.
            message = message.Replace(DetectionLog, detectionLog);
            message = message.Replace(OldConnectionLog, detectionLog);

            return message;
        }

        // Returns a one-line description of a detection, for {DETECTION_LOG}.
        private static string GetDetectionLogLine(PacketHeader packetHeader)
        {
            string logText;
            switch (packetHeader.MatchType)
            {
                case PacketMatch.TcpConnect:
                    logText = $"TCP connection to port {packetHeader.DestinationPort} from {packetHeader.SourceIP}";
                    break;
                case PacketMatch.UdpCommunication:
                    logText = $"UDP communication to port {packetHeader.DestinationPort} from {packetHeader.SourceIP}";
                    break;
                case PacketMatch.IcmpRequest:
                    logText = $"ICMP {packetHeader.IcmpTypeName} request from {packetHeader.SourceIP}";
                    break;
                default:
                    logText = string.Empty;
                    break;
            }
            return $"{packetHeader.Timestamp.ToString(Settings.TimestampFormat)} [{packetHeader.DestinationIP}] {logText}";
        }

        // Returns the services in the detections, each listed once, in ignore rule syntax:
        // TCP ports lowest first, then UDP ports, then icmp. For example, "tcp:22, tcp:443, udp:53, icmp".
        private static string GetDestinationServices(List<PacketHeader> detections)
        {
            IEnumerable<string> tcp = detections
                .Where(d => d.MatchType == PacketMatch.TcpConnect)
                .Select(d => d.DestinationPort)
                .Distinct()
                .OrderBy(port => port)
                .Select(port => $"tcp:{port}");
            IEnumerable<string> udp = detections
                .Where(d => d.MatchType == PacketMatch.UdpCommunication)
                .Select(d => d.DestinationPort)
                .Distinct()
                .OrderBy(port => port)
                .Select(port => $"udp:{port}");
            IEnumerable<string> icmp = detections.Any(d => d.MatchType == PacketMatch.IcmpRequest)
                ? new[] { "icmp" }
                : Enumerable.Empty<string>();

            return string.Join(", ", tcp.Concat(udp).Concat(icmp));
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