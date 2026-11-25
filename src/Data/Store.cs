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

// ---------------------------------------------------------------- Saved data

public class SavedClip { public string Kind { get; set; } public string Text { get; set; } public string File { get; set; } public bool Encrypted { get; set; } public bool Truncated { get; set; } }
public class SavedShelfItem { public string Path { get; set; } public bool Pinned { get; set; } }

class Clip
{
    public bool IsImage;
    public string Text;
    public byte[] Png;
    public ImageSource Thumb;
    public string Hash;
    public bool Pinned;
    public bool Truncated;
    public string File;
}

class ShelfItem
{
    public string Path;
    public bool Pinned;
    public ImageSource Icon;
}

static class Store
{
    public static readonly string Dir = WinUltra.App.DataDir;
    public static string ClipsFile { get { return System.IO.Path.Combine(Dir, "clipboard-pins.json"); } }
    public static string ShelfFile { get { return System.IO.Path.Combine(Dir, "shelf.json"); } }
    public static string ImageDir { get { return System.IO.Path.Combine(Dir, "clip-images"); } }

    // One serializer for both directions, with no practical length limit.
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    public static T Load<T>(string file) where T : class
    {
        try
        {
            if (!File.Exists(file)) return null;
            return Json.Deserialize<T>(File.ReadAllText(file, Encoding.UTF8));
        }
        catch (Exception ex) { Log("load " + System.IO.Path.GetFileName(file) + ": " + ex.Message); return null; }
    }

    // Write to a temporary file, then swap it in, so a crash never leaves no file at all.
    public static bool Save(string file, object value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(value), Encoding.UTF8);
            if (File.Exists(file))
            {
                try { File.Replace(tmp, file, file + ".bak"); }
                catch (Exception) { File.Delete(file); File.Move(tmp, file); }
            }
            else File.Move(tmp, file);
            return true;
        }
        catch (Exception ex) { Log("save " + System.IO.Path.GetFileName(file) + ": " + ex.Message); return false; }
    }

    // Clipboard pins are encrypted with the current Windows user's DPAPI key.
    public static byte[] Seal(byte[] data) { return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser); }
    public static byte[] Open(byte[] data) { return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser); }

    // Append to a log that is rotated at 1 MB; logging must never take the app down.
    public static bool Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string path = System.IO.Path.Combine(Dir, "error.log");
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 1024 * 1024)
                {
                    string old = path + ".1";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
            }
            catch (Exception) { }
            File.AppendAllText(path, DateTime.Now + "  " + text + Environment.NewLine);
            return true;
        }
        catch (Exception) { return false; }
    }
}
