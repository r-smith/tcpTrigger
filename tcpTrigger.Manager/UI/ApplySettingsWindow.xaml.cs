using System.ServiceProcess;
using System.Windows;

namespace tcpTrigger.Manager
{
    /// <summary>
    /// Interaction logic for ApplySettingsWindow.xaml
    /// </summary>
    public partial class ApplySettingsWindow : Window
    {
        private const string _serviceName = "tcpTrigger";
        private const string _isRunningMessage = "To apply your changes, the tcpTrigger service must restart. "
                                                 + "Would you like to restart the service and apply your new settings now?";
        private const string _isStoppedMessage = "The tcpTrigger service is not currently running. "
                                                 + "Would you like to start the serivce now?";
        private const string _isUnknownMessage = "Could not determine the status of the tcpTrigger service. "
                                                 + "Check the Windows services management console to ensure the service is running.";

        

        public ApplySettingsWindow()
        {
            InitializeComponent();

            ElevateImage.Source = Elevate.GetUacIcon();
            if (ElevateImage.Source == null)
            {
                ElevateImage.Visibility = Visibility.Collapsed;
            }

            try
            {
                switch (GetServiceStatus())
                {
                    case ServiceControllerStatus.Running:
                        Message.Text = _isRunningMessage;
                        OKText.Text = "Apply now";
                        Cancel.Content = "Later";
                        break;
                    case ServiceControllerStatus.Stopped:
                        Message.Text = _isStoppedMessage;
                        OKText.Text= "Start service";
                        Cancel.Content = "Later";
                        break;
                    default:
                        Message.Text = _isUnknownMessage;
                        Cancel.Content = "OK";
                        OK.Visibility = Visibility.Collapsed;
                        break;
                }
            }
            catch
            {
                // Error when attempting to retrieve tcpTrigger service status.
                // Set to window to unknown state.
                Message.Text = _isUnknownMessage;
                Cancel.Content = "OK";
                OK.Visibility = Visibility.Collapsed;
            }
        }

        private ServiceControllerStatus GetServiceStatus()
        {
            return new ServiceController(_serviceName).Status;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            //StartServiceViaProcess("tcpTrigger");
            DialogResult = true;
        }
    }
}
