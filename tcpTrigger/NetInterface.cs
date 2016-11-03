using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace tcpTrigger
{
    class NetInterface
    {
        public IPAddress IP { get; }
        public IPAddress SubnetMask { get; }
        public IPAddress BroadcastAddress { get; }
        public PhysicalAddress MacAddress { get; }
        public string MacAddressAsString
        {
            get
            {
                return string.Join("-", (from x in MacAddress.GetAddressBytes() select x.ToString("X2")).ToArray());
            }
        }
        public string Description { get; }
        public Dictionary<IPAddress, DateTime> RateLimitDictionary { get; set; }
        public List<IPAddress> DiscoveredDhcpServerList { get; set; }
        
        public NetInterface(IPAddress address, IPAddress subnetMask, string description, PhysicalAddress macAddress)
        {
            IP = address;
            SubnetMask = subnetMask;
            BroadcastAddress = GetBroadcastAddress(address, subnetMask);
            Description = description;
            MacAddress = macAddress;
            RateLimitDictionary = new Dictionary<IPAddress, DateTime>();
            DiscoveredDhcpServerList = new List<IPAddress>();
        }

        private IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
        {
            // Credit: Jean-Paul Mikkers
            // Source: https://dhcpserver.codeplex.com/

            byte[] ipAdressBytes = address.GetAddressBytes();
            byte[] subnetMaskBytes = subnetMask.GetAddressBytes();

            if (ipAdressBytes.Length != subnetMaskBytes.Length)
                throw new ArgumentException("Lengths of IP address and subnet mask do not match.");

            byte[] broadcastAddress = new byte[ipAdressBytes.Length];
            for (int i = 0; i < broadcastAddress.Length; i++)
            {
                broadcastAddress[i] = (byte)(ipAdressBytes[i] | (subnetMaskBytes[i] ^ 255));
            }
            return new IPAddress(broadcastAddress);
        }

        public void RateLimitDictionaryCleanup(int rateLimitMinutes)
        {
            if (rateLimitMinutes <= 0)
                return;

            var ipAddressToDelete = new List<IPAddress>();
            foreach (KeyValuePair<IPAddress, DateTime> entry in RateLimitDictionary)
            {
                if (entry.Value <= DateTime.Now.AddMinutes(-rateLimitMinutes))
                {
                    ipAddressToDelete.Add(entry.Key);
                }
            }

            ipAddressToDelete.ForEach(x => RateLimitDictionary.Remove(x));
        }
    }
}
