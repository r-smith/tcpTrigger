using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Views
{
    // A list of ignore rules with checkboxes, plus Select all and a count of the selected rules.
    // Used by the export and import windows.
    public partial class IgnoreRuleChecklist : UserControl
    {
        private List<SelectableIgnoreRule> _rules = new List<SelectableIgnoreRule>();

        // Raised when a rule is checked or unchecked.
        public event EventHandler SelectionChanged;

        public int SelectedCount => _rules.Count(rule => rule.IsSelected);

        public IEnumerable<IgnoreRule> SelectedRules => _rules.Where(rule => rule.IsSelected).Select(rule => rule.Rule);

        public IgnoreRuleChecklist()
        {
            InitializeComponent();
        }

        public void SetRules(List<SelectableIgnoreRule> rules)
        {
            _rules = rules;
            RulesList.ItemsSource = _rules;
            UpdateSelection();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            // A click on a partly checked box checks every rule.
            bool isSelected = SelectAll.IsChecked != false;
            foreach (SelectableIgnoreRule rule in _rules)
            {
                rule.IsSelected = isSelected;
            }
            UpdateSelection();
        }

        private void Rule_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelection();
        }

        // Updates the Select all box and the count to match the selected rules.
        private void UpdateSelection()
        {
            int selected = SelectedCount;
            if (selected == 0)
            {
                SelectAll.IsChecked = false;
            }
            else if (selected == _rules.Count)
            {
                SelectAll.IsChecked = true;
            }
            else
            {
                SelectAll.IsChecked = null;
            }

            SelectedCountText.Text = $"{selected} of {_rules.Count} selected";
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
