using System;
using System.Collections.Concurrent;
using System.Threading;

namespace tcpTrigger
{
    // Runs the configured actions for matched packets on a single background thread,
    // so slow actions don't delay packet capture.
    internal class ActionQueue
    {
        // Maximum time Stop waits for queued actions to finish.
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

        private readonly BlockingCollection<(PacketHeader Header, TcpTriggerInterface Interface)> _matches =
            new BlockingCollection<(PacketHeader Header, TcpTriggerInterface Interface)>();
        private Thread _worker;

        // Starts the background thread that runs actions.
        public void Start()
        {
            _worker = new Thread(RunActions)
            {
                IsBackground = true,
                Name = "tcpTrigger actions"
            };
            _worker.Start();
        }

        // Queues a matched packet. Called by the packet listeners.
        public void Add(PacketHeader header, TcpTriggerInterface ipInterface)
        {
            try
            {
                _matches.Add((header, ipInterface));
            }
            catch (InvalidOperationException)
            {
                // The queue has been stopped. A match that arrives while the service is stopping is dropped.
            }
        }

        // Stops accepting matches, then waits for the queued ones to finish.
        public void Stop()
        {
            _matches.CompleteAdding();
            if (!_worker.Join(StopTimeout))
            {
                Logger.WriteError(
                    $"tcpTrigger stopped before all queued actions finished. Actions still queued after {StopTimeout.TotalSeconds} seconds were not run.",
                    Logger.EventCode.Error);
            }
        }

        private void RunActions()
        {
            // Take each match as it's queued. The loop ends once Stop is called and the queue is empty.
            foreach (var match in _matches.GetConsumingEnumerable())
            {
                // This runs on the action thread. Failed actions are logged.
                try
                {
                    ActionRunner.Run(match.Header, match.Interface);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        "A packet matched detection rules but tcpTrigger failed to perform configured actions. "
                        + ex.Message,
                        Logger.EventCode.Error);
                }
            }
        }
    }
}