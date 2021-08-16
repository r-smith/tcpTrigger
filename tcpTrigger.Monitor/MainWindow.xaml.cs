using System;
using System.IO;
using System.Windows;
using System.Xml;

namespace tcpTrigger.Monitor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string LogPath;

        public MainWindow()
        {
            InitializeComponent();

            LogPath = GetLogPath(settingsPath: GetSettingsPath());
            if (string.IsNullOrEmpty(LogPath))
            {
                // No log file found. Display error.
            }
            else if (!Directory.Exists(Path.GetDirectoryName(LogPath)))
            {
                // Log path successfully read from settings, but the directory it points to does not exist. Display error.
            }
            else
            {
                try
                {
                    // Folder containing log file found. Start file system watcher to monitor changes to file.
                    // Log file doesn't necessarily have to exist at this point.
                    Watch();
                    if (File.Exists(LogPath))
                        Log.Text = File.ReadAllText(LogPath);
                }
                catch
                {

                }
            }
        }

        private void Watch()
        {
            if (!string.IsNullOrEmpty(LogPath))
            {
                FileSystemWatcher watcher = new FileSystemWatcher
                {
                    Path = Path.GetDirectoryName(LogPath),
                    Filter = Path.GetFileName(LogPath),
                    NotifyFilter = NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                watcher.Changed += new FileSystemEventHandler(OnChanged);
            }
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            if (e.FullPath.ToLower().Equals(LogPath.ToLower()))
            {
                try
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Log.Text = File.ReadAllText(LogPath);
                        Log.CaretIndex = Log.Text.Length;
                        Log.ScrollToEnd();
                    }));
                }
                catch
                {

                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Log.ScrollToEnd();
        }

        private string GetSettingsPath()
        {
            // Locate the tcpTrigger.xml settings file.
            // First check the current directory. If not found, use ProgramData, regardless if the file exists or not.
            const string _fileName = "tcpTrigger.xml";
            if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + _fileName))
                return AppDomain.CurrentDomain.BaseDirectory + _fileName;
            else
                return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + @"\tcpTrigger\" + _fileName;
        }

        private string GetLogPath(string settingsPath)
        {
            const string _xmlNodeLogPath = "/tcpTrigger/actionSettings/logPath";

            // Read settings XML document to get the log path.
            try
            {
                XmlDocument xd = new XmlDocument();
                xd.Load(settingsPath);
                return xd.DocumentElement.SelectSingleNode(_xmlNodeLogPath)?.InnerText;
            }
            catch
            {
                // Either settings file was not found or something went wrong when parsing the XML.
                // Return an empty string for the path.
                return string.Empty;
            }
        }
    }
}
