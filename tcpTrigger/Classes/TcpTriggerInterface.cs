using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

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
        public Socket NetworkSocket { get; set; }
        public DateTime EmailLastSentTimestamp { get; private set; }
        public object EmailLock { get; } = new object();
        public string EmailLogBuffer { get; set; }
        public System.Timers.Timer EmailSendTimer { get; private set; }

        // Constructor.
        public TcpTriggerInterface(IPAddress address, string description, PhysicalAddress macAddress, string guid)
        {
            IP = address;
            Description = description;
            MacAddress = macAddress;
            Guid = guid;

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
                // Copy and reset the log buffer, record the time, and reset the timer.
                string connectionLog;
                lock (EmailLock)
                {
                    connectionLog = EmailLogBuffer;
                    EmailLogBuffer = string.Empty;
                    EmailLastSentTimestamp = DateTime.Now;
                    EmailSendTimer.Interval = TimeSpan.FromSeconds(Settings.EmailBufferSeconds).TotalMilliseconds;
                    EmailSendTimer.Enabled = false;
                }

                // Send the email. Not awaited. SendEmail catches and logs its own errors.
                _ = TcpTriggerService.SendEmail(this, connectionLog);
            }
        }

        public static List<TcpTriggerInterface> GetInterfaces()
        {
            var networkInterfaces = new List<TcpTriggerInterface>();

            // Enumerate network interfaces. Determine and record which interfaces to listen on.
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Skip: Adapters excluded by user configuration,
                //       loopback adapters,
                //       adapters that aren't currently up,
                //       and adapters that don't support IPv4.
                if (Settings.ExcludedNetworkInterfaces.Contains(networkInterface.Id)
                    || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || networkInterface.OperationalStatus != OperationalStatus.Up
                    || networkInterface.Supports(NetworkInterfaceComponent.IPv4) == false)
                {
                    continue;
                }

                // Get adapter properties.
                foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    // Skip if IP address is not IPv4.
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    // Add adapter to tcpTrigger network interface list.
                    networkInterfaces.Add(
                        new TcpTriggerInterface(
                            address: address.Address,
                            description: networkInterface.Description,
                            macAddress: networkInterface.GetPhysicalAddress(),
                            guid: networkInterface.Id)
                        );
                }
            }

            return networkInterfaces;
        }
    }
}
