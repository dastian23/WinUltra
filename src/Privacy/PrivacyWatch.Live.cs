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

    // Event-driven capture-session monitor. COM calls back on arbitrary threads; state is guarded by micMonitorLock.
    static readonly object micMonitorLock = new object();
    static IMMDeviceEnumerator micDeviceEnumerator;
    static IMMNotificationClient micDeviceNotification;
    static readonly List<MicDeviceMonitor> micDevices = new List<MicDeviceMonitor>();
    static List<PrivacyApp> micSessionApps = new List<PrivacyApp>();
    static bool micMonitorStarted;

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
        return LiveMicrophoneApps().Select(a => a.Name).ToList();
    }

    public static List<PrivacyApp> LiveMicrophoneApps()
    {
        lock (micMonitorLock)
        {
            if (micMonitorStarted) return new List<PrivacyApp>(micSessionApps);
        }
        return EnumerateMicrophoneApps();
    }

    static List<PrivacyApp> EnumerateMicrophoneApps()
    {
        var list = new List<PrivacyApp>();
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
                            var app = ProcessApp(pid);
                            if (app != null && !list.Any(a => a.Name == app.Name)) list.Add(app);
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

    static void StartMicrophoneMonitor()
    {
        lock (micMonitorLock)
        {
            if (micMonitorStarted) return;
            try
            {
                micDeviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                micDeviceNotification = new MicEndpointNotificationCallback();
                micDeviceEnumerator.RegisterEndpointNotificationCallback(micDeviceNotification);
                RefreshMicrophoneDevicesLocked();
                micMonitorStarted = true;
            }
            catch (Exception ex)
            {
                Store.Log("microphone monitor: " + ex.Message);
                micMonitorStarted = false;
            }
        }
        RebuildMicSessionApps();
    }

    static void RefreshMicrophoneDevices()
    {
        lock (micMonitorLock) RefreshMicrophoneDevicesLocked();
        RebuildMicSessionApps();
    }

    static void RefreshMicrophoneDevicesLocked()
    {
        foreach (var old in micDevices)
        {
            foreach (var session in old.Sessions)
            {
                try { session.Unregister(); } catch (Exception) { }
            }
            try { if (old.Manager != null) Marshal.ReleaseComObject(old.Manager); } catch (Exception) { }
            try { if (old.Device != null) Marshal.ReleaseComObject(old.Device); } catch (Exception) { }
        }
        micDevices.Clear();

        try
        {
            var en = micDeviceEnumerator;
            if (en == null) return;
            IntPtr pc;
            if (en.EnumAudioEndpoints(1, 1, out pc) != 0 || pc == IntPtr.Zero) return;
            var devices = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(pc);
            Marshal.Release(pc);
            uint n;
            devices.GetCount(out n);
            var iid = typeof(IAudioSessionManager2).GUID;
            for (uint i = 0; i < n; i++)
            {
                IMMDevice dev;
                if (devices.Item(i, out dev) != 0 || dev == null) continue;
                object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null)
                {
                    Marshal.ReleaseComObject(dev);
                    continue;
                }
                var manager = (IAudioSessionManager2)o;
                var monitor = new MicDeviceMonitor { Device = dev, Manager = manager };
                monitor.Notification = new MicSessionNotificationCallback(monitor);
                manager.RegisterSessionNotification(monitor.Notification);
                micDevices.Add(monitor);
                IAudioSessionEnumerator sessions;
                if (manager.GetSessionEnumerator(out sessions) == 0)
                {
                    int count;
                    sessions.GetCount(out count);
                    for (int k = 0; k < count; k++)
                    {
                        object so;
                        if (sessions.GetSession(k, out so) != 0 || so == null) continue;
                        var ctl = so as IAudioSessionControl2;
                        if (ctl != null) AddMicSession(monitor, ctl);
                        Marshal.ReleaseComObject(so);
                    }
                    Marshal.ReleaseComObject(sessions);
                }
            }
            Marshal.ReleaseComObject(devices);
        }
        catch (Exception ex) { Store.Log("microphone monitor refresh: " + ex.Message); }
    }

    static void AddMicSession(MicDeviceMonitor monitor, IAudioSessionControl2 control)
    {
        int state = 0;
        uint pid = 0;
        try { control.GetState(out state); } catch (Exception) { }
        try { control.GetProcessId(out pid); } catch (Exception) { }
        PrivacyApp app = null;
        if (pid != 0 && pid != ownPid) app = ProcessApp(pid);

        MicSession session;
        lock (micMonitorLock)
        {
            if (monitor.Sessions.Any(s => s.Control == control)) return;
            session = new MicSession(monitor, control, pid, app);
            monitor.Sessions.Add(session);
        }
        try { session.Register(); } catch (Exception) { }
        if (app != null) session.Active = state == 1;
        RebuildMicSessionApps();
    }

    static void RebuildMicSessionApps()
    {
        lock (micMonitorLock) RebuildMicSessionAppsLocked();
        var h = Changed;
        if (h != null) h();
    }

    static void RebuildMicSessionAppsLocked()
    {
        var apps = micDevices.SelectMany(m => m.Sessions)
            .Where(s => s.Active && s.App != null)
            .GroupBy(s => s.App.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First().App)
            .ToList();
        micSessionApps = apps;
    }

    static void RaiseMicrophoneChanged()
    {
        var h = Changed;
        if (h != null) h();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    class MicDeviceMonitor
    {
        public IMMDevice Device;
        public IAudioSessionManager2 Manager;
        public IAudioSessionNotification Notification;
        public readonly List<MicSession> Sessions = new List<MicSession>();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    class MicSession : IAudioSessionEvents
    {
        public readonly MicDeviceMonitor Device;
        public readonly IAudioSessionControl2 Control;
        public readonly IAudioSessionControl2 ControlEvents;
        public uint Pid;
        public PrivacyApp App;
        public bool Active;
        public Guid EventContext;

        public MicSession(MicDeviceMonitor device, IAudioSessionControl2 control, uint pid, PrivacyApp app)
        {
            Device = device;
            Control = control;
            ControlEvents = control;
            Pid = pid;
            App = app;
        }

        public void Register()
        {
            if (Control == null) return;
            EventContext = Guid.Empty;
            IntPtr sink = Marshal.GetComInterfaceForObject(this, typeof(IAudioSessionEvents));
            try { Control.RegisterAudioSessionNotification(sink); }
            finally { Marshal.Release(sink); }
        }

        public void Unregister()
        {
            if (Control == null) return;
            IntPtr sink = Marshal.GetComInterfaceForObject(this, typeof(IAudioSessionEvents));
            try { Control.UnregisterAudioSessionNotification(sink); }
            catch (Exception) { }
            finally { Marshal.Release(sink); }
        }

        public int OnDisplayNameChanged(string displayName, IntPtr eventContext) { return 0; }
        public int OnIconPathChanged(string iconPath, IntPtr eventContext) { return 0; }
        public int OnSimpleVolumeChanged(float volume, int mute, IntPtr eventContext) { return 0; }
        public int OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channel, IntPtr eventContext) { return 0; }
        public int OnGroupingParamChanged(ref Guid groupingParam, IntPtr eventContext) { return 0; }

        public int OnStateChanged(int state)
        {
            Active = state == 1;
            RebuildMicSessionApps();
            return 0;
        }

        public int OnSessionDisconnected(int disconnectReason)
        {
            try { Unregister(); } catch (Exception) { }
            lock (micMonitorLock)
            {
                if (Device != null && Device.Sessions != null) Device.Sessions.Remove(this);
            }
            RebuildMicSessionApps();
            return 0;
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    class MicSessionNotificationCallback : IAudioSessionNotification
    {
        readonly MicDeviceMonitor monitor;
        public MicSessionNotificationCallback(MicDeviceMonitor monitor) { this.monitor = monitor; }
        public int OnSessionCreated(object newSession)
        {
            try
            {
                var control = newSession as IAudioSessionControl2;
                if (control != null) AddMicSession(monitor, control);
            }
            catch (Exception) { }
            return 0;
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    class MicEndpointNotificationCallback : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, int newState)
        {
            ThreadPool.QueueUserWorkItem(_ => RefreshMicrophoneDevices());
            return 0;
        }

        public int OnDeviceAdded(string pwstrDeviceId)
        {
            ThreadPool.QueueUserWorkItem(_ => RefreshMicrophoneDevices());
            return 0;
        }

        public int OnDeviceRemoved(string deviceId)
        {
            ThreadPool.QueueUserWorkItem(_ => RefreshMicrophoneDevices());
            return 0;
        }

        public int OnDefaultDeviceChanged(int flow, int role, string defaultDeviceId)
        {
            if (flow == 1) ThreadPool.QueueUserWorkItem(_ => RefreshMicrophoneDevices());
            return 0;
        }

        public int OnPropertyValueChanged(string pwstrDeviceId, IntPtr key)
        {
            return 0;
        }
    }

    static void StopMicrophoneMonitor()
    {
        lock (micMonitorLock)
        {
            if (!micMonitorStarted) return;
            try
            {
                if (micDeviceEnumerator != null)
                {
                    try { micDeviceEnumerator.UnregisterEndpointNotificationCallback(micDeviceNotification); } catch (Exception) { }
                    try { Marshal.ReleaseComObject(micDeviceEnumerator); } catch (Exception) { }
                }
            }
            catch (Exception) { }
            micDeviceEnumerator = null;
            micDeviceNotification = null;

            foreach (var monitor in micDevices)
            {
                foreach (var session in monitor.Sessions)
                {
                    try { session.Unregister(); } catch (Exception) { }
                }
                try { if (monitor.Manager != null) Marshal.ReleaseComObject(monitor.Manager); } catch (Exception) { }
                try { if (monitor.Device != null) Marshal.ReleaseComObject(monitor.Device); } catch (Exception) { }
            }
            micDevices.Clear();
            micSessionApps.Clear();
            micMonitorStarted = false;
        }
    }

    static PrivacyApp ProcessApp(uint pid)
    {
        try
        {
            using (var p = System.Diagnostics.Process.GetProcessById((int)pid))
            {
                string name = p.ProcessName;
                if (name.Length == 0) return null;
                string key = name;
                try { string file = p.MainModule.FileName; if (!string.IsNullOrEmpty(file)) key = file; }
                catch (Exception) { }
                return new PrivacyApp(char.ToUpper(name[0]) + name.Substring(1), key);
            }
        }
        catch (Exception) { return null; }
    }

    // Peak level from the default microphone, used to animate the capturing-app icon.
    static readonly object meterLock = new object();
    static IMMDeviceEnumerator meterEnumerator;
    static IMMDevice meterDevice;
    static IAudioMeterInformation meterInfo;

    public static float MicrophonePeak()
    {
        lock (meterLock)
        {
            try
            {
                if (meterInfo == null) InitMeter();
                if (meterInfo == null) return 0;
                float peak;
                if (meterInfo.GetPeakValue(out peak) != 0)
                {
                    ReleaseMeter();
                    return 0;
                }
                return peak;
            }
            catch (Exception)
            {
                ReleaseMeter();
                return 0;
            }
        }
    }

    static void InitMeter()
    {
        meterEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        if (meterEnumerator.GetDefaultAudioEndpoint(1, 0, out meterDevice) != 0 || meterDevice == null)
        {
            ReleaseMeter();
            return;
        }
        var iid = typeof(IAudioMeterInformation).GUID;
        object o;
        if (meterDevice.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null)
        {
            ReleaseMeter();
            return;
        }
        meterInfo = (IAudioMeterInformation)o;
    }

    static void ReleaseMeter()
    {
        if (meterInfo != null) { try { Marshal.ReleaseComObject(meterInfo); } catch (Exception) { } meterInfo = null; }
        if (meterDevice != null) { try { Marshal.ReleaseComObject(meterDevice); } catch (Exception) { } meterDevice = null; }
        if (meterEnumerator != null) { try { Marshal.ReleaseComObject(meterEnumerator); } catch (Exception) { } meterEnumerator = null; }
    }

    internal static void ReleaseMicMeter()
    {
        lock (meterLock) ReleaseMeter();
    }
}
