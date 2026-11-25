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

// Media Foundation's sensor activity monitor: Windows calls back whenever an app starts or stops streaming
// from a camera. It only reports activity; it never opens the camera itself.
[ComImport, Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSensorActivityMonitor
{
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
}

[ComImport, Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSensorActivitiesReportCallback
{
    [PreserveSig] int OnActivitiesReport(IMFSensorActivitiesReport report);
}

[ComImport, Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSensorActivitiesReport
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetActivityReport(uint index, out IMFSensorActivityReport report);
    [PreserveSig] int GetActivityReportByDeviceName([MarshalAs(UnmanagedType.LPWStr)] string name, out IMFSensorActivityReport report);
}

[ComImport, Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSensorActivityReport
{
    [PreserveSig] int GetFriendlyName([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, uint size, out uint written);
    [PreserveSig] int GetSymbolicLink([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder link, uint size, out uint written);
    [PreserveSig] int GetProcessCount(out uint count);
    [PreserveSig] int GetProcessActivity(uint index, out IMFSensorProcessActivity activity);
}

[ComImport, Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMFSensorProcessActivity
{
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int GetStreamingState(out int streaming);
    [PreserveSig] int GetStreamingMode(out int mode);
    [PreserveSig] int GetReportTime(out long fileTime);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
class SensorReportCallback : IMFSensorActivitiesReportCallback
{
    readonly Action<IMFSensorActivitiesReport> onReport;
    public SensorReportCallback(Action<IMFSensorActivitiesReport> onReport) { this.onReport = onReport; }
    public int OnActivitiesReport(IMFSensorActivitiesReport report)
    {
        try { onReport(report); } catch (Exception) { }
        return 0;
    }
}
