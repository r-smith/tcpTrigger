using System;
using System.Collections.Concurrent;
using System.Threading;

namespace tcpTrigger
{
    // Runs the notifications for detections on a single background thread,
    // so slow notifications don't delay packet capture.
    internal class DetectionQueue
    {
        // Maximum time Stop waits for queued detections to finish.
        private static readonly TimeSpan s_stopTimeout = TimeSpan.FromSeconds(10);

        // Detections waiting for their notifications to run, with the interface each was captured on.
        private readonly BlockingCollection<(PacketHeader Header, TcpTriggerInterface Interface)> _detections =
            new BlockingCollection<(PacketHeader Header, TcpTriggerInterface Interface)>();
        private Thread _worker;

        // Starts the background thread that runs notifications.
        public void Start()
        {
            _worker = new Thread(RunNotifications)
            {
                IsBackground = true,
                Name = "tcpTrigger notifications"
            };
            _worker.Start();
        }

        // Queues a detection. Called by the packet listeners.
        public void Add(PacketHeader header, TcpTriggerInterface ipInterface)
        {
            try
            {
                _detections.Add((header, ipInterface));
            }
            catch (InvalidOperationException)
            {
                // The queue has been stopped. A detection that arrives while the service is stopping is dropped.
            }
        }

        // Stops accepting detections, then waits for the queued ones to finish.
        public void Stop()
        {
            _detections.CompleteAdding();
            if (!_worker.Join(s_stopTimeout))
            {
                Logger.WriteError(
                    $"tcpTrigger stopped before all queued detections finished. Notifications for detections still queued after {s_stopTimeout.TotalSeconds} seconds were not run.",
                    Logger.EventCode.QueuedActionsDropped);
            }
        }

        // Runs the notifications for each detection (on the background thread) in the order they were queued.
        private void RunNotifications()
        {
            // Take each detection as it's queued. The loop ends once Stop is called and the queue is empty.
            foreach (var detection in _detections.GetConsumingEnumerable())
            {
                try
                {
                    NotificationRunner.Run(detection.Header, detection.Interface);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        "tcpTrigger failed to run the notifications for a detection."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.GetType().Name}: {ex.Message}",
                        Logger.EventCode.ActionFailed);
                }
            }
        }
    }
}