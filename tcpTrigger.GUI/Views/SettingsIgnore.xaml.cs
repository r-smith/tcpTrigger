using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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