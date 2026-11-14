using System;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using tcpTrigger.GUI.Configuration;
using tcpTrigger.GUI.Interop;
using tcpTrigger.GUI.Models;
using tcpTrigger.GUI.Services;
using tcpTrigger.GUI.Views.Dialogs;
using WinForms = System.Windows.Forms;

namespace tcpTrigger.GUI.Views
{
    public partial class MainWindow : Window
    {
        // Each view's default window size. The compact view comes from MainWindow.xaml.
        private Size _compactViewSize;
        private static readonly Size s_detailedViewSize = new Size(970, 396);

        private WinForms.NotifyIcon _trayIcon;
        private readonly MonitorView _monitorView;

        public MainWindow()
        {
            InitializeComponent();

            UserPreferences.Load();
            UpdateMenuItemCheckedState();
            CreateTrayIcon();
            ProcessCommandLineArgs();

            _compactViewSize = new Size(Width, Height);
            if (UserPreferences.DetailedView)
            {
                ResizeTo(s_detailedViewSize);
            }

            _monitorView = new MonitorView();
            MonitorContent.Content = _monitorView;
        }

        // Shows the window if it's hidden in the tray or minimized.
        public void RestoreWindow()
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }

        // Shows the window and keeps it above other windows for a few seconds.
        internal async void ShowOnTop()
        {
            RestoreWindow();
            Topmost = true;
            await Task.Delay(3000);
            Topmost = false;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && UserPreferences.MinimizeToTray)
            {
                HideToTray();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (UserPreferences.ExitToTray)
            {
                HideToTray();
                e.Cancel = true;
                return;
            }

            if (PromptForUnsavedSettings())
            {
                e.Cancel = true;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
        }

        // Returns true if the Settings window has unsaved changes. It's brought forward and asks
        // the user to save them, and the app stays open for now. Settings finishes the exit once
        // the user saves or chooses not to. This has to run before the app starts shutting down:
        // after that, the Settings window can no longer cancel its own close.
        private static bool PromptForUnsavedSettings()
        {
            SettingsWindow settings = SettingsWindow.Instance;
            if (settings == null || !settings.HasUnsavedChanges)
            {
                return false;
            }

            if (settings.WindowState == WindowState.Minimized)
            {
                settings.WindowState = WindowState.Normal;
            }
            settings.Activate();
            settings.CloseForAppExit();
            return true;
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

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            await OpenSettingsAsync();
        }

        // Shows the window when the service stops.
        private void ServiceStatusBar_MonitoringStopped(object sender, EventArgs e)
        {
            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                ShowOnTop();
            }
        }

        // From the status bar's menu: opens Settings on the Status tab.
        private async void ServiceDetails_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settings = await OpenSettingsAsync();
            settings?.ShowStatusTab();
        }

        // Brings Settings forward, opening it first if it isn't open yet. Returns null if the
        // configuration file couldn't be read.
        private async Task<SettingsWindow> OpenSettingsAsync()
        {
            if (SettingsWindow.Instance != null)
            {
                SettingsWindow.Instance.Activate();
                return SettingsWindow.Instance;
            }

            string configPath = ConfigPaths.GetConfigPath();
            ServiceConfig config = await LoadConfigAsync(configPath);
            if (config == null)
            {
                return null;
            }

            // Open centered over this window.
            SettingsWindow settings = new SettingsWindow(config, configPath)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            settings.Show();
            settings.Owner = null;

            // Ensure settings window isn't pushed off the screen.
            settings.Top = Math.Max(settings.Top, SystemParameters.VirtualScreenTop);
            return settings;
        }

        // Opens Settings on the Ignore tab with a new rule filled in from `draft`.
        internal async Task CreateIgnoreRuleAsync(IgnoreRule draft)
        {
            SettingsWindow settings = await OpenSettingsAsync();
            if (settings == null)
            {
                return;
            }

            // Let a Settings window that was just opened finish appearing before the editor
            // opens over it.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            settings.CreateIgnoreRule(draft);
        }

        // Returns null if the configuration file exists but can't be read.
        // The user has already been shown why.
        private async Task<ServiceConfig> LoadConfigAsync(string configPath)
        {
            try
            {
                return ConfigReader.Load(configPath);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                // A new installation has no settings file yet. Settings opens with the
                // defaults, marked as unsaved, so saving them creates the file.
                return ServiceConfig.CreateDefault();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // IOException includes FileLoadException, which ConfigReader throws for parse errors.
                await ShowMessageAsync("Unable to load settings", ex.Message);
                return null;
            }
        }

        // Shows a dialog asking the user to confirm stopping the service.
        // Returns true if they clicked Stop service, or false if they canceled.
        internal Task<bool> ConfirmStopServiceAsync()
        {
            TaskCompletionSource<bool> answered = new TaskCompletionSource<bool>();
            DialogStopService dialog = new DialogStopService();
            dialog.StopClick += (sender, e) => CloseDialog(answered, true);
            dialog.CancelClick += (sender, e) => CloseDialog(answered, false);
            ShowOverlay(dialog);
            return answered.Task;
        }

        private void CloseDialog(TaskCompletionSource<bool> answered, bool result)
        {
            CloseOverlay();
            answered.SetResult(result);
        }

        // Shows a message in the dialog overlay. The task completes when the user closes it.
        // Also brings this window back from the tray. If copyText is given, a Copy button is displayed.
        internal Task ShowMessageAsync(string title, string message, string copyText = null)
        {
            if (!IsVisible)
            {
                RestoreWindow();
            }

            TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>();
            DialogMessage dialog = new DialogMessage(title, message, copyText);
            dialog.ConfirmClick += (sender, e) =>
            {
                CloseOverlay();
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

        private void ProcessCommandLineArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1] == CommandLineArgs.Minimized)
            {
                HideToTray();
            }
        }

        private void UpdateMenuItemCheckedState()
        {
            LaunchAtLogonOption.IsChecked = UserPreferences.LaunchAtLogon;
            MinimizeToTrayOption.IsChecked = UserPreferences.MinimizeToTray;
            ExitToTrayOption.IsChecked = UserPreferences.ExitToTray;
            FocusOnUpdateOption.IsChecked = UserPreferences.FocusOnUpdate;
            DetailedViewOption.IsChecked = UserPreferences.DetailedView;
        }

        private void CreateTrayIcon()
        {
            try
            {
                // Build context menu for tray icon.
                WinForms.ContextMenuStrip menuStrip = new WinForms.ContextMenuStrip();
                WinForms.ToolStripMenuItem menuStatus = new WinForms.ToolStripMenuItem { Enabled = false };
                menuStrip.Opening += (s, args) => menuStatus.Text = GetTrayStatusText();
                menuStrip.Items.Add(menuStatus);
                menuStrip.Items.Add(new WinForms.ToolStripSeparator());
                WinForms.ToolStripMenuItem menuOpen = new WinForms.ToolStripMenuItem("Open tcpTrigger");
                menuOpen.Click += (s, args) => RestoreWindow();
                menuStrip.Items.Add(menuOpen);
                WinForms.ToolStripMenuItem menuSettings = new WinForms.ToolStripMenuItem("Settings");
                menuSettings.Click += async (s, args) => await OpenSettingsAsync();
                menuStrip.Items.Add(menuSettings);
                menuStrip.Items.Add(new WinForms.ToolStripSeparator());
                WinForms.ToolStripMenuItem menuExit = new WinForms.ToolStripMenuItem("Exit tcpTrigger");
                menuExit.Click += (s, args) =>
                {
                    if (PromptForUnsavedSettings())
                    {
                        return;
                    }

                    Application.Current.Shutdown();
                };
                menuStrip.Items.Add(menuExit);

                // Create tray icon.
                _trayIcon = new WinForms.NotifyIcon
                {
                    Icon = new System.Drawing.Icon(
                        Application.GetResourceStream(new Uri("pack://application:,,,/tcpTrigger.ico")).Stream,
                        WinForms.SystemInformation.SmallIconSize),
                    Text = "tcpTrigger",
                    ContextMenuStrip = menuStrip
                };
                _trayIcon.MouseUp += (s, args) =>
                {
                    if (args.Button == WinForms.MouseButtons.Left)
                    {
                        RestoreWindow();
                    }
                };
                _trayIcon.Visible = true;
            }
            catch
            {
                // Failed to create a tray icon. Disable minimize and exit to tray options
                // for this session only (not saved).
                _trayIcon?.Dispose();
                _trayIcon = null;
                UserPreferences.ExitToTray = false;
                UserPreferences.MinimizeToTray = false;
                UpdateMenuItemCheckedState();
            }
        }

        // A short service status for the tray menu, such as "Monitoring".
        private static string GetTrayStatusText()
        {
            try
            {
                switch (TcpTriggerService.GetStatus())
                {
                    case ServiceControllerStatus.Running:
                        return "Monitoring";
                    case ServiceControllerStatus.Stopped:
                        return File.Exists(ConfigPaths.GetConfigPath())
                            ? "Not monitoring: the service is stopped"
                            : "Not monitoring: tcpTrigger isn't set up yet";
                    case ServiceControllerStatus.StartPending:
                        return "Not monitoring yet: the service is starting";
                    case ServiceControllerStatus.StopPending:
                        return "Not monitoring: the service is stopping";
                    default:
                        return "Not monitoring: the service isn't running";
                }
            }
            catch (InvalidOperationException)
            {
                return "Not monitoring: the service isn't installed";
            }
            catch
            {
                return "The service status couldn't be checked";
            }
        }

        private void HideToTray()
        {
            Visibility = Visibility.Hidden;
        }

        private void MinimizeToTrayOption_Click(object sender, RoutedEventArgs e)
        {
            UserPreferences.SetMinimizeToTray(MinimizeToTrayOption.IsChecked);
        }

        private void ExitToTrayOption_Click(object sender, RoutedEventArgs e)
        {
            UserPreferences.SetExitToTray(ExitToTrayOption.IsChecked);
        }

        private void DetailedViewOption_Click(object sender, RoutedEventArgs e)
        {
            SetDetailedView(DetailedViewOption.IsChecked);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // F9 toggles the view between detailed and compact.
            if (e.Key == Key.F9 && Keyboard.Modifiers == ModifierKeys.None)
            {
                SetDetailedView(!UserPreferences.DetailedView);
                e.Handled = true;
            }
            // Ctrl+F opens the filter.
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control
                && DialogOverlay.Visibility != Visibility.Visible)
            {
                _monitorView.ToggleFilter();
                e.Handled = true;
            }
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            _monitorView.ToggleFilter();
        }

        // Switches the view, saves the choice, and sets the menu item's check mark.
        private void SetDetailedView(bool detailedView)
        {
            UserPreferences.SetDetailedView(detailedView);
            DetailedViewOption.IsChecked = detailedView;
            _monitorView.SetDetailedView(detailedView);

            if (WindowState == WindowState.Normal)
            {
                if (detailedView && IsSize(_compactViewSize))
                {
                    ResizeTo(s_detailedViewSize);
                    KeepOnScreen();
                }
                else if (!detailedView && IsSize(s_detailedViewSize))
                {
                    ResizeTo(_compactViewSize);
                }
            }
        }

        // Moves the window back onto its screen if growing pushed it past the right or bottom edge.
        private void KeepOnScreen()
        {
            Rect workArea = WindowInterop.GetWorkArea(this);

            if (Left + Width > workArea.Right)
            {
                Left = Math.Max(workArea.Left, workArea.Right - Width);
            }
            if (Top + Height > workArea.Bottom)
            {
                Top = Math.Max(workArea.Top, workArea.Bottom - Height);
            }
        }

        // True if the window is at this size.
        private bool IsSize(Size size)
        {
            return Math.Abs(Width - size.Width) < 1 && Math.Abs(Height - size.Height) < 1;
        }

        private void ResizeTo(Size size)
        {
            Width = size.Width;
            Height = size.Height;
        }

        private void FocusOnUpdateOption_Click(object sender, RoutedEventArgs e)
        {
            UserPreferences.SetFocusOnUpdate(FocusOnUpdateOption.IsChecked);
        }

        private async void LaunchAtLogonOption_Click(object sender, RoutedEventArgs e)
        {
            bool launchAtLogon = LaunchAtLogonOption.IsChecked;
            try
            {
                if (launchAtLogon)
                {
                    StartupEntry.Create();
                }
                else
                {
                    StartupEntry.Delete();
                }
                UserPreferences.LaunchAtLogon = launchAtLogon;
            }
            catch (Exception ex)
            {
                // Failed to change the Run value in the registry.
                UserPreferences.LaunchAtLogon = !launchAtLogon;
                LaunchAtLogonOption.IsChecked = !launchAtLogon;
                await ShowMessageAsync("Unable to change the sign-in setting", ex.Message);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ((HwndSource)PresentationSource.FromVisual(this)).AddHook(HookProc);
        }

        private IntPtr HookProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_GETMINMAXINFO)
            {
                WindowInterop.FitMaximizedToWorkArea(hwnd, lParam);
            }
            else if (msg == WindowInterop.ShowMainWindowMessage)
            {
                // Another instance was started. Bring this window to the front.
                RestoreWindow();
            }

            return IntPtr.Zero;
        }
    }
}