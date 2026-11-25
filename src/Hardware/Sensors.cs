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

// ---------------------------------------------------------------- CPU / GPU usage and GPU temperature

class Sensors : IDisposable
{
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll")] static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out FmtValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, ref uint count, IntPtr buffer);
    [DllImport("nvml.dll")] static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);

    [StructLayout(LayoutKind.Explicit)]
    struct FmtValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct FmtItem { [FieldOffset(0)] public IntPtr Name; [FieldOffset(8)] public uint Status; [FieldOffset(16)] public double Value; }

    const uint PDH_FMT_DOUBLE = 0x200;
    IntPtr query, gpuCounter, cpuCounter, downCounter, upCounter;
    bool ready, nvmlTried, nvmlOk;
    IntPtr nvDevice;
    IntPtr buffer;
    uint bufferSize;
    readonly Dictionary<string, double> engines = new Dictionary<string, double>();

    public void Init()
    {
        if (ready) return;
        if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) return;
        PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out gpuCounter);
        if (PdhAddEnglishCounter(query, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out cpuCounter) != 0)
            PdhAddEnglishCounter(query, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out cpuCounter);
        PdhAddEnglishCounter(query, @"\Network Interface(*)\Bytes Received/sec", IntPtr.Zero, out downCounter);
        PdhAddEnglishCounter(query, @"\Network Interface(*)\Bytes Sent/sec", IntPtr.Zero, out upCounter);
        PdhCollectQueryData(query);
        ready = true;
    }

    // cpu and gpu in percent, temp in degrees C, down and up in bytes a second; -1 means not available.
    public void Sample(out double cpu, out double gpu, out int temp, out double down, out double up)
    {
        cpu = -1; gpu = -1; temp = -1; down = -1; up = -1;
        if (!ready) Init();
        if (ready && PdhCollectQueryData(query) == 0)
        {
            FmtValue v;
            if (cpuCounter != IntPtr.Zero && PdhGetFormattedCounterValue(cpuCounter, PDH_FMT_DOUBLE, IntPtr.Zero, out v) == 0)
                cpu = Math.Max(0, Math.Min(100, v.Value));
            if (gpuCounter != IntPtr.Zero) gpu = ReadGpu();
            if (downCounter != IntPtr.Zero) down = SumAll(downCounter);
            if (upCounter != IntPtr.Zero) up = SumAll(upCounter);
        }
        temp = GpuTemp();
    }

    // Adds up every instance of a counter, skipping virtual network adapters.
    double SumAll(IntPtr counter)
    {
        uint size = 0, count = 0;
        if ((uint)PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero) != 0x800007D2) return -1;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, ref count, buf) != 0) return -1;
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                var item = (FmtItem)Marshal.PtrToStructure(buf + i * Marshal.SizeOf(typeof(FmtItem)), typeof(FmtItem));
                if (item.Status != 0) continue;
                string name = item.Name != IntPtr.Zero ? Marshal.PtrToStringUni(item.Name) : null;
                if (VirtualAdapter(name)) continue;    // VPN, Hyper-V, loopback and similar never count as "the network"
                sum += item.Value;
            }
            return sum;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static bool VirtualAdapter(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string n = name.ToLowerInvariant();
        return n.Contains("loopback") || n.Contains("isatap") || n.Contains("teredo") || n.Contains("vethernet")
            || n.Contains("virtual") || n.Contains("vpn") || n.Contains("tunnel") || n.Contains("hyper-v")
            || n.Contains("pseudo") || n.Contains("bluetooth") || n.Contains("tap-") || n.Contains("tap windows")
            || n.Contains("wireguard") || n.Contains("openvpn") || n.Contains("wintun") || n.Contains("tailscale");
    }

    double ReadGpu()
    {
        uint size = bufferSize, count = 0;
        int r = PdhGetFormattedCounterArray(gpuCounter, PDH_FMT_DOUBLE, ref size, ref count, buffer);
        if ((uint)r == 0x800007D2)    // PDH_MORE_DATA
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            bufferSize = size + 4096;
            buffer = Marshal.AllocHGlobal((int)bufferSize);
            size = bufferSize;
            r = PdhGetFormattedCounterArray(gpuCounter, PDH_FMT_DOUBLE, ref size, ref count, buffer);
        }
        if (r != 0) return -1;

        // Like Task Manager: add up each engine type per adapter, then take the busiest.
        engines.Clear();
        for (int i = 0; i < count; i++)
        {
            var item = (FmtItem)Marshal.PtrToStructure(buffer + i * Marshal.SizeOf(typeof(FmtItem)), typeof(FmtItem));
            if (item.Status != 0 || item.Name == IntPtr.Zero) continue;
            string name = Marshal.PtrToStringUni(item.Name);
            double value = item.Value;
            int luid = name.IndexOf("luid_", StringComparison.Ordinal);
            int phys = name.IndexOf("_phys", StringComparison.Ordinal);
            int type = name.IndexOf("engtype_", StringComparison.Ordinal);
            if (luid < 0 || phys < luid || type < 0) continue;
            string key = name.Substring(luid, phys - luid) + name.Substring(type);
            double sum;
            engines.TryGetValue(key, out sum);
            engines[key] = sum + value;
        }
        double best = 0;
        foreach (var v in engines.Values) if (v > best) best = v;
        return Math.Min(100, best);
    }

    // NVIDIA only: NVML reports the temperature; other vendors return -1.
    int GpuTemp()
    {
        try
        {
            if (!nvmlTried)
            {
                nvmlTried = true;
                nvmlOk = nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out nvDevice) == 0;
            }
            uint t;
            if (nvmlOk && nvmlDeviceGetTemperature(nvDevice, 0, out t) == 0) return (int)t;
        }
        catch (Exception) { nvmlOk = false; }
        return -1;
    }

    public void Dispose()
    {
        if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; bufferSize = 0; }
        if (query != IntPtr.Zero) { try { PdhCloseQuery(query); } catch (Exception) { } query = IntPtr.Zero; ready = false; }
    }
}
