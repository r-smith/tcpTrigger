using System;
using System.Collections.Generic;

namespace tcpTrigger
{
    // Detections from one source IP, waiting to be sent in one email.
    internal class EmailBatch
    {
        public List<string> Detections { get; } = new List<string>();

        // The interfaces those detections were on, each listed once.
        public List<TcpTriggerInterface> Interfaces { get; } = new List<TcpTriggerInterface>();

        // When the first waiting detection arrived, in UTC.
        public DateTime FirstDetection { get; set; }

        // When this source's last email was sent, in UTC.
        public DateTime LastSent { get; set; }
    }
}