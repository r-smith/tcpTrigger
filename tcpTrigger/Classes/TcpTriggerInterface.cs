using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace tcpTrigger
{
    // Compares interfaces by IP and MAC address, to detect network changes.
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

    // A network interface address that tcpTrigger can monitor, and its email notification buffer.
    // An adapter with several IPv4 addresses has one of these per address.
    public class TcpTriggerInterface
    {
        // The network adapter's ID, as reported by Windows.
        public string Guid { get; }

        // The network adapter's IP address.
        public IPAddress IP { get; }

        // The network adapter's physical (MAC) address.
        public PhysicalAddress MacAddress { get; }
        public string MacAddressAsString => BitConverter.ToString(MacAddress.GetAddressBytes());

        // The network adapter's text description, as reported by Windows.
        public string Description { get; }

        // When the last email was sent, in UTC. Used to apply the email rate limit.
        public DateTime EmailLastSentTimestamp { get; private set; }

        // Protects EmailLogBuffer and EmailSendTimer, which the action thread and the email timer both use.
        public object EmailLock { get; } = new object();

        // Matches waiting to be sent in the next notification email.
        public string EmailLogBuffer { get; set; }

        // Sends the buffered matches once the buffer period has passed and the rate limit allows it.
        public System.Timers.Timer EmailSendTimer { get; private set; }

        public TcpTriggerInterface(IPAddress address, string description, PhysicalAddress macAddress, string guid)
        {
            IP = address;
            Description = description;
            MacAddress = macAddress;
            Guid = guid;

            EmailSendTimer = new System.Timers.Timer
            {
                Interval = TimeSpan.FromSeconds(Settings.EmailBufferSeconds).TotalMilliseconds
            };
            EmailSendTimer.Elapsed += EmailSendTimer_Elapsed;
            EmailSendTimer.Enabled = false;
        }

        // Sends the buffered matches, or delays the timer if an email was sent too recently.
        private void EmailSendTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (EmailLastSentTimestamp > DateTime.UtcNow.AddSeconds(-Settings.EmailRateLimitSeconds))
            {
                // Too soon to send another email. Apply rate limiting by changing the timer interval.
                double secondsSinceLastEmail = (DateTime.UtcNow - EmailLastSentTimestamp).TotalSeconds;
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
                    EmailLastSentTimestamp = DateTime.UtcNow;
                    EmailSendTimer.Interval = TimeSpan.FromSeconds(Settings.EmailBufferSeconds).TotalMilliseconds;
                    EmailSendTimer.Enabled = false;
                }

                // Send the email. Not awaited. EmailNotifier.SendAsync catches and logs its own errors.
                _ = EmailNotifier.SendAsync(this, connectionLog);
            }
        }

        // Returns an entry for each IPv4 address on adapters that are up, excluding loopback and
        // any adapters excluded in the configuration.
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
                    || !networkInterface.Supports(NetworkInterfaceComponent.IPv4))
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