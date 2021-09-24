using System.Windows;
using System.Windows.Media;

namespace tcpTrigger.Manager
{
    /// <summary>
    /// Interaction logic for DialogWindow.xaml
    /// </summary>
    public partial class DialogWindow : Window
    {
        public enum Type
        {
            Warning,
            Error,
            Info,
            None
        }

        public DialogWindow(Type type, string title, string body, string confirmationText, bool isCancelButtonVisible)
        {
            InitializeComponent();

            MyTitle.Text = title;
            Body.Text = body;
            OK.Content = confirmationText;

            if (!isCancelButtonVisible)
                Cancel.Visibility = Visibility.Collapsed;

            switch (type)
            {
                case Type.Warning:
                    MyIcon.Source = (DrawingImage)Application.Current.Resources["icon.exclamation-triangle"];
                    break;
                case Type.Error:
                    MyIcon.Source = (DrawingImage)Application.Current.Resources["icon.exclamation-circle"];
                    break;
                case Type.Info:
                    MyIcon.Source = (DrawingImage)Application.Current.Resources["icon.info-circle"];
                    break;
                case Type.None:
                    MyIcon.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        public static DialogWindow Error(string message, string title = "Error")
        {
            System.Media.SystemSounds.Exclamation.Play();
            return new DialogWindow(
                type: Type.Error,
                title: title,
                body: message,
                confirmationText: "OK",
                isCancelButtonVisible: false);
        }

        public static DialogWindow Warning(string message, string confirmButtonText)
        {
            System.Media.SystemSounds.Exclamation.Play();
            return new DialogWindow(
                type: Type.Warning,
                title: "Warning",
                body: message,
                confirmationText: confirmButtonText,
                isCancelButtonVisible: true);
        }

        public static DialogWindow Info(string message, string title)
        {
            System.Media.SystemSounds.Beep.Play();
            return new DialogWindow(
                type: Type.Info,
                title: title,
                body: message,
                confirmationText: "OK",
                isCancelButtonVisible: false);
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
