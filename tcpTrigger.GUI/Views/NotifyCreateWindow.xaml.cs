using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Views
{
    public partial class NotifyCreateWindow : EditorWindow
    {
        private INotificationHandler _currentControl;

        // One editor per type, kept so that switching to another type and back
        // doesn't lose what was typed.
        private readonly Dictionary<string, INotificationHandler> _editors = new Dictionary<string, INotificationHandler>();

        // The new notification. Set when ShowDialog() returns true.
        public Notification CreatedNotification { get; private set; }

        public NotifyCreateWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            NotificationTypeSelector.SelectedIndex = 0;
        }

        private void NotificationTypeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NotificationEntryControl == null)
            {
                return;
            }

            ComboBoxItem selectedItem = (ComboBoxItem)NotificationTypeSelector.SelectedItem;
            string type = selectedItem.Content.ToString();

            if (!_editors.TryGetValue(type, out _currentControl))
            {
                _currentControl = CreateEditor(type);
                _editors[type] = _currentControl;
            }
            NotificationEntryControl.Content = _currentControl;
        }

        private static INotificationHandler CreateEditor(string type)
        {
            switch (type)
            {
                case "Send email":
                    return new NotificationEmail();
                case "Run command":
                    return new NotificationCommand();
                case "Write to log file":
                    return new NotificationLogFile();
                default:
                    return null;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (_currentControl == null)
            {
                Close();
                return;
            }

            ValidationOutcome validation = _currentControl.ValidateInputs();

            if (validation.IsValid)
            {
                CreatedNotification = _currentControl.GetNotificationData();
                DialogResult = true;
            }
            else
            {
                ShowErrorDialog(validation);
            }
        }
    }
}