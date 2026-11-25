using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MediaManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;
using Radio = Windows.Devices.Radios.Radio;
using RadioKind = Windows.Devices.Radios.RadioKind;
using RadioState = Windows.Devices.Radios.RadioState;

partial class Island
{
    // ---------------------------------------------------------------- full-screen check and the adaptive poll

    readonly StringBuilder className = new StringBuilder(64);

    void Poll()
    {
        bool full = DateTime.Now >= forceShowUntil && FullScreenInFront();

        // Poll slowly while the user is away, slowly while hidden, and quickly when it matters.
        var input = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.LASTINPUTINFO)) };
        bool idle = Native.GetLastInputInfo(ref input) && unchecked((uint)Environment.TickCount - input.dwTime) > 120000;
        double want = idle ? 5000 : (hidden ? 2500 : 1500);
        if (pollTimer.Interval.TotalMilliseconds != want) pollTimer.Interval = TimeSpan.FromMilliseconds(want);

        if (full && !hidden)
        {
            hidden = true;
            if (mode == Mode.Expanded) Collapse();
            StopBars();
            Hide();
        }
        else if (!full && hidden)
        {
            hidden = false;
            Show();
            Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);    // HWND_TOPMOST, no move/size/activate
            if (mode == Mode.Collapsed && playing && !timerRunning && !stopwatch.IsRunning) StartBars();
        }

        if (pinnedOpen && mode == Mode.Expanded && (DateTime.Now - pinnedAt).TotalSeconds > 12 && !CursorOverIsland())
            Collapse();
        if (mode == Mode.Expanded && !pinnedOpen && !draggingOut && !dragInside && !CursorOverIsland())
            Collapse();
    }

    bool FullScreenInFront()
    {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == hwnd) return false;
        className.Length = 0;
        Native.GetClassName(fg, className, className.Capacity);
        string cls = className.ToString();
        if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "XamlExplorerHostIslandWindow") return false;

        int state;
        if (Native.SHQueryUserNotificationState(out state) == 0 && (state == 3 || state == 4)) return true;    // D3D full screen, presentation mode

        Native.RECT r;
        // A maximised window with an auto-hide taskbar also covers the screen, but it is not full screen.
        if (Native.IsZoomed(fg) || !Native.GetWindowRect(fg, out r)) return false;
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(fg, 2), ref mi) || (mi.dwFlags & 1) == 0) return false;
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }
}
