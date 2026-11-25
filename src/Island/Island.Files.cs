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

partial class Island
{
    // "report.zip" -> "report (2).zip" when the name is taken.
    static string FreeName(string path)
    {
        if (!Exists(path)) return path;
        string dir = System.IO.Path.GetDirectoryName(path), name = System.IO.Path.GetFileNameWithoutExtension(path), ext = System.IO.Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string next = System.IO.Path.Combine(dir, name + " (" + i + ")" + ext);
            if (!Exists(next)) return next;
        }
    }

    void AddToShelfTop(string path)
    {
        if (!shelf.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) shelf.Insert(0, new ShelfItem { Path = path });
        SaveShelf();
        RenderShelf();
    }

    // Refuse work that would tie up a lot of memory or disk for too long.
    const long MaxWorkBytes = 1024L * 1024 * 1024;
    const long MaxShrinkBytes = 100L * 1024 * 1024;

    static long SizeOf(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (!Directory.Exists(path)) return -1;
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                total += new FileInfo(f).Length;
                if (total > MaxWorkBytes) return total;
            }
            return total;
        }
        catch (Exception) { return -1; }
    }

    // Zips a file or folder next to itself, in the background, and puts the zip on the shelf.
    void ZipItem(string path, Border button)
    {
        var t = button.Child as TextBlock;
        if (t != null) t.Foreground = UI.Dim;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string clean = path.TrimEnd('\\');
                long size = SizeOf(clean);
                if (size < 0 || size > MaxWorkBytes)
                {
                    Store.Log("zip: skipped " + clean + " (" + size + " bytes)");
                    Ui(delegate { if (t != null) t.Foreground = UI.Red; });
                    return;
                }
                string target = FreeName(clean + ".zip");
                if (Directory.Exists(clean)) ZipFile.CreateFromDirectory(clean, target, CompressionLevel.Optimal, true);
                else using (var zip = ZipFile.Open(target, ZipArchiveMode.Create)) zip.CreateEntryFromFile(clean, System.IO.Path.GetFileName(clean), CompressionLevel.Optimal);
                Ui(delegate { AddToShelfTop(target); });
            }
            catch (Exception ex) { Store.Log("zip: " + ex.Message); Ui(delegate { if (t != null) { t.Foreground = UI.Red; } }); }
        });
    }

    // Saves a copy no bigger than 1600 px on its longest side ("photo-small.jpg") and puts it on the shelf.
    void ShrinkImage(string path, Border button)
    {
        var t = button.Child as TextBlock;
        if (t != null) t.Foreground = UI.Dim;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                if (new FileInfo(path).Length > MaxShrinkBytes)
                {
                    Store.Log("shrink: skipped " + path + " (too large)");
                    Ui(delegate { if (t != null) t.Foreground = UI.Red; });
                    return;
                }
                var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                double scale = Math.Min(1.0, 1600.0 / Math.Max(frame.PixelWidth, frame.PixelHeight));
                BitmapSource src = scale < 1 ? (BitmapSource)new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
                bool png = System.IO.Path.GetExtension(path).ToLowerInvariant() == ".png";
                BitmapEncoder enc = png ? (BitmapEncoder)new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 85 };
                enc.Frames.Add(BitmapFrame.Create(src));
                string target = FreeName(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileNameWithoutExtension(path) + "-small" + (png ? ".png" : ".jpg")));
                using (var fs = File.Create(target)) enc.Save(fs);
                Ui(delegate { AddToShelfTop(target); });
            }
            catch (Exception ex) { Store.Log("shrink: " + ex.Message); Ui(delegate { if (t != null) { t.Foreground = UI.Red; } }); }
        });
    }

    void DragOut(ShelfItem item, UIElement source)
    {
        if (!Exists(item.Path)) return;
        draggingOut = true;
        var result = DragDropEffects.None;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            // Copy or link only, so dragging out never moves the original file away.
            result = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Link);
        }
        catch (Exception ex) { Store.Log("drag out: " + ex.Message); }
        draggingOut = false;
        if (result != DragDropEffects.None && !item.Pinned)
        {
            shelf.Remove(item);
            SaveShelf();
            RenderShelf();
        }
        leaveTimer.Stop();
        leaveTimer.Start();
    }

    void SaveShelf()
    {
        Store.Save(Store.ShelfFile, shelf.Select(s => new SavedShelfItem { Path = s.Path, Pinned = s.Pinned }).ToList());
    }

    void LoadShelf()
    {
        var list = Store.Load<List<SavedShelfItem>>(Store.ShelfFile);
        if (list == null) return;
        foreach (var s in list) if (!string.IsNullOrEmpty(s.Path)) shelf.Add(new ShelfItem { Path = s.Path, Pinned = s.Pinned });
    }
}
