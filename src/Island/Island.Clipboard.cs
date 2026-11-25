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
    // ---------------------------------------------------------------- clipboard

    Grid BuildClipboard()
    {
        var page = new Grid();
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = UI.Star });
        var head = UI.Columns(UI.Auto, UI.Star, UI.Auto);
        head.Margin = new Thickness(4, 0, 0, 8);
        head.Children.Add(UI.At(UI.Text("Recently copied", 13, UI.Fg, FontWeights.SemiBold), 0));
        clipCount = UI.Text("", 12, UI.Dim, FontWeights.Normal);
        clipCount.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(UI.At(clipCount, 1));
        var clear = UI.Text("Clear unpinned", 12, UI.Fg, FontWeights.SemiBold);
        clear.Margin = new Thickness(10, 0, 10, 0);
        head.Children.Add(UI.At(UI.Button(clear, 0, 26, 13, UI.Card, delegate { clips.RemoveAll(c => !c.Pinned); RenderClips(); }), 2));
        page.Children.Add(UI.Row(head, 0));
        clipList = new StackPanel();
        page.Children.Add(UI.Row(new ScrollViewer { Content = clipList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, PanningMode = PanningMode.VerticalOnly }, 1));
        return page;
    }

    uint ReadDword(uint format)
    {
        if (!Native.OpenClipboard(hwnd)) throw new IOException("clipboard busy");
        try
        {
            IntPtr h = Native.GetClipboardData(format);
            if (h == IntPtr.Zero) return 1;
            IntPtr p = Native.GlobalLock(h);
            if (p == IntPtr.Zero) return 1;
            uint v = (uint)Marshal.ReadInt32(p);
            Native.GlobalUnlock(h);
            return v;
        }
        finally { Native.CloseClipboard(); }
    }

    void ReadClipboard()
    {
        if (DateTime.Now < ignoreClipUntil) return;
        try
        {
            // Password managers mark their copies with these formats; Windows' own history skips them too.
            if (Native.IsClipboardFormatAvailable(fmtExclude) || Native.IsClipboardFormatAvailable(fmtViewerIgnore)) return;
            if (Native.IsClipboardFormatAvailable(fmtCanInclude) && ReadDword(fmtCanInclude) == 0) return;

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    bool truncated = text.Length > 200000;
                    if (truncated) text = text.Substring(0, 200000);
                    AddTextClip(text, truncated);
                }
            }
            else if (Clipboard.ContainsImage())
            {
                byte[] png = null;
                var raw = Clipboard.ContainsData("PNG") ? Clipboard.GetData("PNG") as MemoryStream : null;
                if (raw != null)
                {
                    if (raw.Length > MaxImageBytes) return;
                    png = raw.ToArray();
                }
                else
                {
                    var img = Clipboard.GetImage();
                    if (img == null || (long)img.PixelWidth * img.PixelHeight > 40000000) return;
                    var opaque = new FormatConvertedBitmap(img, PixelFormats.Bgr32, null, 0);    // clipboard DIBs often carry a blank alpha channel
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(opaque));
                    using (var ms = new MemoryStream()) { enc.Save(ms); png = ms.ToArray(); }
                }
                if (png == null || png.Length > MaxImageBytes) return;
                AddImage(png);
            }
        }
        catch (Exception)
        {
            if (++clipAttempts <= 3) Later(150, ReadClipboard);
        }
    }

    // Images above this are skipped: one huge copy could exhaust the island's memory.
    const int MaxImageBytes = 25 * 1024 * 1024;

    void AddTextClip(string text, bool truncated)
    {
        var existing = clips.FirstOrDefault(c => !c.IsImage && c.Text == text);
        if (existing != null) clips.Remove(existing);
        var clip = existing ?? new Clip { Text = text };
        clip.Truncated = truncated;
        clips.Insert(0, clip);
        TrimClips();
    }

    void AddImage(byte[] png)
    {
        if (png == null || png.Length > MaxImageBytes) return;
        string hash = HashOf(png);
        var existing = clips.FirstOrDefault(c => c.IsImage && c.Hash == hash);
        if (existing == null)
            foreach (var c in clips) if (c.IsImage && c.Hash == null) LoadImage(c);    // lazy pinned images
        existing = clips.FirstOrDefault(c => c.IsImage && c.Hash == hash);
        if (existing != null) clips.Remove(existing);
        clips.Insert(0, existing ?? new Clip { IsImage = true, Png = png, Hash = hash, Thumb = Thumbnail(png) });
        TrimClips();
    }

    static string HashOf(byte[] data)
    {
        using (var md5 = MD5.Create()) return Convert.ToBase64String(md5.ComputeHash(data));
    }

    static ImageSource Thumbnail(byte[] png)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelHeight = 72;
            bi.StreamSource = new MemoryStream(png);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch (Exception) { return null; }
    }

    void TrimClips()
    {
        while (clips.Count > 30)
        {
            int i = clips.FindLastIndex(c => !c.Pinned);
            if (i < 0) break;
            clips.RemoveAt(i);
        }
        if (mode == Mode.Expanded && tab == TabClipboard) RenderClips();
    }
}
