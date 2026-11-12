using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace tcpTrigger.GUI.Views.Dialogs
{
    // A message with a title and an OK button, shown in a window's dialog overlay.
    public partial class DialogMessage : UserControl
    {
        public event EventHandler ConfirmClick;

        private readonly string _copyText;

        // If copyText is given, add a Copy button that puts it on the clipboard.
        public DialogMessage(string title, string message, string copyText = null)
        {
            InitializeComponent();
            Heading.Text = title;
            Message.Text = message;

            _copyText = copyText;
            if (copyText != null)
            {
                CopyButton.Visibility = Visibility.Visible;
            }

            // Move keyboard focus into the dialog, away from the inputs behind it.
            Loaded += (sender, e) => OkButton.Focus();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ConfirmClick?.Invoke(this, EventArgs.Empty);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_copyText);
                CopyButton.Content = "Copied";
            }
            catch (ExternalException)
            {
                CopyButton.Content = "Try again";
            }
        }
    }
}