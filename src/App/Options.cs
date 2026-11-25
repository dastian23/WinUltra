using System;
using System.IO;

namespace WinUltra
{
    // What the tray menu switches on and off, saved in %LOCALAPPDATA%\WinUltra\settings.txt.
    class Options
    {
        public bool ShortcutsMade;
#if FRIENDS
        public bool ClipboardHistory = true, Notifications = true, FrostedGlass = true, CapsLock = false, SetupDone;
#endif

        static string FilePath { get { return Path.Combine(App.DataDir, "settings.txt"); } }

        public static Options Load()
        {
            var o = new Options();
            try
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    bool on = parts[1].Trim() == "1";
                    if (parts[0] == "shortcuts") o.ShortcutsMade = on;
#if FRIENDS
                    if (parts[0] == "clipboard") o.ClipboardHistory = on;
                    if (parts[0] == "notifications") o.Notifications = on;
                    if (parts[0] == "glass") o.FrostedGlass = on;
                    if (parts[0] == "capslock") o.CapsLock = on;
                    if (parts[0] == "setup") o.SetupDone = on;
#endif
                }
            }
            catch (Exception) { }
            return o;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(App.DataDir);
#if FRIENDS
                File.WriteAllLines(FilePath, new[] { "clipboard=" + (ClipboardHistory ? 1 : 0), "notifications=" + (Notifications ? 1 : 0),
                    "glass=" + (FrostedGlass ? 1 : 0), "capslock=" + (CapsLock ? 1 : 0), "setup=" + (SetupDone ? 1 : 0) });
#else
                File.WriteAllLines(FilePath, new[] { "shortcuts=" + (ShortcutsMade ? 1 : 0) });
#endif
            }
            catch (Exception) { }
        }
    }
}
