using System;
using System.Windows;
using System.Windows.Controls;

namespace tcpTrigger.GUI.Views.Dialogs
{
    // Asked before stopping the service from the main window's status line.
    public partial class DialogStopService : UserControl
    {
        public event EventHandler StopClick;
        public event EventHandler CancelClick;

        public DialogStopService()
        {
            InitializeComponent();

            // Start on Cancel, so pressing Enter doesn't stop monitoring.
            Loaded += (sender, e) => CancelButton.Focus();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            StopClick?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CancelClick?.Invoke(this, EventArgs.Empty);
        }
    }
}