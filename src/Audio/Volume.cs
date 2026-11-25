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

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float db);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr data);
}

[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key);
}

// Master volume of the default speakers. Changed fires on any thread: (level 0..1, muted, changedByAnotherApp).
class Volume : IAudioEndpointVolumeCallback, IMMNotificationClient
{
    public static Guid Context = Guid.NewGuid();
    IMMDeviceEnumerator enumerator;
    IAudioEndpointVolume endpoint;
    public event Action<float, bool, bool> Changed;
    public event Action DefaultDeviceChanged;

    public bool Init()
    {
        try
        {
            if (enumerator == null)
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                enumerator.RegisterEndpointNotificationCallback(this);
            }
            Release();
            IMMDevice device;
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return false;    // eRender, eMultimedia
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            object o;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return false;                         // CLSCTX_ALL
            endpoint = (IAudioEndpointVolume)o;
            endpoint.RegisterControlChangeNotify(this);
            Marshal.ReleaseComObject(device);
            return true;
        }
        catch (Exception) { endpoint = null; return false; }
    }

    void Release()
    {
        if (endpoint == null) return;
        try { endpoint.UnregisterControlChangeNotify(this); Marshal.ReleaseComObject(endpoint); } catch (Exception) { }
        endpoint = null;
    }

    public bool Available { get { return endpoint != null; } }

    public float Level
    {
        get { float v = 0; if (endpoint != null) endpoint.GetMasterVolumeLevelScalar(out v); return v; }
        set { if (endpoint != null) endpoint.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, value)), ref Context); }
    }

    public bool Muted
    {
        get { bool m = false; if (endpoint != null) endpoint.GetMute(out m); return m; }
        set { if (endpoint != null) endpoint.SetMute(value, ref Context); }
    }

    public int OnNotify(IntPtr data)
    {
        var context = (Guid)Marshal.PtrToStructure(data, typeof(Guid));
        bool muted = Marshal.ReadInt32(data, 16) != 0;
        float level = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, 20)), 0);
        var h = Changed;
        if (h != null) h(level, muted, context != Context);
        return 0;
    }

    public int OnDeviceStateChanged(string id, int state) { return 0; }
    public int OnDeviceAdded(string id) { return 0; }
    public int OnDeviceRemoved(string id) { return 0; }
    public int OnPropertyValueChanged(string id, IntPtr key) { return 0; }
    public int OnDefaultDeviceChanged(int flow, int role, string id)
    {
        if (flow == 0 && role == 1) { var h = DefaultDeviceChanged; if (h != null) h(); }
        return 0;
    }
}
