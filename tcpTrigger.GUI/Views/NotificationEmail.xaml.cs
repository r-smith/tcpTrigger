using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Threading.Tasks;
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
            ClientSecret.Password = notification.ClientSecret;

            if (notification.SendMethod == EmailSendMethod.Microsoft365)
            {
                SendWithMicrosoft365.IsSelected = true;
            }

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

        // A password box's built-in scroller takes the mouse wheel even when there's nothing to
        // scroll, which stops the window from scrolling. Pass the wheel on to the parent instead.
        private void PasswordBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            PasswordBox passwordBox = (PasswordBox)sender;
            e.Handled = true;
            MouseWheelEventArgs args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = passwordBox
            };
            ((UIElement)passwordBox.Parent).RaiseEvent(args);
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

        // Returns the selected send method.
        private EmailSendMethod GetSelectedSendMethod()
        {
            return SendWithMicrosoft365.IsSelected ? EmailSendMethod.Microsoft365 : EmailSendMethod.Smtp;
        }

        // Returns the selected security option.
        private EmailSecurity GetSelectedSecurity()
        {
            if (EncryptionTls.IsSelected)
            {
                return EmailSecurity.Tls;
            }
            if (EncryptionStartTls.IsSelected)
            {
                return EmailSecurity.StartTls;
            }
            return EmailSecurity.None;
        }

        // Sends a test email with the connection and address settings on the form, and shows the result.
        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            EditorWindow window = (EditorWindow)Window.GetWindow(this);
            ValidationOutcome validation = ValidateSendInputs();
            if (!validation.IsValid)
            {
                window.ShowErrorDialog(validation);
                return;
            }

            // Read the form now. The send runs on a background thread, which can't read it.
            bool isMicrosoft365 = GetSelectedSendMethod() == EmailSendMethod.Microsoft365;
            string server = Server.Text.Trim();
            EmailSecurity security = GetSelectedSecurity();
            string username = Username.Text;
            string password = Password.Password;
            string tenantId = TenantId.Text.Trim();
            string clientId = ClientId.Text.Trim();
            string clientSecret = ClientSecret.Password;
            string acceptedBy = isMicrosoft365 ? "Microsoft 365" : "The mail server";

            object buttonContent = TestButton.Content;
            TestButton.Content = "Sending...";
            TestButton.IsEnabled = false;
            try
            {
                using (MailMessage message = CreateTestMessage())
                {
                    // Microsoft 365 doesn't report rejected recipients when sending.
                    List<string> rejected;
                    if (isMicrosoft365)
                    {
                        await Task.Run(() => Microsoft365Sender.Send(tenantId, clientId, clientSecret, message));
                        rejected = new List<string>();
                    }
                    else
                    {
                        int port = int.Parse(Port.Text);
                        rejected = await Task.Run(() => SmtpSender.Send(server, port, security, username, password, message));
                    }

                    if (rejected.Count == 0)
                    {
                        window.ShowMessage(
                            "Test email sent",
                            $"{acceptedBy} accepted the test email for {string.Join(", ", message.To)}.{Environment.NewLine}{Environment.NewLine}"
                            + "If it doesn't arrive, check the spam or junk folder.");
                    }
                    else
                    {
                        string text = "The mail server accepted the test email, but rejected these recipients:"
                            + Environment.NewLine + Environment.NewLine
                            + string.Join(Environment.NewLine, rejected);
                        window.ShowMessage("Test email sent", text, text);
                    }
                }
            }
            catch (Exception ex)
            {
                string text = GetErrorText(ex);
                window.ShowMessage("Test email failed", text, text);
            }
            finally
            {
                TestButton.Content = buttonContent;
                TestButton.IsEnabled = true;
            }
        }

        // The test email. Its subject and body are fixed, because the variables need a detection.
        private MailMessage CreateTestMessage()
        {
            string fromAddress = FromAddress.Text.Trim();
            MailMessage message = new MailMessage
            {
                From = string.IsNullOrWhiteSpace(FromDisplayName.Text)
                    ? new MailAddress(fromAddress)
                    : new MailAddress(fromAddress, FromDisplayName.Text.Trim()),
                Subject = "tcpTrigger test email",
                Body = $"This is a test email from tcpTrigger on {Environment.MachineName}."
            };
            foreach (string recipient in EmailInput.ParseRecipients(Recipients.Text))
            {
                message.To.Add(recipient);
            }
            return message;
        }

        // Returns the error's message, followed by the messages of the errors inside it, one per line.
        // SmtpClient's own message is often just "Failure sending mail.", with the cause inside.
        private static string GetErrorText(Exception ex)
        {
            List<string> messages = new List<string>();
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                if (!messages.Contains(current.Message))
                {
                    messages.Add(current.Message);
                }
            }
            return string.Join(Environment.NewLine, messages);
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
            TenantId.Text = TenantId.Text.Trim();
            ClientId.Text = ClientId.Text.Trim();

            // Inputs without a binding.
            if (DataContext is Models.NotificationEmail notification)
            {
                notification.SendMethod = GetSelectedSendMethod();
                notification.Password = Password.Password;
                notification.ClientSecret = ClientSecret.Password;
                notification.Recipients = EmailInput.ParseRecipients(Recipients.Text);
                notification.Security = GetSelectedSecurity();

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
            TenantId.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            ClientId.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
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

            ValidationOutcome sendValidation = ValidateSendInputs();
            if (!sendValidation.IsValid)
            {
                return sendValidation;
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

        // Checks the inputs needed to send an email: the connection settings, recipients, and from address.
        private ValidationOutcome ValidateSendInputs()
        {
            ValidationOutcome connection = GetSelectedSendMethod() == EmailSendMethod.Microsoft365
                ? ValidateMicrosoft365Inputs()
                : ValidateSmtpInputs();
            if (!connection.IsValid)
            {
                return connection;
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

            return ValidationOutcome.ValidResult;
        }

        private ValidationOutcome ValidateSmtpInputs()
        {
            if (string.IsNullOrWhiteSpace(Server.Text))
            {
                return ValidationOutcome.Invalid("Please enter an SMTP server address for sending notifications.", Server);
            }

            if (!PortList.IsValidPort(Port.Text))
            {
                return ValidationOutcome.Invalid(
                    $"Please enter a valid port number.{Environment.NewLine}{Environment.NewLine}"
                    + "Usually 25, 587 (STARTTLS), or 465 (SSL/TLS).",
                    Port);
            }

            return ValidationOutcome.ValidResult;
        }

        // The tenant ID can be a GUID or a domain, such as contoso.onmicrosoft.com, so it's only checked
        // for being filled in. The client ID is always a GUID.
        private ValidationOutcome ValidateMicrosoft365Inputs()
        {
            if (string.IsNullOrWhiteSpace(TenantId.Text))
            {
                return ValidationOutcome.Invalid(
                    $"Please enter the tenant ID of your Microsoft 365 organization.{Environment.NewLine}{Environment.NewLine}"
                    + "It's shown as Directory (tenant) ID on the app registration's overview page in Microsoft Entra.",
                    TenantId);
            }

            if (!Guid.TryParse(ClientId.Text.Trim(), out _))
            {
                return ValidationOutcome.Invalid(
                    $"Please enter a valid client ID.{Environment.NewLine}{Environment.NewLine}"
                    + "It's shown as Application (client) ID on the app registration's overview page in Microsoft Entra.",
                    ClientId);
            }

            if (string.IsNullOrWhiteSpace(ClientSecret.Password))
            {
                return ValidationOutcome.Invalid(
                    $"Please enter the client secret.{Environment.NewLine}{Environment.NewLine}"
                    + "Use the secret's Value from Certificates & secrets in Microsoft Entra, not its Secret ID.",
                    ClientSecret);
            }

            return ValidationOutcome.ValidResult;
        }
    }
}
