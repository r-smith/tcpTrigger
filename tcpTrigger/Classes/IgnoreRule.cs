using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace tcpTrigger
{
    // An enabled ignore rule from the configuration. Packets that match it don't trigger notifications.
    // A packet matches if its source, destination, and service are all in the rule's lists.
    internal class IgnoreRule
    {
        private const string Any = "any";

        public string Name { get; }

        // Source IP addresses, such as 192.168.0.1, 192.168.0.1-100, or 192.168.0.0/24. Or just "any".
        public List<string> Sources { get; }

        // Destination IP addresses (this computer's own), in the same forms as Sources. Or just "any".
        public List<string> Destinations { get; }

        // Ports and protocols, such as "443", "tcp:8000-9000", "udp:53", or "icmp". Or just "any".
        public List<string> Services { get; }

        // Entries that aren't valid. They're skipped.
        public List<string> InvalidEntries { get; } = new List<string>();

        // False if the rule can't be used because its source or service list is empty,
        // or a list has no valid entries.
        public bool IsUsable => _sourceRanges.Count > 0 && _destinationRanges.Count > 0 && _serviceRanges.Count > 0;

        // Sources, Destinations, and Services as number ranges, parsed when the rule is created.
        private readonly List<AddressRange> _sourceRanges;
        private readonly List<AddressRange> _destinationRanges;
        private readonly List<ServiceRange> _serviceRanges;

        public IgnoreRule(string name, List<string> sources, List<string> destinations, List<string> services)
        {
            Name = name;
            Sources = sources;
            Destinations = destinations.Count == 0 ? new List<string> { Any } : destinations;
            Services = services;

            _sourceRanges = ParseAddresses(Sources);
            _destinationRanges = ParseAddresses(Destinations);
            _serviceRanges = ParseServices(Services);
        }

        // Returns true if the packet matches this rule.
        public bool Matches(PacketHeader header)
        {
            uint source = ToNumber(header.SourceIP);
            uint destination = ToNumber(header.DestinationIP);

            return _sourceRanges.Any(range => range.Contains(source))
                && _destinationRanges.Any(range => range.Contains(destination))
                && _serviceRanges.Any(service => service.Matches(header.ProtocolType, header.DestinationPort));
        }

        private List<AddressRange> ParseAddresses(List<string> entries)
        {
            List<AddressRange> ranges = new List<AddressRange>();
            foreach (string entry in entries)
            {
                AddressRange range = ParseAddress(entry.Trim().ToLowerInvariant());
                if (range == null)
                {
                    InvalidEntries.Add(entry);
                }
                else
                {
                    ranges.Add(range);
                }
            }
            return ranges;
        }

        private List<ServiceRange> ParseServices(List<string> entries)
        {
            List<ServiceRange> services = new List<ServiceRange>();
            foreach (string entry in entries)
            {
                ServiceRange service = ParseService(entry.Trim().ToLowerInvariant());
                if (service == null)
                {
                    InvalidEntries.Add(entry);
                }
                else
                {
                    services.Add(service);
                }
            }
            return services;
        }

        // Parses "any", a single IPv4 address (192.168.0.1), a range (192.168.0.1-100),
        // or CIDR notation (192.168.0.0/24). Returns null if the entry isn't valid.
        private static AddressRange ParseAddress(string entry)
        {
            if (entry == Any)
            {
                return new AddressRange(uint.MinValue, uint.MaxValue);
            }

            // A range: the number after '-' is the last octet of the range's end.
            if (entry.Contains('-'))
            {
                string[] rangeParts = entry.Split('-');
                if (rangeParts.Length != 2
                    || !TryParseIPv4(rangeParts[0], out uint start)
                    || !byte.TryParse(rangeParts[1], out byte endOctet)
                    || endOctet <= (start & 0xFF))
                {
                    return null;
                }
                return new AddressRange(start, (start & 0xFFFFFF00) | endOctet);
            }

            // A single address, or CIDR notation.
            string[] parts = entry.Split('/');
            if (parts.Length > 2 || !TryParseIPv4(parts[0], out uint ip))
            {
                return null;
            }
            if (parts.Length == 1)
            {
                return new AddressRange(ip, ip);
            }
            if (!int.TryParse(parts[1], out int prefix) || prefix < 0 || prefix > 32)
            {
                return null;
            }

            // A /0 would shift by 32, which C# treats as a shift by 0. Handle it separately.
            uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
            return new AddressRange(ip & mask, (ip & mask) | ~mask);
        }

        // Parses "any", a protocol (tcp, udp, icmp), a port (443), a port range (8000-9000),
        // or either of those with "tcp:" or "udp:" in front. A port without a protocol is for TCP and UDP.
        // Returns null if the entry isn't valid.
        private static ServiceRange ParseService(string entry)
        {
            switch (entry)
            {
                case Any:
                    return new ServiceRange(isTcp: true, isUdp: true, isIcmp: true, ushort.MinValue, ushort.MaxValue);
                case "tcp":
                    return new ServiceRange(isTcp: true, isUdp: false, isIcmp: false, ushort.MinValue, ushort.MaxValue);
                case "udp":
                    return new ServiceRange(isTcp: false, isUdp: true, isIcmp: false, ushort.MinValue, ushort.MaxValue);
                case "icmp":
                    return new ServiceRange(isTcp: false, isUdp: false, isIcmp: true, ushort.MinValue, ushort.MaxValue);
            }

            bool isTcp = true;
            bool isUdp = true;
            string ports = entry;
            if (entry.StartsWith("tcp:", StringComparison.Ordinal))
            {
                isUdp = false;
                ports = entry.Substring("tcp:".Length);
            }
            else if (entry.StartsWith("udp:", StringComparison.Ordinal))
            {
                isTcp = false;
                ports = entry.Substring("udp:".Length);
            }

            string[] range = ports.Split('-');
            if (range.Length > 2
                || !TryParsePort(range[0], out ushort start)
                || !TryParsePort(range[range.Length - 1], out ushort end)
                || start > end)
            {
                return null;
            }
            return new ServiceRange(isTcp, isUdp, isIcmp: false, start, end);
        }

        // Converts an IPv4 address to a number. Returns false if it isn't valid.
        private static bool TryParseIPv4(string text, out uint ip)
        {
            ip = 0;
            string[] octets = text.Split('.');
            if (octets.Length != 4)
            {
                return false;
            }

            foreach (string octet in octets)
            {
                if (!byte.TryParse(octet, out byte value))
                {
                    return false;
                }
                ip = (ip << 8) | value;
            }
            return true;
        }

        private static bool TryParsePort(string text, out ushort port)
        {
            return ushort.TryParse(text, out port) && port > 0;
        }

        // Returns the IPv4 address as a number, so it can be compared with a range.
        private static uint ToNumber(IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }

        // A range of IPv4 addresses, stored as numbers.
        private class AddressRange
        {
            public uint Start { get; }
            public uint End { get; }

            public AddressRange(uint start, uint end)
            {
                Start = start;
                End = end;
            }

            public bool Contains(uint ip)
            {
                return ip >= Start && ip <= End;
            }
        }

        // The protocols a service entry applies to, and its ports for TCP and UDP.
        private class ServiceRange
        {
            public bool IsTcp { get; }
            public bool IsUdp { get; }
            public bool IsIcmp { get; }
            public ushort StartPort { get; }
            public ushort EndPort { get; }

            public ServiceRange(bool isTcp, bool isUdp, bool isIcmp, ushort startPort, ushort endPort)
            {
                IsTcp = isTcp;
                IsUdp = isUdp;
                IsIcmp = isIcmp;
                StartPort = startPort;
                EndPort = endPort;
            }

            // ICMP has no ports, so only the protocol is checked.
            public bool Matches(Protocol protocol, ushort port)
            {
                switch (protocol)
                {
                    case Protocol.Icmp:
                        return IsIcmp;
                    case Protocol.Tcp:
                        return IsTcp && port >= StartPort && port <= EndPort;
                    case Protocol.Udp:
                        return IsUdp && port >= StartPort && port <= EndPort;
                    default:
                        return false;
                }
            }
        }
    }
}