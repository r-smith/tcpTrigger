using System;
using System.Collections.Generic;

namespace tcpTrigger.GUI.Validation
{
    // A port list is a comma-separated list of ports and port ranges, such as "22,80,8000-8080".
    // Used for both TCP and UDP.
    internal static class PortList
    {
        public const string AllPorts = "1-65535";
        public const int MinPort = 1;
        public const int MaxPort = 65535;

        // Returns the port list in a consistent format: sorted, duplicates removed,
        // and consecutive ports collapsed into ranges. Throws FormatException if the list is invalid.
        public static string Format(string ports)
        {
            return string.Join(",", ToRanges(GetIncludedPorts(ports)));
        }

        // Returns which ports the list includes, indexed by port number, so included[80] is
        // true if port 80 is in the list. An empty list includes no ports.
        // Throws FormatException if the list is invalid.
        public static bool[] GetIncludedPorts(string ports)
        {
            bool[] included = new bool[MaxPort + 1];
            if (string.IsNullOrWhiteSpace(ports))
            {
                return included;
            }

            // Each part is checked before it's expanded, so a typo like "1-65535000" fails right away.
            foreach (string part in ports.Trim().Trim(',').Split(','))
            {
                if (!TryParseRange(part, out int start, out int end))
                {
                    throw new FormatException($"'{part.Trim()}' is not a valid port or port range. Ports must be between {MinPort} and {MaxPort}.");
                }

                for (int port = start; port <= end; port++)
                {
                    included[port] = true;
                }
            }
            return included;
        }

        // The reverse of GetIncludedPorts: lists the included ports in order, with consecutive
        // ports collapsed into ranges, such as { "22", "80", "81", "8000-8080" }.
        public static List<string> ToRanges(bool[] included)
        {
            List<string> ranges = new List<string>();
            for (int current = MinPort; current <= MaxPort; current++)
            {
                if (!included[current])
                {
                    continue;
                }

                // Find the end of this run of consecutive ports.
                int runStart = current;
                while (current < MaxPort && included[current + 1])
                {
                    current++;
                }

                // Two consecutive ports are written as "80", "81". Longer runs as "80-90".
                if (runStart == current)
                {
                    ranges.Add(runStart.ToString());
                }
                else if (runStart + 1 == current)
                {
                    ranges.Add(runStart.ToString());
                    ranges.Add(current.ToString());
                }
                else
                {
                    ranges.Add($"{runStart}-{current}");
                }
            }
            return ranges;
        }

        public static bool IsValidPort(string port)
        {
            return TryParsePort(port, out _);
        }

        // A port range is two ports separated by a hyphen, with the second port higher.
        public static bool IsValidPortRange(string portRange)
        {
            string[] parts = portRange.Split('-');
            return parts.Length == 2
                && TryParsePort(parts[0], out int startPort)
                && TryParsePort(parts[1], out int endPort)
                && endPort > startPort;
        }

        private static bool TryParsePort(string text, out int port)
        {
            return int.TryParse(text, out port) && port >= MinPort && port <= MaxPort;
        }

        // Parses one part of a port list: a single port ("80") or a range ("8000-8080").
        // A single port is returned as a range where start and end are equal.
        private static bool TryParseRange(string part, out int start, out int end)
        {
            start = 0;
            end = 0;

            string[] bounds = part.Split('-');
            if (bounds.Length > 2)
            {
                return false;
            }

            return TryParsePort(bounds[0], out start)
                && TryParsePort(bounds[bounds.Length - 1], out end)
                && start <= end;
        }
    }
}