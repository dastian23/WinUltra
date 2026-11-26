using System;

namespace WinUltra
{
    // The tray icon and its menu. Lives on the tray thread.
    class Tray : IDisposable
    {
        readonly System.Windows.Forms.NotifyIcon icon = new System.Windows.Forms.NotifyIcon();

        public Tray()
        {
            using (var s = typeof(Tray).Assembly.GetManifestResourceStream("WinUltra.ico"))
                icon.Icon = new System.Drawing.Icon(s, System.Windows.Forms.SystemInformation.SmallIconSize);
            icon.Text = "WinUltra";

#if FRIENDS
            BuildMenu();
            icon.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) App.OpenIsland(); };
            icon.Visible = true;
        }

        void BuildMenu()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            var open = new System.Windows.Forms.ToolStripMenuItem("Open island", null, delegate { App.OpenIsland(); }) { ShortcutKeyDisplayString = "Ctrl+Shift" };
            var clip =new System.Windows.Forms.ToolStripMenuItem("Clipboard history", null, delegate { App.SetClipboardHistory(!App.Options.ClipboardHistory); });
            var notes = new System.Windows.Forms.ToolStripMenuItem("Notification pop-ups", null, delegate { App.SetNotifications(!App.Options.Notifications); });
            var glass = new System.Windows.Forms.ToolStripMenuItem("Frosted glass", null, delegate { App.SetFrostedGlass(!App.Options.FrostedGlass); });
            var caps = new System.Windows.Forms.ToolStripMenuItem("Caps Lock pop-up", null, delegate { App.SetCapsLock(!App.Options.CapsLock); });
            var startup = new System.Windows.Forms.ToolStripMenuItem("Start when I sign in", null, delegate { App.SetStartup(!App.StartsWithWindows); });
            menu.Items.Add(open);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(clip);
            menu.Items.Add(notes);
            menu.Items.Add(glass);
            menu.Items.Add(caps);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(startup);
            menu.Items.Add("Settings...", null, delegate { App.ShowSettings(); });
            menu.Items.Add("Open data folder", null, delegate { App.OpenDataFolder(); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quit WinUltra", null, delegate { App.Quit(); });
            menu.Items.Add("Remove WinUltra...", null, delegate { App.Uninstall(); });
            menu.Opening += delegate
            {
                clip.Checked = App.Options.ClipboardHistory;
                notes.Checked = App.Options.Notifications;
                glass.Checked = App.Options.FrostedGlass;
                caps.Checked = App.Options.CapsLock;
                startup.Checked = App.StartsWithWindows;
            };
            icon.ContextMenuStrip = menu;
#else
            var menu = new System.Windows.Forms.ContextMenuStrip();
            var open = new System.Windows.Forms.ToolStripMenuItem("Open island", null, delegate { App.OpenIsland(); }) { ShortcutKeyDisplayString = "Ctrl+Shift" };
            var startup = new System.Windows.Forms.ToolStripMenuItem("Start with Windows", null, delegate { App.SetStartup(!App.StartsWithWindows); });
            menu.Items.Add(open);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(startup);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quit WinUltra", null, delegate { App.Quit(); });
            menu.Items.Add("Uninstall...", null, delegate { App.Uninstall(); });
            menu.Opening += delegate { startup.Checked = App.StartsWithWindows; };
            icon.ContextMenuStrip = menu;
            icon.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) App.OpenIsland(); };
            icon.Visible = true;
#endif
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
        }
    }
}
