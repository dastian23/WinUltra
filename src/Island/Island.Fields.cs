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

partial class Island : Window
{
    // Sizes are in WPF units; WPF scales them for the monitor's DPI.
    const double IdlePillW = 240, PillW = 360, PillH = 34, PopW = 340, PopH = 44, NoteW = 392, NoteH = 62;
    const double ExpW = 520, ExpH = 262, WinW = 580, Gap = 5, SmallWinH = 84, BigWinH = ExpH + 40;
    const int TabMedia = 0, TabControls = 1, TabStats = 2, TabClipboard = 3, TabShelf = 4, TabTimer = 5, TabCount = 6;
    // Build.cmd makes three files that differ only in this number: WinUltra.exe (120 fps, the standard
    // build), WinUltra-HighFPS.exe (144 fps, for 144 Hz+ screens) and WinUltra-LowFPS.exe (60 fps, for slower PCs).
#if HIGHFPS
    const int Fps = 144;
#elif LOWFPS
    const int Fps = 60;
#else
    const int Fps = 120;
#endif
    const string ClockGlyph = "";

    enum Mode { Collapsed, Popup, Expanded }
    Mode mode = Mode.Collapsed;
    int animGen;
    IntPtr hwnd;
    uint openMessage;
    uint fmtExclude, fmtCanInclude, fmtViewerIgnore;
    EventHandler displayHandler;
    Microsoft.Win32.PowerModeChangedEventHandler powerHandler;
    Windows.Foundation.TypedEventHandler<Windows.Devices.Power.Battery, object> batteryHandler;

    readonly Grid root = new Grid();
    readonly Border island = new Border();
    readonly Border hitStrip = new Border();
    readonly Border lineGlow = new Border();
    readonly Grid aurora = new Grid();
    readonly Border rimOverlay = new Border();
    readonly Border baseLayer = new Border();
    readonly Border pillBadge = UI.GlassBadge(24, 12);
    readonly Grid glass = new Grid();
    readonly ImageBrush glassBrush = new ImageBrush { Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute };
    readonly RadialGradientBrush auroraA, auroraB;
    readonly RectangleGeometry clip = new RectangleGeometry();
    readonly Grid collapsedLayer = new Grid(), popupLayer = new Grid(), expandedLayer = new Grid();
    bool built;
    DispatcherTimer hoverTimer, leaveTimer, popupTimer, pollTimer, tickTimer, trimTimer, clipTimer, noteTimer, clockTimer, micLevelTimer;
    int hoveredTab = -1, lastSelectedTab = -1, clockMinute = -1;
    System.Threading.Timer sensorTimer;
    bool pinnedOpen, dragInside, draggingOut, hidden, lastPillActive;
    DateTime pinnedAt, forceShowUntil, ignoreClipUntil;
    int clipAttempts;

    // collapsed status
    readonly Border pillArt = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6) };
    readonly Grid pillLeft = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock pillIcon = UI.Icon("", 12.8, UI.Fg);
    readonly TextBlock pillText = UI.Text("", 14, UI.Fg, FontWeights.SemiBold);
    readonly StackPanel pillBars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel pillMicBars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    readonly float[] micHistory = new float[5];
    float micLevel;
    bool barsRunning;

    // tabs
    readonly Border[] tabButtons = new Border[TabCount];
    readonly TextBlock[] tabLabels = new TextBlock[TabCount];
    readonly Grid[] pages = new Grid[TabCount];
    int tab;
    TextBlock batteryLabel;

    // media page
    Border artBig;
    TextBlock mediaApp, mediaTitle, mediaArtist, playGlyph, posText, durText;
    Grid progressRow;
    Border progressFill;

    // controls page
    Border wifiTile, btTile, nightTile, muteTile;
    VerticalBarSlider brightSlider, volSlider;

    // stats page
    Graph cpuGraph, gpuGraph, netGraph;
    TextBlock netDown, netUp;
    TextBlock cpuValue, gpuValue, battBig, battStatus;
    Border battFill;

    // media state
    MediaManager mediaManager;
    MediaSession session;
    bool playing;
    string mediaKey, title, artist, appName;
    string mediaAppId;
    ImageSource artwork, mediaAppIcon;
    Color tint = Color.FromRgb(0x4A, 0x6C, 0xFF);
    int mediaVersion;
    Windows.Foundation.TypedEventHandler<MediaSession, Windows.Media.Control.MediaPropertiesChangedEventArgs> propsHandler;
    Windows.Foundation.TypedEventHandler<MediaSession, Windows.Media.Control.PlaybackInfoChangedEventArgs> playbackHandler;
    Windows.Foundation.TypedEventHandler<MediaManager, Windows.Media.Control.CurrentSessionChangedEventArgs> mediaCurrentSessionChangedHandler;
    Windows.Foundation.TypedEventHandler<MediaManager, Windows.Media.Control.SessionsChangedEventArgs> mediaSessionsChangedHandler;

    // controls
    readonly Volume volume = new Volume();

    // camera and microphone dot
    readonly PrivacyWatch privacy = new PrivacyWatch();
    readonly System.Windows.Shapes.Ellipse privacyDot = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed, RenderTransformOrigin = new Point(0.5, 0.5) };
    readonly TranslateTransform privacyShift = new TranslateTransform();
    readonly ScaleTransform privacyPulse = new ScaleTransform(1, 1);
    readonly TransformGroup privacyTransform = new TransformGroup();
    bool privacyPulsing;
    List<string> camApps = new List<string>(), micApps = new List<string>();
    string micAppIconKey;
    ImageSource micAppIcon;
    TextBlock privacyLabel;
    DispatcherTimer privacyTimer;
    bool privacyBusy;
    Action privacyChangedHandler;
    Action<float, bool, bool> volumeChangedHandler;
    Action volumeDefaultDeviceChangedHandler;

    // Bluetooth connect pop-ups
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    readonly List<Windows.Devices.Enumeration.DeviceWatcher> btWatchers = new List<Windows.Devices.Enumeration.DeviceWatcher>();
    readonly Dictionary<string, string> btNames = new Dictionary<string, string>();
    int btCompleted;
    string lastBtName;
    DateTime lastBtAt;

    // brightness keys and Caps Lock
    ManagementEventWatcher brightWatcher;
    DateTime ownBrightnessUntil;
    Popup brightPopupCache;
#if !FRIENDS
    readonly IntPtr rawBuffer = Marshal.AllocHGlobal(64);
#endif

    // clipboard picker hotkey
    bool pickerMode;
#if !FRIENDS
    IntPtr pasteTarget;
#endif
    string pickerKeys = "";

    // focus timer, radios and sensors
    bool focusMode;
    Radio wifi, bluetooth;
    readonly Sensors sensors = new Sensors();
    readonly object sensorGate = new object();

    // power
    bool onAc, wasOnAc;
    int batteryPercent = -1, lowWarned = 101;

    // notifications
    Windows.UI.Notifications.Management.UserNotificationListener listener;
    HashSet<uint> seenNotes;
    bool notePolling;

    // clipboard
    readonly List<Clip> clips = new List<Clip>();
    StackPanel clipList;
    TextBlock clipCount;

    // shelf
    readonly List<ShelfItem> shelf = new List<ShelfItem>();
    StackPanel shelfList;
    Point dragStart;
    ShelfItem dragCandidate;
    int dragPreviousTab = -1;

    // timer and stopwatch
    bool timerRunning, timerPaused;
    DateTime timerEnd;
    TimeSpan timerLeft;
    int customMinutes = 15;
    readonly Stopwatch stopwatch = new Stopwatch();
    TextBlock timerText, timerPauseLabel, swText, swStartLabel, customLabel;
    Popup volumePopupCache;

    class Popup { public Grid Root; public Border Fill; public TextBlock Value; public TextBlock Glyph; }
}
