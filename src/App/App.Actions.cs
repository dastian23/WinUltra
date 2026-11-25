using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WinUltra
{
static partial class App
{
#if FRIENDS
        public static bool StartsWithWindows { get { return File.Exists(Shortcut.StartupLink); } }

        public static void SetStartup(bool on)
        {
            if (on) Shortcut.Make(Shortcut.StartupLink, ThisExe); else Shortcut.Remove(Shortcut.StartupLink);
        }

        public static void SetClipboardHistory(bool on) { Options.ClipboardHistory = on; Options.Save(); OnIsland(delegate { island.SetClipboardHistory(on); }); }
        public static void SetNotifications(bool on) { Options.Notifications = on; Options.Save(); OnIsland(delegate { island.SetNotifications(on); }); }
        public static void SetFrostedGlass(bool on) { Options.FrostedGlass = on; Options.Save(); OnIsland(delegate { island.SetFrostedGlass(on); }); }
        public static void SetCapsLock(bool on) { Options.CapsLock = on; Options.Save(); OnIsland(delegate { island.SetCapsLockPopup(on); }); }

        // The settings window again, from the tray. Applies whatever changed.
        public static void ShowSettings()
        {
            var before = new[] { Options.ClipboardHistory, Options.Notifications, Options.FrostedGlass, Options.CapsLock };
            if (!SetupForm.Run(Options, false)) return;
            if (before[0] != Options.ClipboardHistory) SetClipboardHistory(Options.ClipboardHistory);
            if (before[1] != Options.Notifications) SetNotifications(Options.Notifications);
            if (before[2] != Options.FrostedGlass) SetFrostedGlass(Options.FrostedGlass);
            if (before[3] != Options.CapsLock) SetCapsLock(Options.CapsLock);
        }

        public static void OpenDataFolder()
        {
            Directory.CreateDirectory(DataDir);
            Process.Start("explorer.exe", "\"" + DataDir + "\"");
        }

        public static void Uninstall()
        {
            var answer = System.Windows.Forms.MessageBox.Show(
                "Remove WinUltra?\n\nIt will stop starting when you sign in, remove its desktop shortcut, and delete its settings, clipboard pins and shelf list. "
                + "Then you can delete WinUltra.exe yourself.",
                "WinUltra", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question);
            if (answer != System.Windows.Forms.DialogResult.Yes) return;
            SetStartup(false);
            Shortcut.Remove(Shortcut.DesktopLink);
            removeData = true;
            Quit();
        }

        // After the island has closed: delete the default data folder and show the exe so it can be deleted.
        static void FinishUninstall()
        {
            string note = "WinUltra has been removed. You can now delete WinUltra.exe.";
            if (IsSafeDataDir())
            {
                try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); }
                catch (Exception ex) { Store.Log("uninstall data: " + ex.Message); }
            }
            else
            {
                // WINULTRA_DATA points somewhere else: leave that folder alone.
                Store.Log("uninstall: refused to delete " + DataDir);
                note = "WinUltra has been removed. Your custom data folder was left untouched:\n\n" + DataDir;
            }
            System.Windows.Forms.MessageBox.Show(note, "WinUltra",
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            try { Process.Start("explorer.exe", "/select,\"" + ThisExe + "\""); } catch (Exception) { }
        }
#else
        public static bool StartsWithWindows
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("WinUltra") != null; }
                catch (Exception) { return false; }
            }
        }

        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("WinUltra", "\"" + ThisExeOrInstalled() + "\"");
                    else if (k.GetValue("WinUltra") != null) k.DeleteValue("WinUltra");
                }
            }
            catch (Exception ex) { Store.Log("startup: " + ex.Message); }
        }

        static string ThisExeOrInstalled() { return File.Exists(InstalledExe) ? InstalledExe : ThisExe; }
#endif
}
}
