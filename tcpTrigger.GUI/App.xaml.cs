using System;
using System.Windows;
using System.Windows.Threading;

namespace tcpTrigger.GUI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Last line of defense for errors that weren't handled where they happened.
            // Expected errors (such as a config file that can't be read or saved) are
            // handled closer to the source. Anything that reaches here is a bug.
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        // An unhandled exception on the UI thread.
        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // The app may be in an unknown state, so report the error and close
            // rather than keep running.
            e.Handled = true;
            ShowFatalError(e.Exception);
            Shutdown(1);
        }

        // An unhandled exception on a background thread. Windows ends the process
        // after this returns, so all that can be done is to report it.
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowFatalError(e.ExceptionObject as Exception);
        }

        private static void ShowFatalError(Exception ex)
        {
            // Press Ctrl+C in the message box to copy its text, including the details.
            MessageBox.Show(
                "tcpTrigger encountered an unexpected error and needs to close."
                + Environment.NewLine + Environment.NewLine
                + "Details:" + Environment.NewLine
                + (ex?.ToString() ?? "No details available."),
                "tcpTrigger",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}