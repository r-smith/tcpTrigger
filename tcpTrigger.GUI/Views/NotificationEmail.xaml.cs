using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using tcpTrigger.GUI.Models;
using tcpTrigger.GUI.Validation;
using tcpTrigger.GUI.Views.Controls;

namespace tcpTrigger.GUI.Views
{
    public partial class NotificationEmail : UserControl, INotificationHandler
    {
        // Variables that can go in the subject and body.
        private static readonly string[] s_variables =
        {
            "{SENSOR_NAME}", "{SOURCE_IP}", "{SOURCE_HOSTNAME}", "{SOURCE_OS_GUESS}", "{DESTINATION_IP}",
            "{DESTINATION_MAC}", "{DESTINATION_SERVICES}", "{DETECTION_TYPE}", "{DETECTION_COUNT}", "{DETECTION_LOG}"
        };

        // The text box a clicked variable goes into: the subject or the body, whichever had the
        // focus last.
        private TextBox _variableTarget;

        // Placeholder text for the password box, from its Tag in XAML.
        private readonly string _passwordPlaceholder;

        public NotificationEmail()
        {
            InitializeComponent();
            _passwordPlaceholder = (string)Password.Tag;
            SetUpVariables();
            DataContext = new Models.NotificationEmail
            {
                IsEnabled = true,
                Port = Models.NotificationEmail.DefaultPort,
                Security = EmailSecurity.StartTls,
                Subject = Models.NotificationEmail.DefaultSubject,
                Body = Models.NotificationEmail.DefaultBody
            };
        }

        public NotificationEmail(Models.NotificationEmail notification)
        {
            InitializeComponent();
            _passwordPlaceholder = (string)Password.Tag;
            SetUpVariables();
            DataContext = notification;

            Recipients.Text = string.Join(",", notification.Recipients);
            Password.Password = notification.Password;

            switch (notification.Security)
            {
                case EmailSecurity.StartTls:
                    EncryptionStartTls.IsSelected = true;
                    break;
                case EmailSecurity.Tls:
                    EncryptionTls.IsSelected = true;
                    break;
                default:
                    EncryptionNone.IsSelected = true;
                    break;
            }
        }

        private void SetUpVariables()
        {
            _variableTarget = Body;
            VariableList.Variables = s_variables;
            VariableList.VariableClicked += (sender, variable) => VariablePicker.InsertInto(_variableTarget, variable);
        }

        private void VariableTarget_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _variableTarget = (TextBox)sender;
        }

        // Shows the password placeholder only while the password box is empty.
        private void Password_PasswordChanged(object sender, RoutedEventArgs e)
        {
            Password.Tag = Password.Password.Length == 0 ? _passwordPlaceholder : null;
        }

        // The password box's built-in scroller takes the mouse wheel even when there's nothing to
        // scroll, which stops the window from scrolling. Pass the wheel on to the parent instead.
        private void Password_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            MouseWheelEventArgs args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = Password
            };
            ((UIElement)Password.Parent).RaiseEvent(args);
        }

        // When the security setting changes, fills in its usual port. A port the user typed is kept.
        private void Security_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip the selection made while the form is set up.
            if (!IsLoaded || e.RemovedItems.Count == 0 || e.AddedItems.Count == 0)
            {
                return;
            }

            string port = Port.Text.Trim();
            if (port.Length == 0 || port == GetUsualPort(e.RemovedItems[0]))
            {
                Port.Text = GetUsualPort(e.AddedItems[0]);
            }
        }

        // Returns the port usually used with a security option.
        private string GetUsualPort(object securityItem)
        {
            if (securityItem == EncryptionTls)
            {
                return "465";
            }
            if (securityItem == EncryptionStartTls)
            {
                return "587";
            }
            return "25";
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
            Port.Text = Port.Text.Trim();

            // Inputs without a binding.
            if (DataContext is Models.NotificationEmail notification)
            {
                notification.Password = Password.Password;
                notification.Recipients = EmailInput.ParseRecipients(Recipients.Text);
                if (EncryptionTls.IsSelected)
                {
                    notification.Security = EmailSecurity.Tls;
                }
                else if (EncryptionStartTls.IsSelected)
                {
                    notification.Security = EmailSecurity.StartTls;
                }
                else
                {
                    notification.Security = EmailSecurity.None;
                }

                // ValidateInputs has already checked that these are valid numbers.
                if (int.TryParse(RateLimitSeconds.Text, out int rateLimit))
                {
                    notification.RateLimitSeconds = rateLimit;
                }
                if (int.TryParse(WaitSeconds.Text, out int seconds))
                {
                    notification.WaitSeconds = seconds;
                }
            }

            // The bindings are explicit, so copy the inputs now.
            EnabledState.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();
            FriendlyName.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Server.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Port.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Username.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            FromAddress.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            FromDisplayName.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Subject.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Body.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        public ValidationOutcome ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(FriendlyName.Text))
            {
                return ValidationOutcome.Invalid("Please enter a notification name.", FriendlyName);
            }

            if (string.IsNullOrWhiteSpace(Server.Text))
            {
                return ValidationOutcome.Invalid("Please enter an SMTP server address for sending notifications.", Server);
            }

            if (!PortList.IsValidPort(Port.Text))
            {
                return ValidationOutcome.Invalid(
                    $"Please enter a valid port number.{Environment.NewLine}{Environment.NewLine}"
                    + "Typically, this will be port 25 or 587. Implicit TLS connections, typically port 465, are not supported.",
                    Port);
            }

            if (string.IsNullOrWhiteSpace(Recipients.Text) || !EmailInput.IsValidRecipients(Recipients.Text))
            {
                return ValidationOutcome.Invalid(
                    $"Please enter at least one valid recipient email address for receiving notifications, such as alerts@example.com.{Environment.NewLine}{Environment.NewLine}"
                    + "Multiple addresses should be separated by commas (,) or semicolons (;).",
                    Recipients);
            }

            if (!EmailInput.IsValidAddress(FromAddress.Text))
            {
                return ValidationOutcome.Invalid("Please enter a valid 'from' email address to use when sending notifications, such as tcptrigger@example.com.", FromAddress);
            }

            if (string.IsNullOrWhiteSpace(Subject.Text))
            {
                return ValidationOutcome.Invalid("Please enter an email subject to use for sent notifications.", Subject);
            }

            if (string.IsNullOrWhiteSpace(Body.Text))
            {
                return ValidationOutcome.Invalid("Please enter an email body to use for sent notifications.", Body);
            }

            if (!EmailInput.IsValidRateLimit(RateLimitSeconds.Text))
            {
                return ValidationOutcome.Invalid(
                    $"Please enter how often an email can be sent, between {EmailInput.MinRateLimitSeconds} and {EmailInput.MaxRateLimitSeconds} seconds.{Environment.NewLine}{Environment.NewLine}"
                    + $"The default is one email every {Models.NotificationEmail.DefaultRateLimitSeconds} seconds.",
                    RateLimitSeconds);
            }

            if (!EmailInput.IsValidWaitSeconds(WaitSeconds.Text))
            {
                return ValidationOutcome.Invalid(
                    $"Please enter how long to collect detections before sending, between {EmailInput.MinWaitSeconds} and {EmailInput.MaxWaitSeconds} seconds.{Environment.NewLine}{Environment.NewLine}"
                    + $"The default is {Models.NotificationEmail.DefaultWaitSeconds} seconds.",
                    WaitSeconds);
            }

            return ValidationOutcome.ValidResult;
        }
    }
}
