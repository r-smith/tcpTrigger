using System;
using System.Collections.Generic;
using System.Linq;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Configuration
{
    // The ignore rule that's added when UDP detection is turned on. UDP has no connections, so
    // tcpTrigger can't tell unsolicited traffic from replies to this computer's own traffic.
    // This rule leaves out the ports where those replies, and other everyday traffic, arrive.
    internal static class CommonUdpTrafficRule
    {
        public const string Name = "Common UDP traffic";

        // DHCP, NTP, NetBIOS, RDP, mDNS, and the range Windows uses for the source port of
        // outgoing traffic, where replies arrive.
        public static readonly string[] Ports = { "68", "123", "137", "3389", "5353", "49152-65535" };

        public static IgnoreRule Create()
        {
            DateTime now = DateTime.Now;
            return new IgnoreRule
            {
                IsEnabled = true,
                Name = Name,
                Sources = new List<string> { "any" },
                Destinations = new List<string> { "any" },
                Services = GetServices(),
                Created = now,
                Modified = now
            };
        }

        // Returns the rule, or null if there isn't one. Found by name, so a renamed copy
        // is treated as one of the user's own rules.
        public static IgnoreRule Find(IEnumerable<IgnoreRule> rules)
        {
            return rules.FirstOrDefault(rule => rule.Name == Name);
        }

        // True if the rule is still exactly as Create made it. Only then is it removed when UDP
        // detection is turned off. A rule the user has changed is theirs to keep.
        public static bool IsUnchanged(IgnoreRule rule)
        {
            return rule.Name == Name
                && rule.IsEnabled
                && rule.Sources.SequenceEqual(new[] { "any" })
                && rule.Destinations.SequenceEqual(new[] { "any" })
                && rule.Services.SequenceEqual(GetServices());
        }

        private static List<string> GetServices()
        {
            return Ports.Select(port => "udp:" + port).ToList();
        }
    }
}
