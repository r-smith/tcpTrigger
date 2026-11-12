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
    public partial class SettingsNotify : UserControl
    {
        private readonly ServiceConfig _config;

        // Raised when a notification is added, edited, or deleted.
        public event EventHandler Changed;

        public SettingsNotify(ServiceConfig config)
        {
            InitializeComponent();

            _config = config;

            ListCollectionView view = new ListCollectionView(config.Notifications);
            view.SortDescriptions.Add(new SortDescription("IsEnabled", ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            view.IsLiveSorting = true;
            view.LiveSortingProperties.Add("IsEnabled");
            view.LiveSortingProperties.Add("Name");

            NotificationsControl.ItemsSource = view;
        }

        private void NewNotification_Click(object sender, RoutedEventArgs e)
        {
            NotifyCreateWindow createWindow = new NotifyCreateWindow { Owner = Window.GetWindow(this) };
            if (createWindow.ShowDialog() == true)
            {
                _config.Notifications.Add(createWindow.CreatedNotification);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ItemBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is Notification notification)
            {
                EditNotification(notification);
            }

            e.Handled = true;
        }

        private void ItemBorder_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                if (sender is Border border && border.DataContext is Notification notification)
                {
                    EditNotification(notification);
                }

                e.Handled = true;
            }
        }

        private void EditNotification(Notification notification)
        {
            NotifyEditWindow editWindow = new NotifyEditWindow(notification) { Owner = Window.GetWindow(this) };
            bool isUpdated = editWindow.ShowDialog() == true;

            if (editWindow.IsDeleteConfirmed)
            {
                _config.Notifications.Remove(notification);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            else if (isUpdated)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}