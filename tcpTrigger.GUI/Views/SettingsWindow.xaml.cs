using System;
using System.ComponentModel;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using tcpTrigger.GUI.Configuration;
using tcpTrigger.GUI.Models;
using tcpTrigger.GUI.Services;
using tcpTrigger.GUI.Views.Dialogs;

namespace tcpTrigger.GUI.Views
{
    public partial class SettingsWindow : Window
    {
        private enum SaveOutcome
        {
            Saved,          // Saved. A running service reloaded it.
            ReloadFailed,   // Saved, but the service could not apply it.
            Cancelled,      // Not saved. The user declined the UAC prompt.
            Failed          // Not saved. The user has already been shown why.
        }

        public static SettingsWindow Instance { get; private set; }

        // True from the first change until the changes are saved.
        public bool HasUnsavedChanges { get; private set; }

        private bool _isSaving;

        // True until the first save when there was no configuration file. That save
        // offers to start the service.
        private bool _isFirstSave;

        // Set by Failed() during a save. Shown to the user once the save is done.
        private string _saveFailureReason;

        // Set by CloseForAppExit. The app exits when this window closes.
        private bool _exitAppWhenClosed;

        private readonly ServiceConfig _config;
        private readonly string _configPath;
        private readonly SettingsDetect _settingsDetect;
        private readonly SettingsNotify _settingsNotify;
        private readonly SettingsIgnore _settingsIgnore;
        private readonly SettingsStatus _settingsStatus;

        // `config` is edited in place. SaveAsync writes it to `configPath`.
        public SettingsWindow(ServiceConfig config, string configPath)
        {
            InitializeComponent();

            _config = config;
            _configPath = configPath;

            _settingsDetect = new SettingsDetect(config);
            _settingsNotify = new SettingsNotify(config);
            _settingsIgnore = new SettingsIgnore(config);
            _settingsStatus = new SettingsStatus();

            DetectContent.Content = _settingsDetect;
            NotifyContent.Content = _settingsNotify;
            IgnoreContent.Content = _settingsIgnore;
            StatusContent.Content = _settingsStatus;

            _settingsDetect.Changed += Settings_Changed;
            _settingsNotify.Changed += Settings_Changed;
            _settingsIgnore.Changed += Settings_Changed;

            // With no configuration file, the settings shown haven't been saved anywhere yet.
            if (!File.Exists(configPath))
            {
                _isFirstSave = true;
                MarkUnsavedChanges();
            }

            // Legacy port lists became ignore rules when the file was read. Saving writes them.
            if (config.HasConvertedPortLists)
            {
                MarkUnsavedChanges();
            }

            Instance = this;
        }

        private void Settings_Changed(object sender, EventArgs e)
        {
            MarkUnsavedChanges();
        }

        private void MarkUnsavedChanges()
        {
            HasUnsavedChanges = true;
            SaveButton.IsEnabled = true;
            SetSaveStatus("Unsaved changes", isWarning: false);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // DragMove throws if the button has already been released, which can happen
            // when the window was busy. Checking the button first avoids that crash.
            if (e.ChangedButton == MouseButton.Left && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Normal) ? WindowState.Maximized : WindowState.Normal;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Switches to the Status tab. Used by the main window's status bar.
        internal void ShowStatusTab()
        {
            Activate();
            TabStatus.IsSelected = true;
        }

        // Switches to the Ignore tab and opens the new rule editor, filled in from `draft`.
        // Used by the monitor's right-click menu.
        internal void CreateIgnoreRule(IgnoreRule draft)
        {
            Activate();
            TabIgnore.IsSelected = true;
            _settingsIgnore.CreateRule(draft);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Instance = null;

            if (_exitAppWhenClosed)
            {
                Application.Current.Shutdown();
            }
        }

        // Called when the user exits the app. With unsaved changes, the "Save changes?" prompt
        // is shown, and the app finishes exiting once the user saves or chooses not to.
        // Cancelling, or a save that doesn't go through, keeps the app open.
        public void CloseForAppExit()
        {
            _exitAppWhenClosed = true;
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_isSaving)
            {
                // Let the save finish first. Closing now could hide its result.
                e.Cancel = true;
                _exitAppWhenClosed = false;
            }
            else if (HasUnsavedChanges)
            {
                e.Cancel = true;
                ShowUnsavedChangesDialog();
            }
        }

        private void ShowUnsavedChangesDialog()
        {
            DialogUnsavedChanges dialog = new DialogUnsavedChanges();
            dialog.SaveClick += async (sender, e) =>
            {
                CloseOverlay();
                await SaveAsync();

                // Stay open if the save didn't happen, so the user can see why.
                if (HasUnsavedChanges)
                {
                    _exitAppWhenClosed = false;
                }
                else
                {
                    Close();
                }
            };
            dialog.DontSaveClick += (sender, e) =>
            {
                HasUnsavedChanges = false;
                Close();
            };
            dialog.CancelClick += (sender, e) =>
            {
                _exitAppWhenClosed = false;
                CloseOverlay();
            };
            ShowOverlay(dialog);
        }

        // Shows a message in the dialog overlay. The task completes when the user closes it,
        // so callers can wait before doing anything else, such as closing the window.
        // `focusAfterClose` is the input to focus afterward, such as the one an error is about.
        // Also used by the Settings pages, such as the Status tab.
        internal Task ShowMessageAsync(string title, string message, Control focusAfterClose = null)
        {
            TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>();
            DialogMessage dialog = new DialogMessage(title, message);
            dialog.ConfirmClick += (sender, e) =>
            {
                CloseOverlay();
                focusAfterClose?.Focus();
                closed.SetResult(true);
            };
            ShowOverlay(dialog);
            return closed.Task;
        }

        private void ShowOverlay(UserControl dialog)
        {
            DialogOverlay.Content = dialog;
            DialogOverlay.Visibility = Visibility.Visible;
        }

        private void CloseOverlay()
        {
            DialogOverlay.Content = null;
            DialogOverlay.Visibility = Visibility.Collapsed;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            await SaveAsync();
        }

        // Validates, saves, and reloads, then shows the result. The task completes once the
        // user closes any error message. HasUnsavedChanges stays true if nothing was saved.
        private async Task SaveAsync()
        {
            // Apply the Detect settings. Notifications and ignore rules are validated in their
            // editors, before they're added to the configuration.
            _settingsDetect.UpdateSettings();

            // Lock the button and the pages while saving, so a second click can't start another
            // save and an edit made during the save isn't marked as saved along with it.
            _isSaving = true;
            SaveButton.IsEnabled = false;
            MyTabControl.IsEnabled = false;
            SetSaveStatus("Saving...", isWarning: false);
            SaveOutcome outcome;
            try
            {
                outcome = await SaveAndReloadAsync();
            }
            finally
            {
                _isSaving = false;
                SaveButton.IsEnabled = true;
                MyTabControl.IsEnabled = true;
            }

            // Once the file is written, there's nothing left to save, even if the reload failed.
            if (outcome == SaveOutcome.Saved || outcome == SaveOutcome.ReloadFailed)
            {
                HasUnsavedChanges = false;
                SaveButton.IsEnabled = false;
            }

            await ShowSaveOutcomeAsync(outcome);

            // The first save on a new installation creates the settings file, which the
            // service needs before it can start. Offer to start it now.
            if (_isFirstSave && outcome == SaveOutcome.Saved)
            {
                _isFirstSave = false;
                if (IsServiceStopped())
                {
                    await OfferToStartServiceAsync();
                }
            }
        }

        private async Task OfferToStartServiceAsync()
        {
            string time = DateTime.Now.ToShortTimeString();
            TaskCompletionSource<bool> answered = new TaskCompletionSource<bool>();
            DialogStartService dialog = new DialogStartService();
            dialog.StartClick += (sender, e) => answered.SetResult(true);
            dialog.NotNowClick += (sender, e) => answered.SetResult(false);
            ShowOverlay(dialog);
            bool start = await answered.Task;
            CloseOverlay();
            if (!start)
            {
                return;
            }

            // Counts as part of the save, so the window can't close before the result shows.
            _isSaving = true;
            SetSaveStatus("Starting the service...", isWarning: false);
            string error;
            try
            {
                error = await ElevatedServiceControl.StartAsync();
            }
            finally
            {
                _isSaving = false;
            }

            if (error != null)
            {
                SetSaveStatus($"Saved at {time}. The service didn't start.", isWarning: true);
                await ShowMessageAsync("Unable to start the service", error);
            }
            else if (IsServiceRunning())
            {
                SetSaveStatus($"Saved at {time}. The service is running.", isWarning: false);
            }
            else
            {
                // The UAC prompt was declined.
                SetSaveStatus($"Saved at {time}. Changes apply when the service starts.", isWarning: false);
            }
        }

        // Saves the configuration, then tells the service to reload it.
        private async Task<SaveOutcome> SaveAndReloadAsync()
        {
            try
            {
                ConfigWriter.Save(_config, _configPath);
            }
            catch (UnauthorizedAccessException)
            {
                // A typical installation only lets administrators change the configuration.
                // The elevated copy both saves and reloads.
                return await SaveElevatedAsync();
            }
            catch (IOException ex)
            {
                return Failed(ex.Message);
            }

            bool reloaded = await Task.Run(() => TcpTriggerService.Reload());
            return reloaded ? SaveOutcome.Saved : SaveOutcome.ReloadFailed;
        }

        // Writes the configuration to a temporary file, then has an elevated copy of this
        // application read it, save it to the real location, and tell the service to reload it.
        private async Task<SaveOutcome> SaveElevatedAsync()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"tcpTrigger-{Guid.NewGuid():N}.xml");
            try
            {
                ConfigWriter.Save(_config, tempPath);

                string arguments = $"{CommandLineArgs.SaveConfig} \"{tempPath}\"";
                ElevatedResult result = await Task.Run(() => ElevatedCommand.Run(arguments));
                switch (result)
                {
                    case ElevatedResult.Succeeded:
                        return SaveOutcome.Saved;
                    case ElevatedResult.ReloadFailed:
                        return SaveOutcome.ReloadFailed;
                    case ElevatedResult.Cancelled:
                        // Declining the UAC prompt is the user's choice, not an error.
                        return SaveOutcome.Cancelled;
                    case ElevatedResult.TimedOut:
                        return Failed("Saving did not finish in time.");
                    case ElevatedResult.NotElevated:
                        return Failed("Windows did not give the save administrator rights.");
                    case ElevatedResult.UnknownCommand:
                        return Failed("The save command was not recognized.");
                    default:
                        return Failed("The configuration file could not be written.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception)
            {
                return Failed(ex.Message);
            }
            finally
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // A leftover file in the user's temp folder is harmless.
                }
            }
        }

        // Records why the save failed, for ShowSaveOutcomeAsync to show once saving is done.
        private SaveOutcome Failed(string reason)
        {
            _saveFailureReason = reason;
            return SaveOutcome.Failed;
        }

        // Shows the result in the footer. Failures also show a message, and the task completes
        // once the user closes it.
        private async Task ShowSaveOutcomeAsync(SaveOutcome outcome)
        {
            string time = DateTime.Now.ToShortTimeString();
            switch (outcome)
            {
                case SaveOutcome.Saved:
                    SetSaveStatus(IsServiceRunning()
                        ? $"Saved and applied at {time}."
                        : $"Saved at {time}. Changes apply when the service starts.",
                        isWarning: false);
                    break;
                case SaveOutcome.ReloadFailed:
                    SetSaveStatus($"Saved at {time}, but the service could not apply the changes.", isWarning: true);
                    await ShowMessageAsync(
                        "Settings saved",
                        $"Settings were saved, but the tcpTrigger service could not apply them.{Environment.NewLine}{Environment.NewLine}"
                        + "Check the Status tab and the tcpTrigger event log for details.");
                    break;
                case SaveOutcome.Cancelled:
                    SetSaveStatus("Not saved. Administrator approval was cancelled.", isWarning: true);
                    break;
                case SaveOutcome.Failed:
                    SetSaveStatus("Not saved.", isWarning: true);
                    await ShowMessageAsync("Unable to save settings", _saveFailureReason);
                    break;
            }
        }

        private void SetSaveStatus(string text, bool isWarning)
        {
            SaveStatus.Text = text;
            SaveStatus.Foreground = (Brush)FindResource(isWarning ? "SaveStatus.Warning" : "SaveStatus.Normal");
        }

        private static bool IsServiceRunning()
        {
            try
            {
                return TcpTriggerService.GetStatus() == ServiceControllerStatus.Running;
            }
            catch
            {
                return false;
            }
        }

        // False if the service is in any other state, or isn't installed.
        private static bool IsServiceStopped()
        {
            try
            {
                return TcpTriggerService.GetStatus() == ServiceControllerStatus.Stopped;
            }
            catch
            {
                return false;
            }
        }
    }
}