using System.Collections.Generic;

namespace tcpTrigger.GUI.Validation
{
    // Sorts an ignore rule's services: whole protocols (icmp, tcp, udp), then TCP ports, then UDP ports.
    internal class ServiceComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            int result = GetGroup(x).CompareTo(GetGroup(y));
            if (result != 0)
            {
                return result;
            }

            int[] portsX = ParsePorts(x);
            int[] portsY = ParsePorts(y);
            for (int i = 0; i < portsX.Length; i++)
            {
                if (portsX[i] != portsY[i])
                {
                    return portsX[i].CompareTo(portsY[i]);
                }
            }

            // Same ports, such as 22 and tcp:22.
            return string.CompareOrdinal(x, y);
        }

        // The whole protocols in order, then TCP ports, then UDP ports.
        // A port without a protocol is a TCP port.
        private static int GetGroup(string service)
        {
            switch (service)
            {
                case "icmp":
                    return 0;
                case "tcp":
                    return 1;
                case "udp":
                    return 2;
            }
            return service.StartsWith("udp:") ? 4 : 3;
        }

        // The first and last port of a port (443) or port range (8000-9000), after the protocol if there is one.
        private static int[] ParsePorts(string service)
        {
            string[] parts = service.Substring(service.IndexOf(':') + 1).Split('-');
            int[] ports = new int[2];
            int.TryParse(parts[0], out ports[0]);
            int.TryParse(parts[parts.Length - 1], out ports[1]);
            return ports;
        }
    }
}