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
            return x.IP.Equals(y.IP) && x.MacAddress.Equals(y.MacAddress);
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
        public List<IPAddress> DiscoveredDhcpServers { get; set; }
        
        public TcpTriggerInterface(IPAddress address, string description, PhysicalAddress macAddress, string guid)
        {
            IP = address;
            Description = description;
            MacAddress = macAddress;
            Guid = guid;

            RateLimitDictionary = new Dictionary<IPAddress, DateTime>();
            DiscoveredDhcpServers = new List<IPAddress>();
            NetworkSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
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
