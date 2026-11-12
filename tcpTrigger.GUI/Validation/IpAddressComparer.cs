using System.Collections.Generic;

namespace tcpTrigger.GUI.Validation
{
    internal class IpAddressComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            int[] startX = ParseStartAddress(x);
            int[] startY = ParseStartAddress(y);
            for (int i = 0; i < startX.Length; i++)
            {
                if (startX[i] != startY[i])
                {
                    return startX[i].CompareTo(startY[i]);
                }
            }

            // Same first address: sort by length, so a single address comes before a range or CIDR block.
            return x.Length.CompareTo(y.Length);
        }

        // The octets of the first address in a range (192.168.0.1-100) or CIDR block (192.168.0.0/24).
        // Anything that isn't a number is 0.
        private static int[] ParseStartAddress(string ip)
        {
            string[] parts = ip.Split('-', '/')[0].Split('.');
            int[] octets = new int[4];
            for (int i = 0; i < octets.Length && i < parts.Length; i++)
            {
                int.TryParse(parts[i], out octets[i]);
            }
            return octets;
        }
    }
}