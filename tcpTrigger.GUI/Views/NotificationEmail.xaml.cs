using System;
using System.Windows;
using System.Windows.Controls;
using tcpTrigger.GUI.Models;
using tcpTrigger.GUI.Validation;

namespace tcpTrigger.GUI.Views
{
    public partial class NotificationEmail : UserControl, INotificationHandler
    {
        public NotificationEmail()
        {
            InitializeComponent();
            DataContext = new Models.NotificationEmail
            {
                IsEnabled = true,
                Subject = Models.NotificationEmail.DefaultSubject,
                Body = Models.NotificationEmail.DefaultBody
            };
        }

        public NotificationEmail(Models.NotificationEmail notification)
        {
            InitializeComponent();
            DataContext = notification;

            Recipients.Text = string.Join(",", notification.Recipients);
            Password.Password = notification.Password;

            if (notification.IsStartTlsEnabled)
            {
                EncryptionStartTls.IsSelected = true;
            }
            else
            {
                EncryptionNone.IsSelected = true;
            }
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
                notification.IsStartTlsEnabled = EncryptionStartTls.IsSelected;

                // ValidateInputs has already checked that these are valid numbers.
                if (int.TryParse(RateLimitSeconds.Text, out int rateLimit))
                {
                    notification.RateLimitSeconds = rateLimit;
                }
                if (int.TryParse(BufferSeconds.Text, out int buffer))
                {
                    notification.BufferSeconds = buffer;
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

            if (!EmailInput.IsValidBufferSeconds(BufferSeconds.Text))
            {
                return ValidationOutcome.Invalid(
                    $"Please enter how long to collect detections before sending, between {EmailInput.MinBufferSeconds} and {EmailInput.MaxBufferSeconds} seconds.{Environment.NewLine}{Environment.NewLine}"
                    + $"The default is {Models.NotificationEmail.DefaultBufferSeconds} seconds.",
                    BufferSeconds);
            }

            return ValidationOutcome.ValidResult;
        }
    }
}