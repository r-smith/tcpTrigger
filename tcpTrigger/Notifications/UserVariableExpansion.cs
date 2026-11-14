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
        public const string SourceOsGuess = "{SOURCE_OS_GUESS}";
        public const string DestinationIp = "{DESTINATION_IP}";
        public const string DestinationPort = "{DESTINATION_PORT}";
        public const string DestinationHostname = "{DESTINATION_HOSTNAME}";
        public const string DestinationMac = "{DESTINATION_MAC}";
        public const string Timestamp = "{TIMESTAMP}";
        public const string DetectionType = "{DETECTION_TYPE}";
        public const string Protocol = "{PROTOCOL}";
        public const string TcpFlags = "{TCP_FLAGS}";
        public const string IcmpType = "{ICMP_TYPE}";
        public const string Ttl = "{TTL}";
        public const string DetectionLog = "{DETECTION_LOG}";
        public const string DetectionCount = "{DETECTION_COUNT}";
        public const string DestinationServices = "{DESTINATION_SERVICES}";

        // The {DETECTION_TYPE} of TCP, UDP, and ICMP detections.
        private const string ConnectionDetectionType = "Connection";

        // More services than this are given as a count for each protocol, such as "1,000 TCP ports".
        private const int MaxListedServices = 15;

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
            message = message.Replace(SourceOsGuess, GetSourceOsGuess(header.Ttl));
            message = message.Replace(Timestamp, header.Timestamp.ToString(Settings.TimestampFormat));
            message = message.Replace(DetectionType, ConnectionDetectionType);
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
            message = message.Replace(SourceOsGuess, string.Join(", ", detections.Select(d => GetSourceOsGuess(d.Ttl)).Distinct()));
            message = message.Replace(DestinationIp, destinationIps);
            message = message.Replace(OldInterfaceIp, destinationIps);
            message = message.Replace(DestinationMac, destinationMacs);
            message = message.Replace(DestinationServices, GetDestinationServices(detections));
            message = message.Replace(DetectionType, ConnectionDetectionType);
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
                    logText = $"Tried connecting to TCP port {packetHeader.DestinationPort} on {packetHeader.DestinationIP}";
                    break;
                case PacketMatch.UdpCommunication:
                    logText = $"Sent a UDP packet to port {packetHeader.DestinationPort} on {packetHeader.DestinationIP}";
                    break;
                case PacketMatch.IcmpRequest:
                    logText = $"Sent an ICMP {packetHeader.IcmpTypeName} request to {packetHeader.DestinationIP}";
                    break;
                default:
                    logText = string.Empty;
                    break;
            }
            return $"{packetHeader.Timestamp.ToString(Settings.TimestampFormat)}  {logText}";
        }

        // Returns the services in the detections, each listed once, in ignore rule syntax:
        // TCP ports lowest first, then UDP ports, then icmp. For example, "tcp:22, tcp:443, udp:53, icmp".
        // If there are more than MaxListedServices, the ports are counted instead, such as "1,000 TCP ports, icmp".
        private static string GetDestinationServices(List<PacketHeader> detections)
        {
            List<ushort> tcpPorts = GetDistinctPorts(detections, PacketMatch.TcpConnect);
            List<ushort> udpPorts = GetDistinctPorts(detections, PacketMatch.UdpCommunication);
            bool hasIcmp = detections.Any(d => d.MatchType == PacketMatch.IcmpRequest);

            List<string> services = new List<string>();
            if (tcpPorts.Count + udpPorts.Count + (hasIcmp ? 1 : 0) <= MaxListedServices)
            {
                services.AddRange(tcpPorts.Select(port => $"tcp:{port}"));
                services.AddRange(udpPorts.Select(port => $"udp:{port}"));
            }
            else
            {
                if (tcpPorts.Count > 0)
                {
                    services.Add(GetPortCount(tcpPorts.Count, "TCP"));
                }
                if (udpPorts.Count > 0)
                {
                    services.Add(GetPortCount(udpPorts.Count, "UDP"));
                }
            }
            if (hasIcmp)
            {
                services.Add("icmp");
            }

            return string.Join(", ", services);
        }

        // Returns the destination ports of one type of detection, each listed once.
        private static List<ushort> GetDistinctPorts(List<PacketHeader> detections, PacketMatch matchType)
        {
            return detections
                .Where(d => d.MatchType == matchType)
                .Select(d => d.DestinationPort)
                .Distinct()
                .OrderBy(port => port)
                .ToList();
        }

        // Returns a count of ports, such as "1 UDP port" or "1,000 TCP ports".
        private static string GetPortCount(int count, string protocol)
        {
            return count == 1 ? $"1 {protocol} port" : $"{count:N0} {protocol} ports";
        }

        // Guesses the source's OS from the packet's TTL. Each OS starts packets at a fixed TTL.
        private static string GetSourceOsGuess(byte ttl)
        {
            if (ttl <= 64)
            {
                return "Linux/macOS/Unix";
            }
            if (ttl <= 128)
            {
                return "Windows";
            }
            return "Network device";
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