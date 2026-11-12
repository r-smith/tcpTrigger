using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using tcpTrigger.GUI.Models;
using tcpTrigger.GUI.Validation;

namespace tcpTrigger.GUI.Views
{
    public partial class NotificationCommand : UserControl, INotificationHandler
    {
        public NotificationCommand()
        {
            InitializeComponent();
            DataContext = new Models.NotificationCommand { IsEnabled = true };
        }

        public NotificationCommand(Models.NotificationCommand notification)
        {
            InitializeComponent();
            DataContext = notification;
        }

        // Start with the name textbox selected.
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            FriendlyName.Focus();
            FriendlyName.CaretIndex = FriendlyName.Text.Length;
        }

        // Copies the inputs into the new notification and returns it.
        public Notification GetNotificationData()
        {
            UpdateNotificationData();
            return (Notification)DataContext;
        }

        public void UpdateNotificationData()
        {
            // The bindings are explicit, so copy the inputs now.
            EnabledState.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();
            FriendlyName.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Path.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Arguments.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Choose a program",
                Filter = "Programs and scripts (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                Path.Text = dialog.FileName;
            }
        }

        public ValidationOutcome ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(FriendlyName.Text))
            {
                return ValidationOutcome.Invalid("Please enter a notification name.", FriendlyName);
            }

            if (!PathInput.IsValidFilePath(Path.Text))
            {
                return ValidationOutcome.Invalid("Please enter a valid path name.", Path);
            }

            return ValidationOutcome.ValidResult;
        }
    }
}