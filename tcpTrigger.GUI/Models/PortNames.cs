using System.Collections.Generic;

namespace tcpTrigger.GUI.Models
{
    // Names for common well-known ports.
    internal static class PortNames
    {
        private static readonly Dictionary<int, string> s_tcp = new Dictionary<int, string>
        {
            [7] = "Echo",
            [9] = "Discard",
            [13] = "Daytime",
            [21] = "FTP",
            [22] = "SSH",
            [23] = "Telnet",
            [25] = "SMTP",
            [37] = "Time",
            [53] = "DNS",
            [79] = "Finger",
            [80] = "HTTP",
            [88] = "Kerberos",
            [110] = "POP3",
            [111] = "RPC (Unix)",
            [113] = "Ident",
            [119] = "NNTP",
            [135] = "RPC",
            [139] = "NetBIOS",
            [143] = "IMAP",
            [179] = "BGP",
            [389] = "LDAP",
            [443] = "HTTPS",
            [445] = "SMB",
            [465] = "SMTPS",
            [515] = "LPD",
            [554] = "RTSP",
            [587] = "SMTP",
            [631] = "IPP",
            [636] = "LDAPS",
            [873] = "Rsync",
            [990] = "FTPS",
            [993] = "IMAPS",
            [995] = "POP3S",
            [1433] = "SQL Server",
            [1723] = "PPTP",
            [1755] = "Windows Media",
            [1900] = "UPnP",
            [2049] = "NFS",
            [3306] = "MySQL",
            [3389] = "Remote Desktop",
            [5060] = "SIP",
            [5432] = "PostgreSQL",
            [5800] = "VNC over HTTP",
            [5900] = "VNC",
            [5985] = "WinRM",
            [5986] = "WinRM",
            [8080] = "HTTP",
            [8443] = "HTTPS",
            [9100] = "HP JetDirect",
            [47001] = "WinRM",
        };

        private static readonly Dictionary<int, string> s_udp = new Dictionary<int, string>
        {
            [49] = "TACACS",
            [53] = "DNS",
            [67] = "DHCP",
            [68] = "DHCP",
            [69] = "TFTP",
            [88] = "Kerberos",
            [111] = "RPC (Unix)",
            [123] = "NTP",
            [135] = "RPC",
            [137] = "NetBIOS",
            [138] = "NetBIOS",
            [161] = "SNMP",
            [162] = "SNMP Trap",
            [443] = "QUIC",
            [500] = "IKE",
            [514] = "Syslog",
            [520] = "RIP",
            [631] = "IPP",
            [1434] = "SQL Server",
            [1645] = "RADIUS",
            [1646] = "RADIUS",
            [1701] = "L2TP",
            [1812] = "RADIUS",
            [1813] = "RADIUS",
            [1900] = "SSDP / UPnP",
            [2049] = "NFS",
            [3389] = "Remote Desktop",
            [3702] = "WS-Discovery",
            [4500] = "IPsec",
            [5060] = "SIP",
            [5353] = "mDNS",
            [5355] = "LLMNR",
            [16500] = "Citrix",
        };

        // The port's name, or null if it isn't in the table.
        public static string Get(MatchType type, int port)
        {
            Dictionary<int, string> names;
            switch (type)
            {
                case MatchType.TCP:
                    names = s_tcp;
                    break;
                case MatchType.UDP:
                    names = s_udp;
                    break;
                default:
                    return null;
            }
            return names.TryGetValue(port, out string name) ? name : null;
        }
    }
}