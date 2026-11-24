using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using tcpTrigger.GUI.Interop;
using tcpTrigger.GUI.Views.Dialogs;

namespace tcpTrigger.GUI.Views
{
    // Base class for the rule create/edit windows. Show these windows with ShowDialog().
    //
    // Provides the shared title bar behavior (dark title bar, drag to move, no minimize
    // or maximize) and the in-window overlay used for validation errors, messages, and
    // delete confirmation. Derived windows must contain a UserControl named "DialogOverlay".
    public class EditorWindow : Window
    {
        // True when the user confirmed deleting the rule being edited.
        public bool IsDeleteConfirmed { get; private set; }

        private UserControl Overlay => (UserControl)FindName("DialogOverlay");

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            WindowInterop.RemoveMinimizeMaximize(hwnd);
            WindowInterop.UseDarkTitleBar(hwnd,
                ((SolidColorBrush)FindResource("Window.Background")).Color,
                ((SolidColorBrush)FindResource("EditorWindow.Border")).Color);
        }

        protected void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        protected void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void ShowErrorDialog(ValidationOutcome result)
        {
            DialogMessage dialog = new DialogMessage("Error", result.Message);
            dialog.ConfirmClick += (sender, e) =>
            {
                CloseOverlay();
                result.AssociatedControl?.Focus();
            };
            ShowOverlay(dialog);
        }

        // Shows a message. If copyText is given, the dialog has a Copy button that copies it.
        public void ShowMessage(string title, string message, string copyText = null)
        {
            DialogMessage dialog = new DialogMessage(title, message, copyText);
            dialog.ConfirmClick += (sender, e) => CloseOverlay();
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
