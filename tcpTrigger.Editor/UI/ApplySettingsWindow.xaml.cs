using System;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Media.Imaging;

namespace tcpTrigger.Editor
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

            BitmapSource shieldSource;
            try
            {
                // Get UAC icon.
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    Elevate.SHSTOCKICONINFO sii = new Elevate.SHSTOCKICONINFO();
                    sii.cbSize = (UInt32)Marshal.SizeOf(typeof(Elevate.SHSTOCKICONINFO));

                    Marshal.ThrowExceptionForHR(Elevate.SHGetStockIconInfo(Elevate.SHSTOCKICONID.SIID_SHIELD,
                        Elevate.SHGSI.SHGSI_ICON | Elevate.SHGSI.SHGSI_SMALLICON,
                        ref sii));

                    shieldSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        sii.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    Elevate.DestroyIcon(sii.hIcon);
                }
                else
                {
                    shieldSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        System.Drawing.SystemIcons.Shield.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch
            {
                // Failed to get UAC icon. Icon will be null - this is fine.
                shieldSource = null;
            }

            try
            {
                switch (GetServiceStatus())
                {
                    case ServiceControllerStatus.Running:
                        Message.Text = _isRunningMessage;
                        ElevateImage.Source = shieldSource;
                        OKText.Text = "Apply now";
                        Cancel.Content = "Later";
                        break;
                    case ServiceControllerStatus.Stopped:
                        Message.Text = _isStoppedMessage;
                        ElevateImage.Source = shieldSource;
                        OKText.Text= "Start service";
                        Cancel.Content = "Later";
                        break;
                    default:
                        Message.Text = _isUnknownMessage;
                        Cancel.Content = "OK";
                        OK.Visibility = Visibility.Collapsed;
                        break;
                }
                if (shieldSource == null)
                    ElevateImage.Visibility = Visibility.Collapsed;
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
