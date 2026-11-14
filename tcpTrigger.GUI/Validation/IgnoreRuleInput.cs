using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace tcpTrigger.GUI.Validation
{
    // Parsing and validation for the lists of an ignore rule: source IP addresses, destination
    // IP addresses, and services (ports and protocols). Each list is entered one entry per line.
    internal static class IgnoreRuleInput
    {
        // Splits the text into lines, normalizes each entry, and removes blank lines and duplicates.
        public static List<string> ParseLines(string text)
        {
            return text
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => NormalizeLine(line))
                .Where(line => line.Length > 0)
                .Distinct()
                .ToList();
        }

        public static List<string> SortAddresses(List<string> addresses)
        {
            addresses.Sort(new IpAddressComparer());
            return addresses;
        }

        // Returns the first entry that isn't a valid IP address, or null if every entry is valid.
        public static string GetFirstInvalidAddress(string text)
        {
            return ParseLines(text).FirstOrDefault(address => !IsValidAddress(address));
        }

        // Returns the first entry that isn't a valid port or protocol, or null if every entry is valid.
        public static string GetFirstInvalidService(string text)
        {
            return ParseLines(text).FirstOrDefault(service => !IsValidService(service));
        }

        // Lowercase, trim, and remove whitespace around a range's '-' or a CIDR's '/'.
        private static string NormalizeLine(string line)
        {
            return Regex.Replace(line, @"\s*([-/])\s*", "$1").ToLowerInvariant().Trim();
        }

        private static bool IsValidAddress(string ip)
        {
            // Addresses can be specified as a single IPv4 address (192.168.0.1),
            // as a range (192.168.0.1-100), or as CIDR notation (192.168.0.0/24).

            // Check for an IP range (192.168.0.1-100).
            if (ip.Contains('-'))
            {
                string[] rangeParts = ip.Split('-');
                if (rangeParts.Length != 2)
                {
                    // Invalid. More than one '-' is present.
                    return false;
                }

                string[] octets = rangeParts[0].Split('.');
                if (!AreValidOctets(octets))
                {
                    return false;
                }

                // The range value must be greater than the last octet and less than or equal to 255.
                int lastOctet = int.Parse(octets[3]);
                return int.TryParse(rangeParts[1], out int rangeValue)
                    && rangeValue > lastOctet
                    && rangeValue <= 255;
            }

            // Split the IP and CIDR part, if present.
            string[] parts = ip.Split('/');
            if (parts.Length > 2)
            {
                // Invalid. More than one '/' is present.
                return false;
            }

            if (!AreValidOctets(parts[0].Split('.')))
            {
                return false;
            }

            // Validate the CIDR prefix, if present. It must be between 0 and 32.
            if (parts.Length == 2)
            {
                return int.TryParse(parts[1], out int cidr) && cidr >= 0 && cidr <= 32;
            }

            return true;
        }

        // There must be exactly 4 octets, each between 0 and 255.
        private static bool AreValidOctets(string[] octets)
        {
            if (octets.Length != 4)
            {
                return false;
            }

            foreach (string octet in octets)
            {
                if (!int.TryParse(octet, out int value) || value < 0 || value > 255)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidService(string service)
        {
            // Valid service formats include:
            // - Protocol name (tcp, udp, icmp).
            // - Single port number (443). A port without a protocol is a TCP port.
            // - Port range (8000-9000).
            // - Protocol and single port (udp:53).
            // - Protocol and port range (tcp:8000-9000).

            const string ProtocolPattern = @"^(tcp|udp|icmp)$";
            const string PortPattern = @"^([0-9]{1,5})$";
            const string PortRangePattern = @"^([0-9]{1,5})-([0-9]{1,5})$";
            const string ProtocolPortPattern = @"^(tcp|udp):([0-9]{1,5})$";
            const string ProtocolPortRangePattern = @"^(tcp|udp):([0-9]{1,5})-([0-9]{1,5})$";

            if (Regex.IsMatch(service, ProtocolPattern))
            {
                return true;
            }
            else if (Regex.IsMatch(service, PortPattern))
            {
                return PortList.IsValidPort(service);
            }
            else if (Regex.IsMatch(service, PortRangePattern))
            {
                return PortList.IsValidPortRange(service);
            }
            else if (Regex.IsMatch(service, ProtocolPortPattern))
            {
                return PortList.IsValidPort(service.Split(':')[1]);
            }
            else if (Regex.IsMatch(service, ProtocolPortRangePattern))
            {
                return PortList.IsValidPortRange(service.Split(':')[1]);
            }
            else
            {
                return false;
            }
        }
    }
}