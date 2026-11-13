using System;

namespace tcpTrigger
{
    // Buffers detections on one interface and sends them together in one email,
    // once the wait time has passed and the rate limit allows it.
    internal class EmailBatch
    {
        private readonly TcpTriggerInterface _ipInterface;

        // Protects _buffer and _sendTimer, which the action thread and the timer both use.
        private readonly object _lock = new object();

        // Detections waiting to be sent in the next email.
        private string _buffer = string.Empty;

        private readonly System.Timers.Timer _sendTimer;

        // When the last email was sent, in UTC. Used to apply the rate limit.
        private DateTime _lastSent;

        public EmailBatch(TcpTriggerInterface ipInterface)
        {
            _ipInterface = ipInterface;

            _sendTimer = new System.Timers.Timer
            {
                Interval = TimeSpan.FromSeconds(Settings.EmailBufferSeconds).TotalMilliseconds
            };
            _sendTimer.Elapsed += SendTimer_Elapsed;
            _sendTimer.Enabled = false;
        }

        // Adds a detection to the buffer and starts the timer, which sends the email later.
        public void Add(string detection)
        {
            lock (_lock)
            {
                _buffer += detection + Environment.NewLine;
                _sendTimer.Enabled = true;
            }
        }

        // Sends the buffered detections, or delays the timer if an email was sent too recently.
        private void SendTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_lastSent > DateTime.UtcNow.AddSeconds(-Settings.EmailRateLimitSeconds))
            {
                // Too soon to send another email. Apply rate limiting by changing the timer interval.
                double secondsSinceLastEmail = (DateTime.UtcNow - _lastSent).TotalSeconds;
                if (secondsSinceLastEmail > 0 && secondsSinceLastEmail < Settings.EmailRateLimitSeconds)
                {
                    _sendTimer.Interval = TimeSpan.FromSeconds(Settings.EmailRateLimitSeconds - secondsSinceLastEmail).TotalMilliseconds;
                }
                else
                {
                    _sendTimer.Interval = TimeSpan.FromSeconds(Settings.EmailRateLimitSeconds).TotalMilliseconds;
                }
            }
            else
            {
                // Copy and reset the buffer, record the time, and reset the timer.
                string connectionLog;
                lock (_lock)
                {
                    connectionLog = _buffer;
                    _buffer = string.Empty;
                    _lastSent = DateTime.UtcNow;
                    _sendTimer.Interval = TimeSpan.FromSeconds(Settings.EmailBufferSeconds).TotalMilliseconds;
                    _sendTimer.Enabled = false;
                }

                // Send the email. Not awaited. EmailNotifier.SendAsync catches and logs its own errors.
                _ = EmailNotifier.SendAsync(_ipInterface, connectionLog);
            }
        }
    }
}