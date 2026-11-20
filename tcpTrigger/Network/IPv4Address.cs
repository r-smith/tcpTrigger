using System.Net;

namespace tcpTrigger
{
    // Converts IPv4 addresses to and from numbers, with the first octet in the highest byte.
    // PacketHeader's SourceAddress and DestinationAddress and the ignore rule ranges use this order,
    // so they can be compared directly.
    internal static class IPv4Address
    {
        public static uint ToNumber(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }

        public static IPAddress ToIPAddress(uint number)
        {
            return new IPAddress((uint)IPAddress.HostToNetworkOrder((int)number));
        }
    }
}