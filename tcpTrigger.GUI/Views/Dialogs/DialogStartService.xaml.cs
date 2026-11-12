using System;
using System.Windows;
using System.Windows.Controls;

namespace tcpTrigger.GUI.Views.Dialogs
{
    // Asked after the first save on a new installation, when the service is stopped.
    public partial class DialogStartService : UserControl
    {
        public event EventHandler StartClick;
        public event EventHandler NotNowClick;

        public DialogStartService()
        {
            InitializeComponent();

            // Move keyboard focus into the dialog, away from the inputs behind it.
            Loaded += (sender, e) => StartButton.Focus();
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            StartClick?.Invoke(this, EventArgs.Empty);
        }

        private void NotNow_Click(object sender, RoutedEventArgs e)
        {
            NotNowClick?.Invoke(this, EventArgs.Empty);
        }
    }
}