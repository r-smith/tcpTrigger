using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace tcpTrigger.GUI.Models
{
    // An ignore rule with a checkbox, for choosing which rules to export or import.
    public class SelectableIgnoreRule : INotifyPropertyChanged
    {
        // How many entries of a list to show before "+N more".
        private const int ShownEntries = 2;

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

        // One-line previews of the rule's sources and ports, such as "10.0.0.5, 10.0.0.6 +2 more".
        public string SourcesPreview => Preview(Rule.Sources, "Any IP address");
        public string ServicesPreview => Preview(Rule.Services, "Any port");

        public SelectableIgnoreRule(IgnoreRule rule, bool isSelected)
        {
            Rule = rule;
            _isSelected = isSelected;
        }

        private static string Preview(List<string> entries, string anyText)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }
            if (entries.Count == 1 && entries[0] == "any")
            {
                return anyText;
            }

            string preview = string.Join(", ", entries.Take(ShownEntries));
            return entries.Count > ShownEntries
                ? $"{preview} +{entries.Count - ShownEntries} more"
                : preview;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
