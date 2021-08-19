using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace tcpTrigger.Editor
{
    /// <summary>
    /// Interaction logic for RestartServiceWindow.xaml
    /// </summary>
    public partial class RestartServiceWindow : Window
    {
        private const int EXIT_SUCCESS = 0;
        private const int EXIT_FAILURE = 1;
        private const int EXIT_CANCEL = 2;

        public RestartServiceWindow()
        {
            InitializeComponent();

            Overlay.Visibility = Visibility.Visible;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Setup a background thread to restart the service.
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += new DoWorkEventHandler(Thread_RestartService);
            bw.RunWorkerCompleted += new RunWorkerCompletedEventHandler(Thread_RestartServiceCompleted);
            bw.RunWorkerAsync();
        }

        private void Thread_RestartService(object sender, DoWorkEventArgs e)
        {
            try
            {
                // To restart the tcpTrigger service, a new process is started that requests UAC elevation.
                // The process is a new instance of this application, but with command line arguments
                // passed in to tell the new instance of this app to restart the tcpTrigger service and exit.
                // See Application_Startup() method in App.xaml.cs for handling of command line arguments.

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.Verb = "runas";
                startInfo.UseShellExecute = true;
                startInfo.WorkingDirectory = Environment.CurrentDirectory;
                startInfo.FileName = System.Reflection.Assembly.GetExecutingAssembly().Location;
                startInfo.Arguments = "--restart-service";
                using (Process process = Process.Start(startInfo))
                {
                    const int _maxWaitMilliseconds = 45 * 1000;
                    bool didProcessExit = process.WaitForExit(_maxWaitMilliseconds);
                    if (!didProcessExit || process.ExitCode == EXIT_FAILURE)
                    {
                        // Process started, but either it exceeded the maximum wait time to exit, or it
                        // ran and exited with a failure code. Set thread result to failure.
                        e.Result = EXIT_FAILURE;
                    }
                }
                // Success. Process ran and exited with a success code. This should indicate that the
                // tcpTrigger service restarted successfully. Set thread result to success.
                e.Result = EXIT_SUCCESS;
            }
            catch (Win32Exception)
            {
                // The user refused elevation.
                e.Result = EXIT_CANCEL;
            }
            catch
            {
                // Something unexpected happened when trying to run the process.
                e.Result = EXIT_FAILURE;
            }
        }

        private void Thread_RestartServiceCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            const string _successTitle = "Your settings were applied";
            const string _successMessage = "The tcpTrigger service restarted successfully with your new settings applied.";
            const string _successIcon = "icon.check-circle";
            const string _successBrush = "#2263e6be";
            const string _failTitle = "Unable to restart service";
            const string _failMessage = "Use the Windows services management console to manually restart the tcpTrigger service.";
            const string _failIcon = "icon.exclamation-circle";
            const string _failBrush = "#22ffd43b";
            const string _cancelTitle = "Cancelled";
            const string _cancelMessage = "When you're ready to apply your changes, restart the tcpTrigger service.";

            // Set gui to show the result of the service restart.
            if (e.Result != null && (int)e.Result == EXIT_SUCCESS)
            {
                // The service restarted successfully.
                ResultTitle.Text = _successTitle;
                ResultIcon.Source = (DrawingImage)Application.Current.Resources[_successIcon];
                ResultBorder.Background = (Brush)new BrushConverter().ConvertFromString(_successBrush);
                ResultMessage.Text = _successMessage;
            }
            else if (e.Result != null && (int)e.Result == EXIT_CANCEL)
            {
                // Cancelled UAC elevation.
                ResultTitle.Text = _cancelTitle;
                ResultIcon.Source = (DrawingImage)Application.Current.Resources[_failIcon];
                ResultBorder.Background = (Brush)new BrushConverter().ConvertFromString(_failBrush);
                ResultMessage.Text = _cancelMessage;
            }
            else
            {
                // Failed to restart service.
                ResultTitle.Text = _failTitle;
                ResultIcon.Source = (DrawingImage)Application.Current.Resources[_failIcon];
                ResultBorder.Background = (Brush)new BrushConverter().ConvertFromString(_failBrush);
                ResultMessage.Text = _failMessage;
            }

            // Hide the restart progress overlay.
            Overlay.Visibility = Visibility.Collapsed;
            OK.IsEnabled = true;
        }
    }
}
