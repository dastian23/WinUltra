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

// ---------------------------------------------------------------- Brightness (WMI)

static class Brightness
{
    static readonly object gate = new object();
    static int pending = -1;
    static bool busy;

    public static int Get()
    {
        try
        {
            using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
            using (var all = s.Get())
                foreach (ManagementObject mo in all)
                    using (mo) return Convert.ToInt32(mo["CurrentBrightness"]);
        }
        catch (Exception) { }
        return -1;
    }

    public static bool Set(int level)
    {
        try
        {
            level = Math.Max(0, Math.Min(100, level));
            using (var mc = new ManagementClass("root\\WMI", "WmiMonitorBrightnessMethods", null))
            using (var all = mc.GetInstances())
                foreach (ManagementObject mo in all)
                    using (mo) mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)level });
            return true;
        }
        catch (Exception) { return false; }
    }

    // Slider drags call this many times a second; only the latest value is sent.
    public static void SetLater(int level)
    {
        lock (gate)
        {
            pending = level;
            if (busy) return;
            busy = true;
        }
        ThreadPool.QueueUserWorkItem(delegate
        {
            while (true)
            {
                int v;
                lock (gate)
                {
                    v = pending;
                    pending = -1;
                    if (v < 0) { busy = false; return; }
                }
                try { Set(v); } catch (Exception) { }
            }
        });
    }
}
