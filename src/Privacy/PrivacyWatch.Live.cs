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
    // ---- live detection (Windows 11 25H2 stopped updating the registry records reliably)
    [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] static extern int MFShutdown();
    [DllImport("mfsensorgroup.dll")] static extern int MFCreateSensorActivityMonitor(IMFSensorActivitiesReportCallback callback, out IMFSensorActivityMonitor monitor);
    bool mfStarted;
    static readonly object liveLock = new object();
    static List<uint> cameraPids = new List<uint>();
    static readonly uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
    SensorReportCallback sensorCallback;
    IMFSensorActivityMonitor sensorMonitor;

    public bool CameraMonitorRunning { get { return sensorMonitor != null; } }

    void StartCameraMonitor()
    {
        try
        {
            mfStarted = MFStartup(0x00020070, 1) == 0;    // MF_VERSION, MFSTARTUP_LITE
            sensorCallback = new SensorReportCallback(OnSensorReport);
            if (MFCreateSensorActivityMonitor(sensorCallback, out sensorMonitor) == 0) sensorMonitor.Start();
        }
        catch (Exception ex) { Store.Log("camera monitor: " + ex.Message); }
    }

    // Each report lists the cameras and which processes are streaming from them right now.
    void OnSensorReport(IMFSensorActivitiesReport report)
    {
        var pids = new List<uint>();
        uint n;
        if (report.GetCount(out n) != 0) return;
        for (uint i = 0; i < n; i++)
        {
            IMFSensorActivityReport device;
            if (report.GetActivityReport(i, out device) != 0 || device == null) continue;
            uint count;
            if (device.GetProcessCount(out count) == 0)
                for (uint j = 0; j < count; j++)
                {
                    IMFSensorProcessActivity act;
                    if (device.GetProcessActivity(j, out act) != 0 || act == null) continue;
                    uint pid; int streaming;
                    if (act.GetProcessId(out pid) == 0 && act.GetStreamingState(out streaming) == 0 && streaming != 0 && pid != ownPid && !pids.Contains(pid)) pids.Add(pid);
                    Marshal.ReleaseComObject(act);
                }
            Marshal.ReleaseComObject(device);
        }
        lock (liveLock) cameraPids = pids;
        var h = Changed;
        if (h != null) h();
    }

    public static List<string> LiveCamera()
    {
        List<uint> pids;
        lock (liveLock) pids = new List<uint>(cameraPids);
        return pids.Select(ProcessLabel).Where(x => x != null).Distinct().ToList();
    }

    // Microphone: any recording (capture) audio session that is active right now, the same thing the volume mixer sees.
    public static List<string> LiveMicrophone()
    {
        var list = new List<string>();
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IntPtr pc;
            if (en.EnumAudioEndpoints(1, 1, out pc) != 0 || pc == IntPtr.Zero) return list;    // eCapture, DEVICE_STATE_ACTIVE
            var devices = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pc);
            Marshal.Release(pc);
            uint n;
            devices.GetCount(out n);
            var iid = typeof(IAudioSessionManager2).GUID;
            for (uint i = 0; i < n; i++)
            {
                IMMDevice dev;
                object o;
                if (devices.Item(i, out dev) != 0 || dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) continue;
                IAudioSessionEnumerator sessions;
                if (((IAudioSessionManager2)o).GetSessionEnumerator(out sessions) == 0)
                {
                    int count;
                    sessions.GetCount(out count);
                    for (int k = 0; k < count; k++)
                    {
                        object so;
                        if (sessions.GetSession(k, out so) != 0) continue;
                        var ctl = so as IAudioSessionControl2;
                        int state; uint pid;
                        if (ctl != null && ctl.GetState(out state) == 0 && state == 1 && ctl.GetProcessId(out pid) == 0 && pid != 0 && pid != ownPid)
                        {
                            var name = ProcessLabel(pid);
                            if (name != null && !list.Contains(name)) list.Add(name);
                        }
                        Marshal.ReleaseComObject(so);
                    }
                    Marshal.ReleaseComObject(sessions);
                }
                Marshal.ReleaseComObject(o);
                Marshal.ReleaseComObject(dev);
            }
            Marshal.ReleaseComObject(devices);
            Marshal.ReleaseComObject(en);
        }
        catch (Exception) { }
        return list;
    }
}
