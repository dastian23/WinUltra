#if FRIENDS
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WinUltra
{
    // Windows' standard shortcut (.lnk) maker.
    static class Shortcut
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr data, int flags);
            void GetIDList(out IntPtr list);
            void SetIDList(IntPtr list);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short key);
            void SetHotkey(short key);
            void GetShowCmd(out int show);
            void SetShowCmd(int show);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int icon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
            void Resolve(IntPtr hwnd, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        public static string StartupLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "WinUltra.lnk"); } }
        public static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WinUltra.lnk"); } }

        public static void Make(string link, string target)
        {
            try
            {
                var shell = (IShellLinkW)new ShellLink();
                shell.SetPath(target);
                shell.SetWorkingDirectory(Path.GetDirectoryName(target));
                shell.SetIconLocation(target, 0);
                shell.SetDescription("WinUltra");
                ((IPersistFile)shell).Save(link, true);
                Marshal.FinalReleaseComObject(shell);
            }
            catch (Exception ex) { Store.Log("shortcut: " + ex.Message); }
        }

        public static void Remove(string link)
        {
            try { if (File.Exists(link)) File.Delete(link); } catch (Exception) { }
        }
    }
}
#endif
