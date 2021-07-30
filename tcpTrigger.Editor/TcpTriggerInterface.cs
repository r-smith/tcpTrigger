using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace tcpTrigger.Editor
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

    public class TcpTriggerInterface
    {
        public string Guid { get; }
        public string Description { get; }
        public bool IsExcluded { get; set; }
        public List<IPAddress> IPAddresses { get; }
        public PhysicalAddress MacAddress { get; }
        public string MacAddressAsString
        {
            get
            {
                return string.Join(":", (from x in MacAddress.GetAddressBytes() select x.ToString("X2")).ToArray());
            }
        }
        
        public TcpTriggerInterface(string guid, string description, bool isExcluded, List<IPAddress> ipAddresses, PhysicalAddress macAddress)
        {
            Guid = guid;
            Description = description;
            IsExcluded = isExcluded;
            IPAddresses = ipAddresses;
            MacAddress = macAddress;
        }
    }
}
