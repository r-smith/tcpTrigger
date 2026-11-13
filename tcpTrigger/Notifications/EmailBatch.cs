using System;
using System.Collections.Generic;
using System.Linq;

namespace tcpTrigger
{
    // Buffers detections from one source IP for one email notification, and sends them together
    // in one email once the wait time has passed and the rate limit allows it.
    internal class EmailBatch : IDisposable
    {
        private readonly NotificationEmail _email;

        // Protects _buffer, _interfaces, and _sendTimer, which the detection queue thread and the timer both use.
        private readonly object _lock = new object();

        // Detections waiting to be sent in the next email.
        private string _buffer = string.Empty;

        // The interfaces those detections were on. Used for the interface variables.
        private List<TcpTriggerInterface> _interfaces = new List<TcpTriggerInterface>();

        private readonly System.Timers.Timer _sendTimer;

        // When the last email was sent, in UTC. Used to apply the rate limit.
        private DateTime _lastSent;

        public EmailBatch(NotificationEmail email)
        {
            _email = email;

            _sendTimer = new System.Timers.Timer
            {
                Interval = TimeSpan.FromSeconds(_email.WaitSeconds).TotalMilliseconds
            };
            _sendTimer.Elapsed += SendTimer_Elapsed;
            _sendTimer.Enabled = false;
        }

        // True when no detections are waiting and the rate limit has passed, so the batch can be removed.
        public bool IsIdle
        {
            get
            {
                lock (_lock)
                {
                    return _buffer.Length == 0 && _lastSent <= DateTime.UtcNow.AddSeconds(-_email.RateLimitSeconds);
                }
            }
        }

        // Adds a detection to the buffer and starts the timer, which sends the email later.
        public void Add(string detection, TcpTriggerInterface ipInterface)
        {
            lock (_lock)
            {
                _buffer += detection + Environment.NewLine;
                if (!_interfaces.Any(existing => existing.IP.Equals(ipInterface.IP)))
                {
                    _interfaces.Add(ipInterface);
                }
                _sendTimer.Enabled = true;
            }
        }

        public void Dispose()
        {
            _sendTimer.Dispose();
        }

        // Sends the buffered detections, or delays the timer if an email was sent too recently.
        private void SendTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_lastSent > DateTime.UtcNow.AddSeconds(-_email.RateLimitSeconds))
            {
                // Too soon to send another email. Apply rate limiting by changing the timer interval.
                double secondsSinceLastEmail = (DateTime.UtcNow - _lastSent).TotalSeconds;
                if (secondsSinceLastEmail > 0 && secondsSinceLastEmail < _email.RateLimitSeconds)
                {
                    _sendTimer.Interval = TimeSpan.FromSeconds(_email.RateLimitSeconds - secondsSinceLastEmail).TotalMilliseconds;
                }
                else
                {
                    _sendTimer.Interval = TimeSpan.FromSeconds(_email.RateLimitSeconds).TotalMilliseconds;
                }
            }
            else
            {
                // Copy and reset the buffer and interfaces, record the time, and reset the timer.
                string connectionLog;
                List<TcpTriggerInterface> interfaces;
                lock (_lock)
                {
                    connectionLog = _buffer;
                    interfaces = _interfaces;
                    _buffer = string.Empty;
                    _interfaces = new List<TcpTriggerInterface>();
                    _lastSent = DateTime.UtcNow;
                    _sendTimer.Interval = TimeSpan.FromSeconds(_email.WaitSeconds).TotalMilliseconds;
                    _sendTimer.Enabled = false;
                }

                // Send the email. Not awaited. EmailNotifier.SendAsync catches and logs its own errors.
                _ = EmailNotifier.SendAsync(_email, interfaces, connectionLog);
            }
        }
    }
}