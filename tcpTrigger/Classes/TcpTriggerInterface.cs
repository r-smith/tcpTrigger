using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace tcpTrigger
{
    public class TcpTriggerInterfaceComparer : IEqualityComparer<TcpTriggerInterface>
    {
        public bool Equals(TcpTriggerInterface x, TcpTriggerInterface y)
        {
            return x.IP.Equals(y.IP) && x.SubnetMask.Equals(y.SubnetMask) && x.MacAddress.Equals(y.MacAddress);
        }

        public int GetHashCode(TcpTriggerInterface obj)
        {
            return obj.IP.GetHashCode();
        }
    }

    public class TcpTriggerInterface
    {
        public string Guid { get; }
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
        public Socket NetworkSocket { get; }
        public Dictionary<IPAddress, DateTime> RateLimitDictionary { get; set; }
        public List<IPAddress> DiscoveredDhcpServerList { get; set; }
        
        public TcpTriggerInterface(IPAddress address, IPAddress subnetMask, string description, PhysicalAddress macAddress, string guid)
        {
            IP = address;
            SubnetMask = subnetMask;
            BroadcastAddress = GetBroadcastAddress(address, subnetMask);
            Description = description;
            MacAddress = macAddress;
            Guid = guid;
            RateLimitDictionary = new Dictionary<IPAddress, DateTime>();
            DiscoveredDhcpServerList = new List<IPAddress>();

            NetworkSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
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

        public void RateLimitDictionaryCleanup(int rateLimitSeconds)
        {
            if (rateLimitSeconds <= 0)
                return;

            var ipAddressToDelete = new List<IPAddress>();
            foreach (KeyValuePair<IPAddress, DateTime> entry in RateLimitDictionary)
            {
                if (entry.Value <= DateTime.Now.AddSeconds(-rateLimitSeconds))
                {
                    ipAddressToDelete.Add(entry.Key);
                }
            }

            ipAddressToDelete.ForEach(x => RateLimitDictionary.Remove(x));
        }
    }
}
