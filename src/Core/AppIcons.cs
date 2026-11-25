using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class AppIcons
{
    const int Size = 32;
    static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> misses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> order = new List<string>();

    // Shell icons are extracted once per AUMID/path; frozen bitmaps can be shared freely.
    public static ImageSource For(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (cache)
        {
            ImageSource hit;
            if (cache.TryGetValue(id, out hit)) return hit;
            if (misses.Contains(id)) return null;
        }
        ImageSource icon = Load(id.Trim());
        lock (cache)
        {
            if (icon != null)
            {
                cache[id] = icon;
                order.Add(id);
                while (order.Count > 48)
                {
                    cache.Remove(order[0]);
                    order.RemoveAt(0);
                }
            }
            else misses.Add(id);
        }
        return icon;
    }

    static ImageSource Load(string id)
    {
        try
        {
            if (id.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return FromShell(id);
            if (File.Exists(id) || Directory.Exists(id)) return FromFile(id);

            // Packaged apps and PWAs resolve through the AppsFolder namespace.
            if (id.IndexOf('!') >= 0 || id.IndexOfAny(new[] { '\\', '/' }) >= 0 || !id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var packaged = FromAppsFolder(id);
                if (packaged != null) return packaged;
            }

            string exe = FindExecutable(id);
            if (exe != null)
            {
                var fileIcon = FromFile(exe);
                if (fileIcon != null) return fileIcon;
            }
            return FromAppsFolder(id);
        }
        catch (Exception) { return null; }
    }

    static ImageSource FromAppsFolder(string id)
    {
        var icon = FromShell(@"shell:AppsFolder\" + id);
        if (icon != null || id.IndexOf('!') >= 0) return icon;
        return FromShell(@"shell:AppsFolder\" + id + "!App");
    }

    static string FindExecutable(string id)
    {
        string name = id;
        int bang = name.LastIndexOf('!');
        if (bang >= 0) name = name.Substring(bang + 1);
        int slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) name = name.Substring(slash + 1);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
        if (name.Length == 0 || name.Equals("App", StringComparison.OrdinalIgnoreCase)) return null;
        if (name.Equals("MSEdge", StringComparison.OrdinalIgnoreCase)) name = "msedge";

        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                string file = p.MainModule.FileName;
                if (!string.IsNullOrEmpty(file) && File.Exists(file)) return file;
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }
        return null;
    }

    static ImageSource FromFile(string path)
    {
        IntPtr icon = Native.LargeIcon(path);
        if (icon == IntPtr.Zero)
        {
            var info = new Native.SHFILEINFO();
            Native.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), 0x100);    // SHGFI_ICON, large
            icon = info.hIcon;
        }
        if (icon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { Native.DestroyIcon(icon); }
    }

    static ImageSource FromShell(string path)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        IShellItemImageFactory factory;
        try { SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory); }
        catch (Exception) { return null; }
        if (factory == null) return null;

        IntPtr hbm = IntPtr.Zero;
        try
        {
            int hr = factory.GetImage(new SIZE(Size, Size), SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK, out hbm);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(Size, Size));
            src.Freeze();
            return src;
        }
        catch (Exception) { return null; }
        finally
        {
            Marshal.ReleaseComObject(factory);
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; public SIZE(int w, int h) { cx = w; cy = h; } }

    [Flags]
    enum SIIGBF { RESIZETOFIT = 0, BIGGERSIZEOK = 1, ICONONLY = 4 }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
}
