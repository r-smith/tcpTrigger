using System.Windows;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Views
{
    public partial class NotifyEditWindow : EditorWindow
    {
        private readonly Notification _selectedNotification;
        private INotificationHandler _currentControl;

        public NotifyEditWindow(Notification notification)
        {
            InitializeComponent();
            _selectedNotification = notification;
            ApplyNotificationToGUI();
        }

        private void ApplyNotificationToGUI()
        {
            switch (_selectedNotification)
            {
                case Models.NotificationEmail n:
                    NotificationTypeText.Text = "Email";
                    _currentControl = new NotificationEmail(n);
                    NotificationEntryControl.Content = _currentControl;
                    break;
                case Models.NotificationCommand n:
                    NotificationTypeText.Text = "Command";
                    _currentControl = new NotificationCommand(n);
                    NotificationEntryControl.Content = _currentControl;
                    break;
                case Models.NotificationLogFile n:
                    NotificationTypeText.Text = "Log file";
                    _currentControl = new NotificationLogFile(n);
                    NotificationEntryControl.Content = _currentControl;
                    break;
                default:
                    break;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ValidationOutcome validation = _currentControl.ValidateInputs();

            if (validation.IsValid)
            {
                _currentControl.UpdateNotificationData();
                DialogResult = true;
            }
            else
            {
                ShowErrorDialog(validation);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            ShowDeleteDialog(_selectedNotification);
        }
    }
}