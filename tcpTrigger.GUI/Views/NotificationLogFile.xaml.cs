using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using tcpTrigger.GUI.Configuration;
using tcpTrigger.GUI.Models;
using tcpTrigger.GUI.Validation;

namespace tcpTrigger.GUI.Views
{
    public partial class NotificationLogFile : UserControl, INotificationHandler
    {
        public NotificationLogFile()
        {
            InitializeComponent();
            DataContext = new Models.NotificationLogFile { IsEnabled = true };
        }

        public NotificationLogFile(Models.NotificationLogFile notification)
        {
            InitializeComponent();
            DataContext = notification;
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
            // The bindings are explicit, so copy the inputs now.
            EnabledState.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();
            FriendlyName.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Path.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            // The service appends to an existing log file, so choosing one doesn't replace it.
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Choose a log file",
                FileName = "tcpTrigger.log",
                Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                OverwritePrompt = false,
                InitialDirectory = System.IO.Path.GetDirectoryName(ConfigPaths.ProgramDataPath)
            };

            // A path already entered takes priority.
            try
            {
                string currentPath = Path.Text.Trim();
                if (currentPath.Length > 0)
                {
                    string currentFolder = System.IO.Path.GetDirectoryName(currentPath);
                    if (!string.IsNullOrEmpty(currentFolder))
                    {
                        dialog.InitialDirectory = currentFolder;
                    }
                    dialog.FileName = System.IO.Path.GetFileName(currentPath);
                }
            }
            catch (ArgumentException)
            {
                // The current path isn't valid. Start from the tcpTrigger folder instead.
            }

            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                Path.Text = dialog.FileName;
            }
        }

        public ValidationOutcome ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(FriendlyName.Text))
            {
                return ValidationOutcome.Invalid("Please enter a notification name.", FriendlyName);
            }

            if (!PathInput.IsValidFilePath(Path.Text))
            {
                return ValidationOutcome.Invalid("Please enter a valid path name.", Path);
            }

            return ValidationOutcome.ValidResult;
        }
    }
}