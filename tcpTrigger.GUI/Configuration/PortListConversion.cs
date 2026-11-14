using System;
using System.Collections.Generic;
using System.Linq;
using tcpTrigger.GUI.Models;
using tcpTrigger.GUI.Validation;

namespace tcpTrigger.GUI.Configuration
{
    // The legacy configuration chose which ports to include or exclude. Ignore rules do that
    // job now. This turns port lists into an ignore rule.
    internal static class PortListConversion
    {
        // The longest port list, in characters, to show in a rule's name,
        // such as "22, 3389, 8080".
        private const int MaxPortsLengthInName = 40;

        // `protocol` is "tcp" or "udp". An empty include list means all ports.
        // Returns null if the lists didn't leave out any ports.
        // Throws FormatException if a list isn't valid.
        public static IgnoreRule ToIgnoreRule(string protocol, string include, string exclude)
        {
            bool isAllPorts = string.IsNullOrWhiteSpace(include) || PortList.Format(include) == PortList.AllPorts;
            bool[] watched = PortList.GetIncludedPorts(isAllPorts ? PortList.AllPorts : include);
            bool[] excluded = PortList.GetIncludedPorts(exclude);

            // Ignore every port that wasn't being watched.
            bool[] ignored = new bool[PortList.MaxPort + 1];
            for (int port = PortList.MinPort; port <= PortList.MaxPort; port++)
            {
                watched[port] = watched[port] && !excluded[port];
                ignored[port] = !watched[port];
            }

            List<string> ignoredRanges = PortList.ToRanges(ignored);
            if (ignoredRanges.Count == 0)
            {
                return null;
            }

            // TCP ports are written without a protocol prefix.
            string prefix = protocol == "udp" ? "udp:" : string.Empty;

            DateTime now = DateTime.Now;
            return new IgnoreRule
            {
                IsEnabled = true,
                Name = GetName(protocol.ToUpperInvariant(), isAllPorts, exclude, watched),
                Sources = new List<string> { "any" },
                Services = ignoredRanges.Select(range => prefix + range).ToList(),
                Created = now,
                Modified = now
            };
        }

        // Describes the rule, such as "Only detect TCP ports 22, 3389".
        private static string GetName(string label, bool isAllPorts, string exclude, bool[] watched)
        {
            if (isAllPorts)
            {
                string excludedPorts = PortList.Format(exclude).Replace(",", ", ");
                return excludedPorts.Length <= MaxPortsLengthInName
                    ? $"Don't detect {label} ports {excludedPorts}"
                    : $"{label} ports excluded in earlier settings";
            }

            string watchedPorts = string.Join(", ", PortList.ToRanges(watched));
            if (watchedPorts.Length == 0)
            {
                return $"Don't detect any {label} ports";
            }
            return watchedPorts.Length <= MaxPortsLengthInName
                ? $"Only detect {label} ports {watchedPorts}"
                : $"Only detect the {label} ports from earlier settings";
        }
    }
}