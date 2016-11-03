using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace tcpTrigger
{
    class PacketGenerator
    {
        public static void SendLlmnrQuery(NetInterface netInterface, ushort transactionId, string queryName)
        {
            if (queryName.Length > 255) queryName = queryName.Substring(0, 255);
            queryName = queryName.ToLower();

            var byteList = new List<byte[]>();
            byteList.Add(BitConverter.GetBytes(transactionId));
            if (BitConverter.IsLittleEndian)
                Array.Reverse(byteList[0]);
            byteList.Add(new byte[] { 0x00, 0x00, 0x00, 0x01,
                                      0x00, 0x00, 0x00, 0x00,
                                      0x00, 0x00 });
            var nameLengthByte = new Byte[1];
            nameLengthByte[0] = Convert.ToByte(queryName.Length);
            byteList.Add(nameLengthByte);
            byteList.Add(Encoding.ASCII.GetBytes(queryName));
            byteList.Add(new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01 });

            var sendBytes = byteList.SelectMany(a => a).ToArray();

            try
            {
                var multicastAddress = IPAddress.Parse("224.0.0.252");
                var remoteEndpoint = new IPEndPoint(multicastAddress, 5355);

                // Since this is a multicast packet, we must specify the local endpoint
                // to ensure it is sent out on every interface on a multi-homed system.
                var localEndpoint = new IPEndPoint(netInterface.IP, 0);
                var udpClient = new UdpClient(localEndpoint);
                udpClient.Send(sendBytes, sendBytes.Length, remoteEndpoint);
                udpClient.Close();
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error transmitting LLMNR query: {ex.Message}",
                    EventLogEntryType.Error,
                    405);
            }
        }

        public static void SendNetbiosQuery(NetInterface netInterface, ushort transactionId, string netbiosName)
        {
            var byteList = new List<byte[]>();
            byteList.Add(BitConverter.GetBytes(transactionId));
            if (BitConverter.IsLittleEndian)
                Array.Reverse(byteList[0]);
            byteList.Add(new byte[] { 0x01, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20 });
            byteList.Add(EncodeNetbiosName(netbiosName));
            byteList.Add(new byte[] { 0x00, 0x00, 0x20, 0x00, 0x01 });

            var sendBytes = byteList.SelectMany(a => a).ToArray();

            try
            {
                var endpoint = new IPEndPoint(netInterface.BroadcastAddress, 137);
                var udpClient = new UdpClient(137);
                udpClient.Send(sendBytes, sendBytes.Length, endpoint);
                udpClient.Close();
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "tcpTrigger",
                    $"Error transmitting NetBIOS query: {ex.Message}",
                    EventLogEntryType.Error,
                    405);
            }
        }

        private static byte[] EncodeNetbiosName(string name)
        {
            // Check here: https://support.microsoft.com/en-us/kb/194203
            // Check here: https://tools.ietf.org/html/rfc1002
            // Windows uses 15 characters for the name (padded with spaces).
            // The 16th character is reserved for the service identifier.

            name = name.ToUpper();
            if (name.Length > 15) name = name.Substring(0, 15);
            name = name.PadRight(16);

            var encodedNameBuffer = new byte[32];
            for (int i = 0; i < name.Length; ++i)
            {
                encodedNameBuffer[i * 2] = Convert.ToByte((name[i] >> 4) + 'A');
                encodedNameBuffer[(i * 2) + 1] = Convert.ToByte((name[i] & 0x0F) + 'A');
            }

            // Append NetBIOS suffix
            encodedNameBuffer[30] = 'A' + 0;
            encodedNameBuffer[31] = 'A' + 0;

            return encodedNameBuffer;
        }
    }
}
