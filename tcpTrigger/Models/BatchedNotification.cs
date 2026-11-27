using System.Collections.Generic;
using System.Net;

namespace tcpTrigger
{
    // A notification that sends detections in batches, one batch per source IP. Detections are collected for
    // the wait time and sent together, at most once per rate limit.
    internal abstract class BatchedNotification : Notification
    {
        public int RateLimitSeconds { get; set; }
        public int WaitSeconds { get; set; }

        // Detections waiting to be sent, one batch per source IP. Only used on the detection queue thread.
        public Dictionary<IPAddress, DetectionBatch> Batches { get; } = new Dictionary<IPAddress, DetectionBatch>();
    }
}
