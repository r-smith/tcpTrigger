using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using tcpTrigger.GUI.Configuration;
using tcpTrigger.GUI.Models;
using tcpTrigger.GUI.Validation;

namespace tcpTrigger.GUI.Views
{
    // Shows the ignore rules found in a file or on the clipboard, so the user can choose which to add.
    public partial class IgnoreImportWindow : EditorWindow
    {
        // The rules the user chose. Set when ShowDialog() returns true.
        public List<IgnoreRule> ImportedRules { get; private set; }

        // `found` is the rules read from the file or clipboard, and `existing` is the rules already on the
        // Ignore page. `source` says where the rules came from, such as "in tcpTrigger-ignore.xml".
        // Valid rules start selected, unless a rule that matches the same traffic already exists, or comes earlier
        // in what was found. Rules that aren't valid can't be selected. Rules with destination addresses get a
        // badge and a note, because those addresses may not match this computer.
        public IgnoreImportWindow(List<IgnoreRule> found, IEnumerable<IgnoreRule> existing, string source)
        {
            InitializeComponent();

            List<IgnoreRule> existingRules = existing.ToList();
            List<SelectableIgnoreRule> rules = new List<SelectableIgnoreRule>();
            foreach (IgnoreRule rule in found)
            {
                bool isValid = IgnoreRuleInput.IsValidRule(rule);
                bool exists = existingRules.Any(existingRule => IgnoreRuleTransfer.HasSameMatch(rule, existingRule));
                string warningBadge = null;
                if (isValid && exists)
                {
                    warningBadge = "Already exists";
                }
                else if (isValid && HasDestinations(rule))
                {
                    warningBadge = "Check destinations";
                    DestinationsNote.Visibility = Visibility.Visible;
                }

                rules.Add(new SelectableIgnoreRule(rule, isValid && !exists)
                {
                    CanSelect = isValid,
                    WarningBadge = warningBadge,
                    ErrorBadge = isValid ? null : "Not valid"
                });

                if (isValid)
                {
                    existingRules.Add(rule);
                }
            }

            string count = found.Count == 1 ? "1 rule" : $"{found.Count} rules";
            Summary.Text = $"Found {count} {source}.{Environment.NewLine}Checked rules are added when you save settings.";
            Checklist.SetRules(rules);
        }

        // True if the rule applies to specific destination addresses, not "any".
        private static bool HasDestinations(IgnoreRule rule)
        {
            return rule.Destinations != null && rule.Destinations.Count > 0 && rule.Destinations[0] != "any";
        }

        private void Checklist_SelectionChanged(object sender, EventArgs e)
        {
            int selected = Checklist.SelectedCount;
            AddButton.Content = selected == 1 ? "_Add 1 rule" : $"_Add {selected} rules";
            AddButton.IsEnabled = selected > 0;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            ImportedRules = Checklist.SelectedRules.ToList();
            DialogResult = true;
        }
    }
}
