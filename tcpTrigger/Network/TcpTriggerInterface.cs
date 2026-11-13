using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace tcpTrigger
{
    // Compares interfaces by IP and MAC address, to detect network changes.
    internal class TcpTriggerInterfaceComparer : IEqualityComparer<TcpTriggerInterface>
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

    // A network interface address that tcpTrigger can monitor, and its email batches.
    // An adapter with several IPv4 addresses has one of these per address.
    internal class TcpTriggerInterface
    {
        // The network adapter's ID, as reported by Windows.
        public string Guid { get; }

        // The network adapter's IP address.
        public IPAddress IP { get; }

        // The network adapter's physical (MAC) address.
        public PhysicalAddress MacAddress { get; }
        public string MacAddressAsString => BitConverter.ToString(MacAddress.GetAddressBytes());

        // The network adapter's name in Windows.
        public string Name { get; }

        // The network adapter's description in Windows.
        public string Description { get; }

        // One batch for each email notification. Each buffers this interface's detections for its email.
        public Dictionary<NotificationEmail, EmailBatch> EmailBatches { get; }

        public TcpTriggerInterface(IPAddress address, string name, string description, PhysicalAddress macAddress, string guid)
        {
            IP = address;
            Name = name;
            Description = description;
            MacAddress = macAddress;
            Guid = guid;

            EmailBatches = Settings.Notifications
                .OfType<NotificationEmail>()
                .ToDictionary(email => email, email => new EmailBatch(this, email));
        }

        // Returns an entry for each IPv4 address on adapters that are up, excluding loopback and
        // any adapters excluded in the configuration.
        public static List<TcpTriggerInterface> GetInterfaces()
        {
            List<TcpTriggerInterface> networkInterfaces = new List<TcpTriggerInterface>();

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
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    networkInterfaces.Add(
                        new TcpTriggerInterface(
                            address: address.Address,
                            name: networkInterface.Name,
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