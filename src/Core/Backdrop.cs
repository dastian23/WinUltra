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

// ---------------------------------------------------------------- Frosted glass backdrop

// Windows' own blur falls back to a flat colour for an always-on-top window that never takes focus,
// so the island makes its own glass: just before it grows, it copies the strip of screen behind it,
// shrinks it, blurs and saturates it, and paints that under a dark tint. It stays in memory only.
static class Backdrop
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    public static BitmapSource Capture(int x, int y, int w, int h)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        BitmapSource src;
        try
        {
            BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020);    // SRCCOPY
            SelectObject(mem, old);
            src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        finally { DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }

        int sw = Math.Max(8, w / 8), sh = Math.Max(8, h / 8);
        var small = new FormatConvertedBitmap(new TransformedBitmap(src, new ScaleTransform((double)sw / w, (double)sh / h)), PixelFormats.Bgra32, null, 0);
        sw = small.PixelWidth;
        sh = small.PixelHeight;
        var px = new byte[sw * sh * 4];
        small.CopyPixels(px, sw * 4, 0);
        for (int pass = 0; pass < 3; pass++) BoxBlur(px, sw, sh, 2);
        for (int i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i + 1], r = px[i + 2], l = 0.299 * r + 0.587 * g + 0.114 * b;
            px[i] = Clamp(l + (b - l) * 1.4);
            px[i + 1] = Clamp(l + (g - l) * 1.4);
            px[i + 2] = Clamp(l + (r - l) * 1.4);
            px[i + 3] = 255;
        }
        var result = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgra32, null, px, sw * 4);
        result.Freeze();
        return result;
    }

    static byte Clamp(double v) { return (byte)(v < 0 ? 0 : v > 255 ? 255 : v); }

    static void BoxBlur(byte[] p, int w, int h, int r)
    {
        var t = new byte[p.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            byte[] from = pass == 0 ? p : t, to = pass == 0 ? t : p;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    for (int c = 0; c < 3; c++)
                    {
                        int sum = 0;
                        for (int k = -r; k <= r; k++)
                        {
                            int xx = pass == 0 ? Math.Min(w - 1, Math.Max(0, x + k)) : x;
                            int yy = pass == 0 ? y : Math.Min(h - 1, Math.Max(0, y + k));
                            sum += from[(yy * w + xx) * 4 + c];
                        }
                        to[(y * w + x) * 4 + c] = (byte)(sum / (2 * r + 1));
                    }
        }
    }
}
