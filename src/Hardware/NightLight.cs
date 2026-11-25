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

// ---------------------------------------------------------------- Night light (undocumented CloudStore blob)

static class NightLight
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    static byte[] Read()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Key))
                return k == null ? null : k.GetValue("Data") as byte[];
        }
        catch (Exception) { return null; }
    }

    // The layout this code understands: "CB" header, a timestamp at byte 10, the inner record's
    // length at byte 18, the inner "CB" header at 19..22, and "02 01" at 23 when night light is on.
    static bool Valid(byte[] d)
    {
        return d != null && d.Length > 26 && d[0] == 0x43 && d[1] == 0x42 && d[15] == 0x2A && d[16] == 0x2B && d[17] == 0x0E
            && d[19] == 0x43 && d[20] == 0x42 && d[21] == 0x01 && d[22] == 0x00 && d.Length == d[18] + 22;
    }

    // 1 on, 0 off, -1 unknown.
    public static int State()
    {
        var d = Read();
        if (!Valid(d)) return -1;
        return d[23] == 0x02 && d[24] == 0x01 ? 1 : 0;
    }

    public static bool Set(bool on)
    {
        var d = Read();
        if (!Valid(d)) return false;
        bool now = d[23] == 0x02 && d[24] == 0x01;
        if (now == on) return true;

        var b = new List<byte>(d);
        if (on) { b.InsertRange(23, new byte[] { 0x02, 0x01 }); b[18] = (byte)(b[18] + 2); }
        else { b.RemoveRange(23, 2); b[18] = (byte)(b[18] - 2); }

        // Bump the timestamp so Windows notices the change.
        int end = 10;
        while (end < 15 && (b[end] & 0x80) != 0) end++;
        int oldLength = end - 10 + 1;
        var stamp = new List<byte>();
        ulong secs = (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        do { byte x = (byte)(secs & 0x7F); secs >>= 7; if (secs != 0) x |= 0x80; stamp.Add(x); } while (secs != 0);
        if (stamp.Count == oldLength) for (int i = 0; i < oldLength; i++) b[10 + i] = stamp[i];

        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Key, true))
            {
                if (k == null) return false;
                k.SetValue("Data", b.ToArray(), RegistryValueKind.Binary);
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    public static bool Restore(byte[] original)
    {
        try
        {
            if (!Valid(original)) return false;
            using (var k = Registry.CurrentUser.OpenSubKey(Key, true))
            {
                if (k == null) return false;
                k.SetValue("Data", original, RegistryValueKind.Binary);
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    public static byte[] Snapshot() { return Read(); }
}
