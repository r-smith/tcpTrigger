using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace tcpTrigger.Manager
{
    public class IPAddressComparer : IComparer<IPAddress>
    {
        // Sort list of IP addresses so that IPv4 addresses appear sorted first
        // followed by sorted IPv6 addresses.
        public int Compare(IPAddress a, IPAddress b)
        {
            // Get byte array of each IP address.
            byte[] bytesA = a.GetAddressBytes();
            byte[] bytesB = b.GetAddressBytes();

            // Compare length of byte array. IPv4 addresses (smaller byte array) should be sorted first.
            if (bytesA.Length < bytesB.Length)
                return -1;
            if (bytesA.Length > bytesB.Length)
                return 1;

            // Byte arrays are equal length. Sort by comparing one byte at a time.
            int i = 0;
            while (i < bytesA.Length && i < bytesB.Length)
            {
                if (bytesA[i] > bytesB[i])
                    return 1;
                else if (bytesA[i] < bytesB[i])
                    return -1;
                else
                    i++;
            }
            // All bytes in both arrays are equal.
            return 0;
        }
    }

    public class PortComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            if (string.IsNullOrWhiteSpace(x) && string.IsNullOrWhiteSpace(y))
                return 0;
            else if (string.IsNullOrWhiteSpace(y))
                return -1;
            else if (string.IsNullOrWhiteSpace(x))
                return 1;

            Regex regex = new Regex("^(\\d+)");
            Match xMatch = regex.Match(x);
            Match yMatch = regex.Match(y);

            if (xMatch.Success && yMatch.Success)
            {
                return int.Parse(xMatch.Groups[1].Value)
                    .CompareTo(int.Parse(yMatch.Groups[1].Value));
            }

            return x.CompareTo(y);
        }
    }
}