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
        EvaluateFullScreen();

        // Poll slowly while the user is away, slowly while hidden, and quickly when it matters.
        var input = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.LASTINPUTINFO)) };
        bool idle = Native.GetLastInputInfo(ref input) && unchecked((uint)Environment.TickCount - input.dwTime) > 120000;
        double want = idle ? 5000 : 1500;
        if (pollTimer.Interval.TotalMilliseconds != want) pollTimer.Interval = TimeSpan.FromMilliseconds(want);

        if (pinnedOpen && mode == Mode.Expanded && (DateTime.Now - pinnedAt).TotalSeconds > 5 && !CursorOverIsland())
            Collapse();
        if (mode == Mode.Expanded && !pinnedOpen && !draggingOut && !dragInside && !CursorOverIsland())
            Collapse();
    }
}
