using System;
using System.Net;

namespace tcpTrigger.Monitor
{
    public enum MatchType
    {
        ICMP,
        TCP,
        UDP,
        DHCP
    }

    class DetectionEvent
    {
        public DateTime Timestamp { get; set; }
        public MatchType Type { get; set; }
        public IPAddress SourceIP { get; set; }
        public IPAddress DestinationIP { get; set; }
        public int SourcePort { get; set; }
        public int DestinationPort { get; set; }
    }
}
