using System;
using System.Windows;
using System.Windows.Controls;

namespace tcpTrigger.GUI.Views.Dialogs
{
    public partial class DialogRuleDelete : UserControl
    {
        public event EventHandler ConfirmClick;
        public event EventHandler CancelClick;

        // `rule` is the Notification or IgnoreRule being deleted. Its Name is displayed.
        public DialogRuleDelete(object rule)
        {
            InitializeComponent();
            DataContext = rule;

            // Move keyboard focus into the dialog. Cancel, so a stray Enter doesn't delete.
            Loaded += (sender, e) => CancelButton.Focus();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CancelClick?.Invoke(this, EventArgs.Empty);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            ConfirmClick?.Invoke(this, EventArgs.Empty);
        }
    }
}