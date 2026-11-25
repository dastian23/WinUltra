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

// ---------------------------------------------------------------- Core Audio (volume)

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl(IntPtr a, uint b, out IntPtr c);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr a, uint b, out IntPtr c);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    [PreserveSig] int RegisterSessionNotification([MarshalAs(UnmanagedType.Interface)] IAudioSessionNotification notification);
    [PreserveSig] int UnregisterSessionNotification([MarshalAs(UnmanagedType.Interface)] IAudioSessionNotification notification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
}

[ComImport, Guid("641DD20B-4D41-49CC-ABA3-174B50038C85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionNotification
{
    [PreserveSig] int OnSessionCreated([MarshalAs(UnmanagedType.IUnknown)] object newSession);
}

[ComImport, Guid("24918ACC-64B3-47C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEvents
{
    [PreserveSig] int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string displayName, IntPtr eventContext);
    [PreserveSig] int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string iconPath, IntPtr eventContext);
    [PreserveSig] int OnSimpleVolumeChanged(float volume, int mute, IntPtr eventContext);
    [PreserveSig] int OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channel, IntPtr eventContext);
    [PreserveSig] int OnGroupingParamChanged(ref Guid groupingParam, IntPtr eventContext);
    [PreserveSig] int OnStateChanged(int state);
    [PreserveSig] int OnSessionDisconnected(int disconnectReason);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation
{
    [PreserveSig] int GetPeakValue(out float peak);
    [PreserveSig] int GetMeteringChannelCount(out int count);
    [PreserveSig] int GetChannelsPeakValues(int count, [Out] float[] peaks);
    [PreserveSig] int QueryHardwareSupport(out int mask);
}

[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2
{
    [PreserveSig] int GetState(out int state);    // 1 = active (recording right now)
    [PreserveSig] int GetDisplayName(out IntPtr name);
    [PreserveSig] int SetDisplayName(IntPtr name, IntPtr ctx);
    [PreserveSig] int GetIconPath(out IntPtr path);
    [PreserveSig] int SetIconPath(IntPtr path, IntPtr ctx);
    [PreserveSig] int GetGroupingParam(out Guid g);
    [PreserveSig] int SetGroupingParam(ref Guid g, IntPtr ctx);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
    [PreserveSig] int GetSessionIdentifier(out IntPtr id);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
    [PreserveSig] int GetProcessId(out uint pid);
}
