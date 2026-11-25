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

partial class PrivacyWatch
{
    [DllImport("advapi32.dll")] static extern int RegNotifyChangeKeyValue(IntPtr key, bool subtree, uint filter, IntPtr ev, bool async);
    const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    readonly List<RegistryKey> keys = new List<RegistryKey>();
    readonly List<AutoResetEvent> events = new List<AutoResetEvent>();
    public event Action Changed;

    public void Start()
    {
        foreach (var cap in new[] { "webcam", "microphone" })
        {
            var key = Registry.CurrentUser.OpenSubKey(Root + cap);
            if (key == null) continue;
            var ev = new AutoResetEvent(false);
            keys.Add(key);
            events.Add(ev);
            Arm(key, ev);
            ThreadPool.RegisterWaitForSingleObject(ev, delegate
            {
                Arm(key, ev);
                var h = Changed;
                if (h != null) h();
            }, null, -1, false);
        }
        StartCameraMonitor();
    }

    // Names, values and subkeys anywhere below; REG_NOTIFY_THREAD_AGNOSTIC so any thread can re-arm it.
    static void Arm(RegistryKey key, AutoResetEvent ev)
    {
        RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), true, 0x1 | 0x4 | 0x10000000, ev.SafeWaitHandle.DangerousGetHandle(), true);
    }

    static string ProcessLabel(uint pid)
    {
        try
        {
            var name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            return name.Length > 0 ? char.ToUpper(name[0]) + name.Substring(1) : null;
        }
        catch (Exception) { return null; }
    }
    public static List<string> InUse(string capability)
    {
        var list = new List<string>();
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Root + capability))
            {
                if (key == null) return list;
                Scan(key, list, false);
                using (var np = key.OpenSubKey("NonPackaged")) if (np != null) Scan(np, list, true);
            }
        }
        catch (Exception) { }
        return list;
    }

    static void Scan(RegistryKey key, List<string> list, bool desktopApp)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            if (name == "NonPackaged") continue;
            using (var app = key.OpenSubKey(name))
            {
                if (app == null) continue;
                object start = app.GetValue("LastUsedTimeStart"), stop = app.GetValue("LastUsedTimeStop");
                if (start is long && (long)start != 0 && stop is long && (long)stop == 0) list.Add(Pretty(name, desktopApp));
            }
        }
    }

    // Stops the live monitor and releases every registry/event handle the watcher armed.
    public void Dispose()
    {
        Changed = null;
        if (sensorMonitor != null)
        {
            try { sensorMonitor.Stop(); } catch (Exception) { }
            try { Marshal.ReleaseComObject(sensorMonitor); } catch (Exception) { }
            sensorMonitor = null;
        }
        foreach (var e in events) e.Dispose();
        events.Clear();
        foreach (var k in keys) k.Dispose();
        keys.Clear();
        if (mfStarted) { try { MFShutdown(); } catch (Exception) { } mfStarted = false; }
    }

    // "C:#Program Files#Google#Chrome#Application#chrome.exe" -> "Chrome"; "Microsoft.WindowsCamera_8wekyb3d8bbwe" -> "WindowsCamera".
    static string Pretty(string name, bool desktopApp)
    {
        string s = name;
        if (desktopApp)
        {
            s = s.Substring(s.LastIndexOf('#') + 1);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
        }
        else
        {
            int u = s.IndexOf('_');
            if (u > 0) s = s.Substring(0, u);
            s = s.Substring(s.LastIndexOf('.') + 1);
        }
        return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : name;
    }
}
