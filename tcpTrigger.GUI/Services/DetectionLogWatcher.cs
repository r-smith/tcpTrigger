using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Text.RegularExpressions;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Services
{
    // Reads recent tcpTrigger detection events from the Windows event log, then keeps
    // watching for new ones. DetectionReceived is raised on a background thread.
    internal sealed class DetectionLogWatcher : IDisposable
    {
        private const string LogName = "tcpTrigger";
        private const int DetectIcmpEventId = 200;
        private const int DetectTcpEventId = 201;
        private const int DetectUdpEventId = 202;

        private EventLogWatcher _watcher;

        public event Action<DetectionEvent> DetectionReceived;

        // Returns up to maxExisting of the most recent detection events, newest first.
        // Then starts watching for newer events, which are raised through DetectionReceived.
        // If maxDays > 0, only events from the last maxDays are read.
        // Otherwise, if fromDate (ISO 8601) is set, only events from that date onward are read.
        public List<DetectionEvent> Start(int maxExisting, int maxDays, string fromDate)
        {
            string queryText = BuildQuery(maxDays, fromDate);

            // Read newest first, so reading can stop once maxExisting events are found
            // instead of going through the whole log.
            EventLogQuery recentQuery = new EventLogQuery(LogName, PathType.LogName, queryText)
            {
                ReverseDirection = true
            };
            List<DetectionEvent> existing = ReadRecent(recentQuery, maxExisting, out EventBookmark newestBookmark);

            // Watch for events written after the newest one read above, including any written
            // while reading. If the log had no events, watch for new events only.
            EventLogQuery watchQuery = new EventLogQuery(LogName, PathType.LogName, queryText);
            _watcher = new EventLogWatcher(watchQuery, newestBookmark, readExistingEvents: newestBookmark != null);
            _watcher.EventRecordWritten += Watcher_EventRecordWritten;
            _watcher.Enabled = true;

            return existing;
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.Enabled = false;
                _watcher.Dispose();
                _watcher = null;
            }
        }

        private static string BuildQuery(int maxDays, string fromDate)
        {
            // TimeCreated filter to include in the query, if any.
            string timeSpan = maxDays > 0
                ? $"and TimeCreated[timediff(@SystemTime) &lt;= {TimeSpan.FromDays(maxDays).TotalMilliseconds}]"
                : !string.IsNullOrWhiteSpace(fromDate)
                    ? $"and TimeCreated[@SystemTime&gt;='{fromDate}']"
                    : string.Empty;

            return "<QueryList>"
                + "<Query Id='0'>"
                + $"  <Select Path='{LogName}'>"
                + "    *[System[Provider[@Name='tcpTrigger']"
                + $"      and (EventID &gt;= {DetectIcmpEventId} and EventID &lt;= {DetectUdpEventId}) "
                + timeSpan + "]]"
                + "  </Select>"
                + "</Query>"
                + "</QueryList>";
        }

        // Also returns a bookmark for the newest event read, or null if the log has no events.
        private static List<DetectionEvent> ReadRecent(EventLogQuery query, int maxCount, out EventBookmark newestBookmark)
        {
            List<DetectionEvent> detections = new List<DetectionEvent>();
            newestBookmark = null;

            using (EventLogReader reader = new EventLogReader(query))
            {
                while (detections.Count < maxCount)
                {
                    using (EventRecord record = reader.ReadEvent())
                    {
                        if (record == null)
                        {
                            break; // No more events.
                        }

                        if (newestBookmark == null)
                        {
                            newestBookmark = record.Bookmark;
                        }

                        DetectionEvent detection = Parse(record);
                        if (detection != null)
                        {
                            detections.Add(detection);
                        }
                    }
                }
            }

            return detections;
        }

        private void Watcher_EventRecordWritten(object sender, EventRecordWrittenEventArgs e)
        {
            using (EventRecord record = e.EventRecord)
            {
                if (record == null)
                {
                    return;
                }

                DetectionEvent detection = Parse(record);
                if (detection != null)
                {
                    DetectionReceived?.Invoke(detection);
                }
            }
        }

        // Returns null if the record isn't a detection event or can't be parsed.
        private static DetectionEvent Parse(EventRecord record)
        {
            string description = record.FormatDescription();
            if (string.IsNullOrEmpty(description))
            {
                return null;
            }

            string pattern;
            MatchType matchType;
            switch (record.Id)
            {
                case DetectIcmpEventId:
                    matchType = MatchType.ICMP;
                    pattern = @"^Source IP: (?<s_ip>[^\r\n]*)\r\n"
                            + @"^Destination IP: (?<d_ip>[^\r\n]*)";
                    break;
                case DetectTcpEventId:
                    matchType = MatchType.TCP;
                    pattern = @"^Source IP: (?<s_ip>[^\r\n]*)\r\n"
                            + @"^Source port: (?<s_port>[^\r\n]*)\r\n"
                            + @"^Destination IP: (?<d_ip>[^\r\n]*)\r\n"
                            + @"^Destination port: (?<d_port>[^\r\n]*)";
                    break;
                case DetectUdpEventId:
                    matchType = MatchType.UDP;
                    pattern = @"^Source IP: (?<s_ip>[^\r\n]*)\r\n"
                            + @"^Source port: (?<s_port>[^\r\n]*)\r\n"
                            + @"^Destination IP: (?<d_ip>[^\r\n]*)\r\n"
                            + @"^Destination port: (?<d_port>[^\r\n]*)";
                    break;
                default:
                    return null;
            }

            Match match = Regex.Match(description, pattern, RegexOptions.Multiline);
            if (!match.Success)
            {
                return null;
            }

            IPAddress.TryParse(match.Groups["s_ip"].Value, out IPAddress sourceIP);
            IPAddress.TryParse(match.Groups["d_ip"].Value, out IPAddress destinationIP);
            int.TryParse(match.Groups["s_port"].Value, out int sourcePort);
            int.TryParse(match.Groups["d_port"].Value, out int destinationPort);

            return new DetectionEvent
            {
                Timestamp = record.TimeCreated.Value,
                Type = matchType,
                SourceIP = sourceIP,
                SourcePort = sourcePort,
                DestinationIP = destinationIP,
                DestinationPort = destinationPort
            };
        }
    }
}