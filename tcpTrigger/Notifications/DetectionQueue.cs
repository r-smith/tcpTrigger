using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace tcpTrigger
{
    // Runs the notifications for detections on a single background thread,
    // so slow notifications don't delay packet capture.
    internal class DetectionQueue
    {
        // Maximum time Stop waits for queued detections to finish.
        private static readonly TimeSpan s_stopTimeout = TimeSpan.FromSeconds(10);

        // How often the thread checks for emails that are due, while any batches are waiting.
        private static readonly TimeSpan s_emailCheckInterval = TimeSpan.FromSeconds(1);

        // Maximum time to wait for the last emails to send when stopping. Less than s_stopTimeout.
        private static readonly TimeSpan s_lastEmailsTimeout = TimeSpan.FromSeconds(5);

        // Detections waiting for their notifications to run.
        private readonly BlockingCollection<PacketHeader> _detections = new BlockingCollection<PacketHeader>();
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
        public void Add(PacketHeader header)
        {
            try
            {
                _detections.Add(header);
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
                    Logger.EventCode.QueuedDetectionsDropped);
            }
        }

        // Runs the notifications for each detection (on the background thread) in the order they were queued.
        // Also sends the emails that are due, checking once a second while any batches are waiting.
        private void RunNotifications()
        {
            // The loop ends once Stop is called and the queue is empty.
            while (!_detections.IsCompleted)
            {
                try
                {
                    // Wait for the next detection. If email batches are waiting, wake at least once a second so emails go out on time.
                    TimeSpan timeout = NotificationRunner.HasEmailBatches ? s_emailCheckInterval : Timeout.InfiniteTimeSpan;
                    if (_detections.TryTake(out PacketHeader detection, timeout))
                    {
                        NotificationRunner.Run(detection);
                    }
                    NotificationRunner.SendDueEmails();
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        "tcpTrigger failed to run the notifications for a detection."
                        + $"{Environment.NewLine}{Environment.NewLine}{ex.GetType().Name}: {ex.Message}",
                        Logger.EventCode.NotificationFailed);
                }
            }

            // Send the emails still waiting, so stopping or reloading doesn't lose them.
            Task.WaitAll(NotificationRunner.SendAllEmails(), s_lastEmailsTimeout);
        }
    }
}