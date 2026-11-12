using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace tcpTrigger.GUI.Services
{
    // This computer's own IP addresses, used as a hint next to a destination address box.
    internal static class LocalAddresses
    {
        // The IPv4 addresses of network interfaces that are up, not counting loopback.
        // Returns an empty list if the interfaces can't be read.
        public static List<string> GetIPv4()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                        && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                    .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(unicast => unicast.Address.ToString())
                    .Distinct()
                    .ToList();
            }
            catch (NetworkInformationException)
            {
                return new List<string>();
            }
        }
    }
}
