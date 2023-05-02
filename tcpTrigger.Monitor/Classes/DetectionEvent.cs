using System;
using System.Net;

namespace tcpTrigger.Monitor
{
    public enum MatchType
    {
        ICMP,
        TCP,
        UDP
    }

    class DetectionEvent
    {
        public DateTime Timestamp { get; set; }
        public MatchType Type { get; set; }
        public string Action {
            get
            {
                switch (Type)
                {
                    case MatchType.ICMP:
                        return "ICMP request";
                    case MatchType.TCP:
                        return "TCP connection";
                    case MatchType.UDP:
                        return "UDP connection";
                    default:
                        return string.Empty;
                }
            }
        }
        public IPAddress SourceIP { get; set; }
        public IPAddress DestinationIP { get; set; }
        public int SourcePort { get; set; }
        public int DestinationPort { get; set; }
    }
}
