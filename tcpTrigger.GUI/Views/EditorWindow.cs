using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using tcpTrigger.GUI.Interop;
using tcpTrigger.GUI.Views.Dialogs;

namespace tcpTrigger.GUI.Views
{
    // Base class for the rule create/edit windows. Show these windows with ShowDialog().
    //
    // Provides the shared title bar behavior (drag to move, close button, no minimize
    // or maximize) and the in-window overlay used for validation errors and delete
    // confirmation. Derived windows must contain a UserControl named "DialogOverlay".
    public class EditorWindow : Window
    {
        // True when the user confirmed deleting the rule being edited.
        public bool IsDeleteConfirmed { get; private set; }

        private UserControl Overlay => (UserControl)FindName("DialogOverlay");

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowInterop.RemoveMinimizeMaximize(new WindowInteropHelper(this).Handle);
        }

        protected void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        protected void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected void ShowErrorDialog(ValidationOutcome result)
        {
            DialogMessage dialog = new DialogMessage("Error", result.Message);
            dialog.ConfirmClick += (sender, e) =>
            {
                CloseOverlay();
                result.AssociatedControl?.Focus();
            };
            ShowOverlay(dialog);
        }

        // `rule` is the Notification or IgnoreRule being deleted. The dialog displays its Name.
        protected void ShowDeleteDialog(object rule)
        {
            DialogRuleDelete dialog = new DialogRuleDelete(rule);
            dialog.ConfirmClick += (sender, e) =>
            {
                IsDeleteConfirmed = true;
                Close();
            };
            dialog.CancelClick += (sender, e) => CloseOverlay();
            ShowOverlay(dialog);
        }

        private void ShowOverlay(UserControl dialog)
        {
            Overlay.Content = dialog;
            Overlay.Visibility = Visibility.Visible;
        }

        private void CloseOverlay()
        {
            Overlay.Content = null;
            Overlay.Visibility = Visibility.Collapsed;
        }
    }
}