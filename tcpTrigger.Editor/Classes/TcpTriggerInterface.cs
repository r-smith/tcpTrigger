using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace tcpTrigger.Editor
{
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
