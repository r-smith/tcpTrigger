using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace tcpTrigger
{
    public class TcpTriggerInterfaceComparer : IEqualityComparer<TcpTriggerInterface>
    {
        public bool Equals(TcpTriggerInterface x, TcpTriggerInterface y)
        {
            return x.IP.Equals(y.IP) && x.MacAddress.Equals(y.MacAddress);
        }

        public int GetHashCode(TcpTriggerInterface obj)
        {
            return obj.IP.GetHashCode();
        }
    }

    public class TcpTriggerInterface
    {
        public Mutex Mutex { get; private set; } = new Mutex();
        public string Guid { get; }
        public IPAddress IP { get; }
        public PhysicalAddress MacAddress { get; }
        public string MacAddressAsString
        {
            get
            {
                return string.Join("-", (from x in MacAddress.GetAddressBytes() select x.ToString("X2")).ToArray());
            }
        }
        public string Description { get; }
        public Socket NetworkSocket { get; }
        public List<IPAddress> DiscoveredDhcpServers { get; set; }
        public DateTime EmailLastSentTimestamp { get; private set; }
        public string EmailLogBuffer { get; set; }
        public System.Timers.Timer EmailSendTimer { get; private set; }

        // Constructor.
        public TcpTriggerInterface(IPAddress address, string description, PhysicalAddress macAddress, string guid)
        {
            IP = address;
            Description = description;
            MacAddress = macAddress;
            Guid = guid;

            DiscoveredDhcpServers = new List<IPAddress>();
            NetworkSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            EmailSendTimer = new System.Timers.Timer();
            EmailSendTimer.Interval = TimeSpan.FromSeconds(Settings.EmailBufferSeconds).TotalMilliseconds;
            EmailSendTimer.Elapsed += EmailBufferTimer_Elapsed;
            EmailSendTimer.Enabled = false;
        }

        // Methods.
        private void EmailBufferTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (EmailLastSentTimestamp != null
                && EmailLastSentTimestamp > DateTime.Now.AddSeconds(-Settings.EmailRateLimitSeconds))
            {
                // Too soon to send another email. Apply rate limiting by changing the timer interval.
                double secondsSinceLastEmail = (DateTime.Now - EmailLastSentTimestamp).TotalSeconds;
                if (secondsSinceLastEmail > 0 && secondsSinceLastEmail < Settings.EmailRateLimitSeconds)
                {
                    // Set timer interval to rate limit setting minus seconds since the last send email.
                    EmailSendTimer.Interval = TimeSpan.FromSeconds(Settings.EmailRateLimitSeconds - secondsSinceLastEmail).TotalMilliseconds;
                }
                else
                {
                    // Set timer interval to rate limit setting.
                    EmailSendTimer.Interval = TimeSpan.FromSeconds(Settings.EmailRateLimitSeconds).TotalMilliseconds;
                }
            }
            else
            {
                // Send email using current log buffer.
                tcpTrigger.SendEmail(this);

                // Email sent. Record current timestamp, reset log buffer, and reset timer settings.
                Mutex.WaitOne();
                EmailLastSentTimestamp = DateTime.Now;
                EmailLogBuffer = string.Empty;
                EmailSendTimer.Interval = TimeSpan.FromSeconds(Settings.EmailBufferSeconds).TotalMilliseconds;
                EmailSendTimer.Enabled = false;
                Mutex.ReleaseMutex();
            }
        }
    }
}
