using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinUltra
{
static partial class App
{
        public const string IslandTitle = "WinUltra Island";
#if FRIENDS
        const string MutexName = @"Local\WinUltraFriends", QuitEventName = @"Local\WinUltraFriendsQuit";
#else
        const string MutexName = @"Local\WinUltra", QuitEventName = @"Local\WinUltraQuit";
#endif
#if !FRIENDS
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
#endif
        static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#if FRIENDS
        // WINULTRA_DATA can point WinUltra at another data folder (handy for testing).
        public static readonly string DataDir = Environment.GetEnvironmentVariable("WINULTRA_DATA") ?? Path.Combine(LocalAppData, "WinUltra");
        public static string ExePath { get { return ThisExe; } }
        static bool removeData;
#else
        public static readonly string DataDir = Path.Combine(LocalAppData, "WinUltra");
#endif
        public static readonly string InstallDir = Path.Combine(LocalAppData, "Programs", "WinUltra");
        public static string InstalledExe { get { return Path.Combine(InstallDir, "WinUltra.exe"); } }
        static string ThisExe { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        // Only the real data folder may ever be deleted, never one reached through WINULTRA_DATA.
        static bool IsSafeDataDir()
        {
            try
            {
                string root = Path.GetFullPath(Path.Combine(LocalAppData, "WinUltra")).TrimEnd('\\');
                string dir = Path.GetFullPath(DataDir).TrimEnd('\\');
                return string.Equals(dir, root, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        public static Options Options = new Options();
        static Island island;
        static Tray tray;
        static SynchronizationContext trayThread;

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--selftest") { SelfTest.Run(args[1]); return; }
            if (args.Length == 2 && args[0] == "--open")
            {
                int n;
                if (int.TryParse(args[1], out n)) OpenRunning(n);
                return;
            }

#if FRIENDS
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) { OpenRunning(-1); return; }    // already running: just open the island
                Options = Options.Load();
                bool firstRun = !Options.SetupDone;
                if (firstRun)
                {
                    System.Windows.Forms.Application.EnableVisualStyles();
                    if (!SetupForm.Run(Options, true)) return;    // cancelled: nothing was changed
                }
                Run(firstRun);
            }
            if (removeData) FinishUninstall();
#else
            bool portable = args.Contains("--portable");
            if (!portable && !string.Equals(Path.GetFullPath(ThisExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
            {
                Install();
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) { OpenRunning(-1); return; }    // already running: just open the island
                Run(args.Contains("--welcome"));
            }
#endif
        }

        static void Run(bool welcome)
        {
            Options = Options.Load();

            // The tray icon runs on its own thread with a WinForms message loop;
            // the island runs on this one with WPF.
            var ready = new ManualResetEvent(false);
            var thread = new Thread(TrayThread) { IsBackground = true, Name = "WinUltra tray" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start(ready);
            ready.WaitOne(5000);

            var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) => { Store.Log(e.Exception.ToString()); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Store.Log(e.ExceptionObject.ToString());
            island = new Island();

            // A newer copy being installed asks this one to close.
            var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
            ThreadPool.RegisterWaitForSingleObject(quit, delegate { Quit(); }, null, -1, true);

            if (welcome)
            {
                island.Loaded += delegate
                {
                    var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    t.Tick += delegate { t.Stop(); island.ShowWelcome(); };
                    t.Start();
                };
            }

            app.Run(island);

            // Take the tray icon away before the process ends, or a dead icon stays in the tray.
            if (trayThread != null) trayThread.Send(delegate { if (tray != null) tray.Dispose(); }, null);
        }

        static void TrayThread(object ready)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            trayThread = new System.Windows.Forms.WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(trayThread);
            try { tray = new Tray(); }
            catch (Exception ex) { Store.Log("tray thread: " + ex); }
            ((ManualResetEvent)ready).Set();
            System.Windows.Forms.Application.Run();
        }

        public static void OnIsland(Action a) { if (island != null) island.Dispatcher.BeginInvoke(a); }

        // ---------------------------------------------------------------- tray menu actions

        public static void OpenIsland() { OnIsland(delegate { island.OpenFromTray(); }); }

        public static void Quit() { OnIsland(delegate { island.Close(); }); }

        static void OpenRunning(int tab)
        {
            IntPtr h = Native.FindWindow(null, IslandTitle);
            if (h != IntPtr.Zero) Native.PostMessage(h, Native.RegisterWindowMessage("DynamicIslandOpenTab"), new IntPtr(tab), IntPtr.Zero);
        }
}
}
