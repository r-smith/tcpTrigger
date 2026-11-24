using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace tcpTrigger.GUI.Models
{
    public abstract class Notification : INotifyPropertyChanged
    {
        public NotificationType Type { get; protected set; }

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

        // Warning and error badges for the settings list. Null when there's none.
        public virtual string WarningBadge => null;
        public virtual string ErrorBadge => null;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
