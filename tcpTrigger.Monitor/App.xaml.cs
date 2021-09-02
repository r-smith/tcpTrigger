using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace tcpTrigger.Monitor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly Mutex mutex = new Mutex(true, "{DFCD6819-93EA-4040-8C8A-EF0EDE8E43FA}");
        [STAThread]
        public static void Main()
        {
            if (mutex.WaitOne(TimeSpan.Zero, true))
            {
                // Launch application.
                App app = new App();
                app.InitializeComponent();
                app.Run();
                mutex.ReleaseMutex();
            }
            else
            {
                // Application already running.
                // Send window message to notify currently running instance.
                NativeMethods.PostMessage(
                    (IntPtr)NativeMethods.HWND_BROADCAST,
                    NativeMethods.WM_SHOWME,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
        }

        internal static class NativeMethods
        {
            public const int HWND_BROADCAST = 0xffff;
            public static readonly int WM_SHOWME = RegisterWindowMessage("WM_SHOWME");
            [DllImport("user32")]
            public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
            [DllImport("user32")]
            public static extern int RegisterWindowMessage(string message);
        }
    }
}
