using System;
using System.Windows;
using System.Windows.Controls;

namespace tcpTrigger.GUI.Views.Dialogs
{
    // Asked when the Settings window is closed with unsaved changes.
    public partial class DialogUnsavedChanges : UserControl
    {
        public event EventHandler SaveClick;
        public event EventHandler DontSaveClick;
        public event EventHandler CancelClick;

        public DialogUnsavedChanges()
        {
            InitializeComponent();

            // Move keyboard focus into the dialog, away from the inputs behind it.
            Loaded += (sender, e) => SaveButton.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveClick?.Invoke(this, EventArgs.Empty);
        }

        private void DontSave_Click(object sender, RoutedEventArgs e)
        {
            DontSaveClick?.Invoke(this, EventArgs.Empty);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CancelClick?.Invoke(this, EventArgs.Empty);
        }
    }
}