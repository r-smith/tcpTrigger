using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using tcpTrigger.GUI.Configuration;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Views
{
    // Lets the user choose ignore rules, then copy them to the clipboard or save them to a file.
    public partial class IgnoreExportWindow : EditorWindow
    {
        private readonly List<SelectableIgnoreRule> _rules;

        // Every rule starts selected. Rules are listed the same way as on the Ignore page: enabled first, then by name.
        public IgnoreExportWindow(IEnumerable<IgnoreRule> rules)
        {
            InitializeComponent();

            _rules = rules
                .OrderByDescending(rule => rule.IsEnabled)
                .ThenBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(rule => new SelectableIgnoreRule(rule, true))
                .ToList();
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

        // Updates the Select all box, the count, and the buttons to match the selected rules.
        private void UpdateSelection()
        {
            int selected = _rules.Count(rule => rule.IsSelected);
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

            SelectedCount.Text = $"{selected} of {_rules.Count} selected";
            CopyButton.IsEnabled = selected > 0;
            SaveButton.IsEnabled = selected > 0;
        }

        private string GetExportText()
        {
            return IgnoreRuleTransfer.Export(_rules.Where(rule => rule.IsSelected).Select(rule => rule.Rule));
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(GetExportText());
                DialogResult = true;
            }
            catch (ExternalException ex)
            {
                // Another program can have the clipboard open.
                ShowMessage("Error", $"The rules couldn't be copied to the clipboard.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Export ignore rules",
                FileName = "tcpTrigger-ignore.xml",
                DefaultExt = ".xml",
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                File.WriteAllText(dialog.FileName, GetExportText(), new UTF8Encoding(false));
                DialogResult = true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowMessage("Error", $"The rules couldn't be saved.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            }
        }
    }
}
