using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace tcpTrigger.Manager
{
    /// <summary>
    /// Interaction logic for HelpWindow.xaml
    /// </summary>
    public partial class HelpWindow : Window
    {
        public static HelpWindow _OpenWindow = null;

        public HelpWindow()
        {
            InitializeComponent();

            Version version = typeof(MainWindow).Assembly.GetName().Version;
            Version.Inlines.Clear();
            Version.Inlines.Add(new Run($"Version: {version.Major}.{version.Minor}.{version.Build}"));

            // Set initial focus to scrollviewer.  That way you can scroll the help window with the keyboard
            // without having to first click in the window.
            MainDocument.Focus();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            }
            catch { }
            finally
            {
                e.Handled = true;
            }
        }

        private void Overview_Selected(object sender, RoutedEventArgs e)
        {
            TopOfDocument.BringIntoView();
        }

        private void DeploymentGuide_Selected(object sender, RoutedEventArgs e)
        {
            DeploymentGuide.BringIntoView();
        }

        private void TriggerRules_Selected(object sender, RoutedEventArgs e)
        {
            TriggerRules.BringIntoView();
        }

        private void Actions_Selected(object sender, RoutedEventArgs e)
        {
            Actions.BringIntoView();
        }

        private void Email_Selected(object sender, RoutedEventArgs e)
        {
            EmailNotificaitons.BringIntoView();
        }

        private void Whitelist_Selected(object sender, RoutedEventArgs e)
        {
            Whitelist.BringIntoView();
        }

        private void Devices_Selected(object sender, RoutedEventArgs e)
        {
            Devices.BringIntoView();
        }

        private void Status_Selected(object sender, RoutedEventArgs e)
        {
            ServiceStatus.BringIntoView();
        }

        private void MainDocument_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            DependencyObject scrollHost = sender as DependencyObject;
            ScrollViewer scrollViewer = GetScrollViewer(scrollHost) as ScrollViewer;

            if (scrollViewer != null)
            {
                double scrollSpeed = 5.0;
                double offset = scrollViewer.VerticalOffset - (e.Delta * scrollSpeed / 6);
                if (offset < 0)
                {
                    scrollViewer.ScrollToVerticalOffset(0);
                }
                else if (offset > scrollViewer.ExtentHeight)
                {
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.ExtentHeight);
                }
                else
                {
                    scrollViewer.ScrollToVerticalOffset(offset);
                }

                e.Handled = true;
            }
            else
            {
                //throw new NotSupportedException("ScrollSpeed Attached Property is not attached to an element containing a ScrollViewer.");
            }
        }

        public static DependencyObject GetScrollViewer(DependencyObject o)
        {
            // Return the DependencyObject if it is a ScrollViewer
            if (o is ScrollViewer)
            { return o; }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var child = VisualTreeHelper.GetChild(o, i);

                var result = GetScrollViewer(child);
                if (result == null)
                {
                    continue;
                }
                else
                {
                    return result;
                }
            }

            return null;
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _OpenWindow = this;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _OpenWindow = null;
        }
    }
}
