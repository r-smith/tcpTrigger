using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace tcpTrigger.GUI.Models
{
    public class IgnoreRule : INotifyPropertyChanged
    {
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
        private string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        // Source IP addresses, such as 192.168.0.1, 192.168.0.1-100, or 192.168.0.0/24. Or just "any".
        private List<string> _sources;
        public List<string> Sources
        {
            get { return _sources; }
            set
            {
                _sources = value;
                OnPropertyChanged();
            }
        }
        // Destination IP addresses (this computer's own), in the same forms as Sources. Or just "any".
        private List<string> _destinations = new List<string> { "any" };
        public List<string> Destinations
        {
            get { return _destinations; }
            set
            {
                _destinations = value;
                OnPropertyChanged();
            }
        }

        // Ports and protocols, such as "443", "tcp:8000-9000", "udp:53", or "icmp". Or just "any".
        private List<string> _services;
        public List<string> Services
        {
            get { return _services; }
            set
            {
                _services = value;
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