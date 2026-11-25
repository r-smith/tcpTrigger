using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace tcpTrigger.GUI.Models
{
    // An ignore rule with a checkbox, for choosing which rules to export or import.
    public class SelectableIgnoreRule : INotifyPropertyChanged
    {
        public IgnoreRule Rule { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        // False for a rule that can't be chosen, such as an imported rule that isn't valid.
        public bool CanSelect { get; set; } = true;

        // Badges beside the rule, such as "Already exists" or "Not valid". Null for none.
        public string WarningBadge { get; set; }
        public string ErrorBadge { get; set; }

        public SelectableIgnoreRule(IgnoreRule rule, bool isSelected)
        {
            Rule = rule;
            _isSelected = isSelected;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
