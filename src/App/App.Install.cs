using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinUltra
{
static partial class App
{
#if !FRIENDS
        public static void Uninstall()
        {
            var answer = System.Windows.Forms.MessageBox.Show(
                "Uninstall WinUltra?\n\nIt will stop starting with Windows, remove its shortcuts, and delete its settings, clipboard pins and shelf list.",
                "WinUltra", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question);
            if (answer != System.Windows.Forms.DialogResult.Yes) return;
            SetStartup(false);
            foreach (var link in new[] { DesktopShortcut, StartMenuShortcut })
                try { if (File.Exists(link)) File.Delete(link); } catch (Exception) { }
            try
            {
                // Remove the files a few seconds after this process has gone.
                var cmd = "/c timeout /t 3 /nobreak >nul & rmdir /s /q \"" + InstallDir + "\" & rmdir /s /q \"" + DataDir + "\"";
                Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch (Exception) { }
            Quit();
        }

        // ---------------------------------------------------------------- install and update

        static void Install()
        {
            try
            {
                bool firstInstall = !File.Exists(InstalledExe);

                // Close a copy that is already running, so its file can be replaced.
                try { using (var ev = EventWaitHandle.OpenExisting(QuitEventName)) ev.Set(); } catch (Exception) { }
                try
                {
                    using (var m = Mutex.OpenExisting(MutexName))
                    {
                        try { if (m.WaitOne(8000)) m.ReleaseMutex(); }
                        catch (AbandonedMutexException) { m.ReleaseMutex(); }
                    }
                }
                catch (Exception) { }

                Directory.CreateDirectory(InstallDir);
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Copy(ThisExe, InstalledExe, true); break; }
                    catch (IOException) { if (attempt >= 15) throw; Thread.Sleep(300); }
                }
                if (firstInstall || StartsWithWindows) SetStartup(true);

                // A desktop shortcut and a Start menu entry, made once: if you delete them they stay deleted.
                var options = Options.Load();
                if (!options.ShortcutsMade)
                {
                    MakeShortcut(DesktopShortcut);
                    MakeShortcut(StartMenuShortcut);
                    options.ShortcutsMade = true;
                    options.Save();
                }
                Process.Start(new ProcessStartInfo(InstalledExe, "--welcome") { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("WinUltra couldn't install itself:\n\n" + ex.Message, "WinUltra",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }

        static string DesktopShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WinUltra.lnk"); } }
        static string StartMenuShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinUltra.lnk"); } }

        // Uses Windows Script Host's shortcut maker, which every Windows 11 PC has.
        static void MakeShortcut(string path)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { InstalledExe });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { InstallDir });
                linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { InstalledExe + ",0" });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Open WinUltra" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                Marshal.FinalReleaseComObject(link);
                Marshal.FinalReleaseComObject(shell);
            }
            catch (Exception ex) { Store.Log("shortcut: " + ex.Message); }
        }
#endif
}
}
