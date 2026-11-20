using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static tcpTrigger.GUI.Interop.NativeMethods;

namespace tcpTrigger.GUI.Interop
{
    // Window helpers built on the Win32 declarations in NativeMethods.
    internal static class WindowInterop
    {
        // Sent by a second instance of the application to ask the running instance to show itself.
        public static readonly int ShowMainWindowMessage = RegisterWindowMessage("WM_SHOW_TCPTRIGGER:" + Environment.UserName);

        public static void BroadcastShowMainWindow()
        {
            PostMessage((IntPtr)HWND_BROADCAST, ShowMainWindowMessage, IntPtr.Zero, IntPtr.Zero);
        }

        // Removes the minimize and maximize buttons and their system menu commands.
        public static void RemoveMinimizeMaximize(IntPtr hwnd)
        {
            SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_MAXIMIZEBOX & ~WS_MINIMIZEBOX);
        }

        // Gives a window with the standard Windows title bar a dark title bar. On Windows 11, the
        // title bar and window border also get the given colors. Windows versions that don't
        // support a setting ignore it.
        public static void UseDarkTitleBar(IntPtr hwnd, Color captionColor, Color borderColor)
        {
            int darkMode = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int)) != 0)
            {
                // Windows 10 before version 20H1 uses an older attribute number.
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
            }

            int caption = ToColorRef(captionColor);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

            int border = ToColorRef(borderColor);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        // Win32 colors are stored as 0x00BBGGRR.
        private static int ToColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        // Call when a borderless window receives WM_GETMINMAXINFO. Without this, a maximized
        // borderless window covers the whole screen, including the taskbar. This limits it
        // to the work area of the monitor the window is on.
        public static void FitMaximizedToWorkArea(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO
                {
                    cbSize = Marshal.SizeOf(typeof(MONITORINFO))
                };
                GetMonitorInfo(monitor, ref monitorInfo);
                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;
                mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        // The work area (the screen minus the taskbar) of the monitor the window is on, in WPF's
        // device-independent units.
        public static Rect GetWorkArea(Window window)
        {
            PresentationSource source = PresentationSource.FromVisual(window);
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            MONITORINFO info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };

            // Not shown yet, or the monitor couldn't be read: use the primary screen's work area.
            if (source == null || monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            {
                return SystemParameters.WorkArea;
            }

            Matrix fromPixels = source.CompositionTarget.TransformFromDevice;
            Point topLeft = fromPixels.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
            Point bottomRight = fromPixels.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));
            return new Rect(topLeft, bottomRight);
        }
    }
}
