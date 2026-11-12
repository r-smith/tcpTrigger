using System;
using System.Globalization;
using System.Text;

namespace tcpTrigger
{
    // Formats a matched packet as a single-line JSON object.
    internal static class JsonLogLine
    {
        // ISO 8601 with UTC offset, such as 2026-11-05T09:54:42.516-08:00.
        private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";

        // Returns the packet's details as a single-line JSON object, with no trailing line ending.
        public static string Build(PacketHeader packetHeader)
        {
            bool hasPorts = packetHeader.MatchType == PacketMatch.TcpConnect
                || packetHeader.MatchType == PacketMatch.UdpCommunication;

            StringBuilder json = new StringBuilder("{");
            AppendField(json, "time", packetHeader.Timestamp.ToString(TimeFormat, CultureInfo.InvariantCulture));
            AppendField(json, "sensor_name", Environment.MachineName);
            AppendField(json, "event_type", "connection");
            AppendField(json, "protocol", packetHeader.MatchTypeName.ToLowerInvariant());

            AppendField(json, "source_ip", packetHeader.SourceIP.ToString());
            if (hasPorts)
            {
                AppendField(json, "source_port", packetHeader.SourcePort);
            }

            AppendField(json, "destination_ip", packetHeader.DestinationIP.ToString());
            if (hasPorts)
            {
                AppendField(json, "destination_port", packetHeader.DestinationPort);
            }

            AppendField(json, "ttl", packetHeader.Ttl);

            if (packetHeader.MatchType == PacketMatch.TcpConnect)
            {
                AppendField(json, "tcp_flags", packetHeader.TcpFlagNames);
            }

            if (packetHeader.MatchType == PacketMatch.IcmpRequest)
            {
                AppendField(json, "icmp_type", packetHeader.IcmpTypeName);
            }
            json.Append('}');

            return json.ToString();
        }

        // Appends a "name":"value" field, escaping the value as JSON requires.
        private static void AppendField(StringBuilder json, string name, string value)
        {
            AppendName(json, name);
            json.Append('"');
            foreach (char c in value ?? string.Empty)
            {
                // JSON requires escaping quotation marks, backslashes, and the control characters (U+0000 to U+001F).
                switch (c)
                {
                    // These characters get a two-character escape sequence, such as "\n".
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        // Other control characters are escaped as \u and four hex digits.
                        // All other characters are written as-is.
                        if (c < ' ')
                        {
                            json.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            json.Append(c);
                        }
                        break;
                }
            }
            json.Append('"');
        }

        // Appends a "name":number field.
        private static void AppendField(StringBuilder json, string name, int value)
        {
            AppendName(json, name);
            json.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        // Appends the field name, with a comma before every field except the first.
        private static void AppendName(StringBuilder json, string name)
        {
            if (json.Length > 1)
            {
                json.Append(',');
            }
            json.Append('"').Append(name).Append("\":");
        }
    }
}