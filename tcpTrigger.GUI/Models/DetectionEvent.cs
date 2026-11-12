using System;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;

namespace tcpTrigger.GUI.Models
{
    public class DetectionEvent : INotifyPropertyChanged
    {
        public DateTime Timestamp { get; set; }
        public MatchType Type { get; set; }
        public string Action
        {
            get
            {
                switch (Type)
                {
                    case MatchType.ICMP:
                        return "ICMP request";
                    case MatchType.TCP:
                        return "TCP connection";
                    case MatchType.UDP:
                        return "UDP connection";
                    default:
                        return string.Empty;
                }
            }
        }
        public IPAddress SourceIP { get; set; }
        public IPAddress DestinationIP { get; set; }
        public int SourcePort { get; set; }
        public int DestinationPort { get; set; }

        // The name of a well-known destination port, such as "SMB", or null.
        public string PortName
        {
            get { return PortNames.Get(Type, DestinationPort); }
        }

        // String showing the full source and destination endpoints ("10.0.0.8:51234 ➝ 10.0.0.3:445")
        public string Endpoints
        {
            get
            {
                if (SourceIP == null || DestinationIP == null)
                {
                    return string.Empty;
                }
                if (Type == MatchType.ICMP)
                {
                    return $"{SourceIP} ➝ {DestinationIP}";
                }
                return $"{new IPEndPoint(SourceIP, SourcePort)} ➝ {new IPEndPoint(DestinationIP, DestinationPort)}";
            }
        }

        // UI state for MonitorView: whether this detection's details are open.
        private bool _showDetails;
        public bool ShowDetails
        {
            get { return _showDetails; }
            set
            {
                _showDetails = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}