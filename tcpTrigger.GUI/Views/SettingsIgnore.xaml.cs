using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using tcpTrigger.GUI.Configuration;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Views
{
    public partial class SettingsIgnore : UserControl
    {
        private readonly ServiceConfig _config;

        // Raised when an ignore rule is added, edited, or deleted.
        public event EventHandler Changed;

        public SettingsIgnore(ServiceConfig config)
        {
            InitializeComponent();

            _config = config;

            ListCollectionView view = new ListCollectionView(config.IgnoreRules);
            view.SortDescriptions.Add(new SortDescription("IsEnabled", ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            view.IsLiveSorting = true;
            view.LiveSortingProperties.Add("IsEnabled");
            view.LiveSortingProperties.Add("Name");

            RulesControl.ItemsSource = view;
        }

        private void NewRule_Click(object sender, RoutedEventArgs e)
        {
            CreateRule();
        }

        // Opens the new rule editor. `draft` fills it in, such as with a detection's source.
        // The rule is added to the list only if the user adds it.
        public void CreateRule(IgnoreRule draft = null)
        {
            IgnoreCreateWindow createWindow = new IgnoreCreateWindow(draft) { Owner = Window.GetWindow(this) };
            if (createWindow.ShowDialog() == true)
            {
                _config.IgnoreRules.Add(createWindow.CreatedRule);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ItemBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is IgnoreRule rule)
            {
                EditRule(rule);
            }

            e.Handled = true;
        }

        private void ItemBorder_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                if (sender is Border border && border.DataContext is IgnoreRule rule)
                {
                    EditRule(rule);
                }

                e.Handled = true;
            }
        }

        private void ImportExport_Click(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = ImportExportButton.ContextMenu;
            menu.PlacementTarget = ImportExportButton;
            menu.IsOpen = true;
        }

        // Places the menu below the button, lined up with its right edge, so it also slides in from the right.
        // Windows' right-handed menu setting mirrors the placement, so the opposite side is used when it's on.
        private void ImportExportButton_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ContextMenu menu = ImportExportButton.ContextMenu;
            menu.Placement = SystemParameters.MenuDropAlignment ? PlacementMode.Right : PlacementMode.Left;
            menu.PlacementRectangle = new Rect(e.NewSize.Width, e.NewSize.Height, 0, 0);
        }

        // While the menu is open, the import and export button ignores clicks, so clicking it again just closes the menu.
        private void ImportExportMenu_Opened(object sender, RoutedEventArgs e)
        {
            ImportExportButton.IsHitTestVisible = false;
            ExportMenuItem.IsEnabled = _config.IgnoreRules.Count > 0;
            PasteMenuItem.IsEnabled = Clipboard.ContainsText();
        }

        private void ImportExportMenu_Closed(object sender, RoutedEventArgs e)
        {
            ImportExportButton.IsHitTestVisible = true;
        }

        private void ExportRules_Click(object sender, RoutedEventArgs e)
        {
            IgnoreExportWindow exportWindow = new IgnoreExportWindow(_config.IgnoreRules) { Owner = Window.GetWindow(this) };
            exportWindow.ShowDialog();
        }

        private void ImportFromFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Import ignore rules",
                Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            string text;
            try
            {
                text = File.ReadAllText(dialog.FileName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                ShowMessage("Unable to import rules", ex.Message);
                return;
            }
            string fileName = Path.GetFileName(dialog.FileName);
            ImportRules(
                text,
                $"in {fileName}",
                "No rules found",
                $"{fileName} doesn't contain any ignore rules.{Environment.NewLine}{Environment.NewLine}"
                + "Choose a file saved with Export rules, or a tcpTrigger configuration file.");
        }

        private void PasteRules_Click(object sender, RoutedEventArgs e)
        {
            string text;
            try
            {
                text = Clipboard.GetText();
            }
            catch (ExternalException ex)
            {
                // Another program can have the clipboard open.
                ShowMessage("Unable to paste rules", ex.Message);
                return;
            }
            ImportRules(
                text,
                "on the clipboard",
                "No rules to paste",
                $"The clipboard doesn't contain any ignore rules.{Environment.NewLine}{Environment.NewLine}"
                + "Paste rules adds rules shared as text, such as in an email or chat message. Copy the shared text, "
                + "including the <ignoreRules> lines, then choose Paste rules again.");
        }

        // Shows the rules in the text so the user can choose which to add. `source` says where the text came
        // from, such as "on the clipboard". If there are no rules, shows `noRulesTitle` and `noRulesMessage`
        // instead. The chosen rules are added like new rules, so saving settings keeps them.
        private void ImportRules(string text, string source, string noRulesTitle, string noRulesMessage)
        {
            List<IgnoreRule> found;
            try
            {
                found = IgnoreRuleTransfer.Import(text);
            }
            catch (FormatException ex)
            {
                ShowMessage("Unable to import rules", ex.Message);
                return;
            }

            if (found.Count == 0)
            {
                ShowMessage(noRulesTitle, noRulesMessage);
                return;
            }

            IgnoreImportWindow importWindow = new IgnoreImportWindow(found, _config.IgnoreRules, source)
            {
                Owner = Window.GetWindow(this)
            };
            if (importWindow.ShowDialog() == true)
            {
                foreach (IgnoreRule rule in importWindow.ImportedRules)
                {
                    _config.IgnoreRules.Add(rule);
                }
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        // Shown in the Settings window's dialog overlay.
        private void ShowMessage(string title, string message)
        {
            SettingsWindow settingsWindow = (SettingsWindow)Window.GetWindow(this);
            _ = settingsWindow.ShowMessageAsync(title, message);
        }

        private void EditRule(IgnoreRule rule)
        {
            IgnoreEditWindow editWindow = new IgnoreEditWindow(rule) { Owner = Window.GetWindow(this) };
            bool isUpdated = editWindow.ShowDialog() == true;

            if (editWindow.IsDeleteConfirmed)
            {
                _config.IgnoreRules.Remove(rule);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            else if (isUpdated)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
