using System;
using System.Collections.Generic;

namespace tcpTrigger
{
    // Detections from one source IP, waiting to be sent in one email.
    internal class EmailBatch
    {
        public List<PacketHeader> Detections { get; } = new List<PacketHeader>();

        // When the first waiting detection arrived, in UTC.
        public DateTime FirstDetection { get; set; }

        // When this source's last email was sent, in UTC.
        public DateTime LastSent { get; set; }
    }
}