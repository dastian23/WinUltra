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
    // ---------------------------------------------------------------- window setup

    void OnSourceInitialized(object sender, EventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle;
        int ex = Native.GetWindowLong(hwnd, -20);
        Native.SetWindowLong(hwnd, -20, ex | 0x08000000 | 0x80);    // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);
        if (!Native.RegisterHotKey(hwnd, 1, 0x0001 | 0x0002 | 0x4000, 0x20))    // Ctrl+Alt+Space, no repeat
            Store.Log("hotkey: Ctrl+Alt+Space is already taken");
        // The clipboard picker. Not Ctrl+Alt+V, which is Paste Special in Word.
        if (Native.RegisterHotKey(hwnd, 2, 0x0008 | 0x0004 | 0x4000, 0x56)) pickerKeys = "Win+Shift+V";
        else if (Native.RegisterHotKey(hwnd, 2, 0x0001 | 0x0002 | 0x0004 | 0x4000, 0x56)) pickerKeys = "Ctrl+Alt+Shift+V";
        else Store.Log("hotkey: no clipboard picker shortcut available");
#if !FRIENDS
        // Keyboard input in the background, only to notice Caps Lock (RIDEV_INPUTSINK).
        var kb = new[] { new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 6, Flags = 0x100, Target = hwnd } };
        Native.RegisterRawInputDevices(kb, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
#endif
        openMessage = Native.RegisterWindowMessage("DynamicIslandOpenTab");
        fmtExclude = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        fmtCanInclude = Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");
        fmtViewerIgnore = Native.RegisterClipboardFormat("Clipboard Viewer Ignore");
#if FRIENDS
        if (WinUltra.App.Options.ClipboardHistory) Native.AddClipboardFormatListener(hwnd);
#else
        Native.AddClipboardFormatListener(hwnd);
#endif
    }

    void OnLoaded(object sender, EventArgs e)
    {
        LoadClips();
        LoadShelf();
        InitMedia();
        InitRadios();
        InitPower();
#if FRIENDS
        if (WinUltra.App.Options.Notifications) InitNotifications();
        if (WinUltra.App.Options.CapsLock) SetCapsLockPopup(true);
#else
        InitNotifications();
#endif
        volumeChangedHandler = (level, muted, external) => Ui(delegate { OnVolumeChanged(level, muted, external); });
        volume.DefaultDeviceChanged += volumeDefaultDeviceChangedHandler = () => Ui(delegate { volume.Init(); PrivacyWatch.ReleaseMicMeter(); });
        volume.Changed += volumeChangedHandler;
        volume.Init();
        pollTimer.Start();
        ApplyCollapsed(false);
        try { privacyChangedHandler = () => Ui(delegate { privacyTimer.Stop(); privacyTimer.Start(); }); PrivacyWatch.Changed += privacyChangedHandler; privacy.Start(); UpdatePrivacy(); }
        catch (Exception ex) { Store.Log("privacy: " + ex.Message); }
        InitBluetoothWatch();
        InitBrightnessWatch();
        Later(8000, delegate { if (mode != Mode.Expanded) Native.TrimMemory(); });
    }

    // ---------------------------------------------------------------- called from the tray menu

    public void OpenFromTray() { OpenPinned(-1); }

#if FRIENDS
    const string PickerHint = " opens this list";

    public void SetClipboardHistory(bool on)
    {
        if (on) Native.AddClipboardFormatListener(hwnd);
        else { Native.RemoveClipboardFormatListener(hwnd); clips.RemoveAll(c => !c.Pinned); RenderClips(); }
    }

    public void SetNotifications(bool on)
    {
        if (!on) { noteTimer.Stop(); return; }
        if (listener == null) InitNotifications(); else noteTimer.Start();
    }

    // Checks whether the Caps Lock light changed, a few times a second, only while this is switched on.
    DispatcherTimer capsTimer;
    bool capsWasOn;

    public void SetCapsLockPopup(bool on)
    {
        if (capsTimer == null)
        {
            capsTimer = MakeTimer(350, delegate
            {
                bool now = (Native.GetKeyState(0x14) & 1) != 0;
                if (now != capsWasOn) { capsWasOn = now; ShowCapsLock(); }
            });
        }
        capsWasOn = (Native.GetKeyState(0x14) & 1) != 0;
        if (on) capsTimer.Start(); else capsTimer.Stop();
    }

    public void SetFrostedGlass(bool on) { if (!on) glassBrush.ImageSource = null; }
#else
    const string PickerHint = " pastes from here";
#endif

    public void ShowWelcome()
    {
        var p = MakePopup("\uE945", UI.Purple, "WinUltra is running", false);
        p.Value.Text = "Hover the line";
        p.Value.Foreground = UI.Dim;
        ShowPopup(p.Root, 380, PopH, 4500, true);
    }

    // Detach every static event and native handle: updates can still be queued when the window closes.
    void Cleanup()
    {
        try
        {
            Native.UnregisterHotKey(hwnd, 1);
            Native.UnregisterHotKey(hwnd, 2);
            if (brightWatcher != null) brightWatcher.Stop();
            foreach (var bw in btWatchers) bw.Stop();
            Native.RemoveClipboardFormatListener(hwnd);
            if (displayHandler != null) SystemEvents.DisplaySettingsChanged -= displayHandler;
            if (powerHandler != null) SystemEvents.PowerModeChanged -= powerHandler;
            if (batteryHandler != null) { try { Windows.Devices.Power.Battery.AggregateBattery.ReportUpdated -= batteryHandler; } catch (Exception) { } }
            foreach (var t in new[] { hoverTimer, leaveTimer, popupTimer, pollTimer, tickTimer, trimTimer, clipTimer, noteTimer, privacyTimer, clockTimer, micLevelTimer })
                if (t != null) t.Stop();
            if (volumeChangedHandler != null) volume.Changed -= volumeChangedHandler;
            if (volumeDefaultDeviceChangedHandler != null) volume.DefaultDeviceChanged -= volumeDefaultDeviceChangedHandler;
            if (privacyChangedHandler != null) PrivacyWatch.Changed -= privacyChangedHandler;
            if (mediaCurrentSessionChangedHandler != null && mediaManager != null) mediaManager.CurrentSessionChanged -= mediaCurrentSessionChangedHandler;
            if (mediaSessionsChangedHandler != null && mediaManager != null) mediaManager.SessionsChanged -= mediaSessionsChangedHandler;
            if (session != null)
            {
                try { session.MediaPropertiesChanged -= propsHandler; } catch (Exception) { }
                try { session.PlaybackInfoChanged -= playbackHandler; } catch (Exception) { }
                session = null;
            }
            if (sensorTimer != null) { sensorTimer.Dispose(); sensorTimer = null; }
            privacy.Dispose();
            lock (sensorGate) sensors.Dispose();
#if !FRIENDS
            if (rawBuffer != IntPtr.Zero) Marshal.FreeHGlobal(rawBuffer);
#endif
        }
        catch (Exception) { }
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x0312 && w.ToInt32() == 1)    // WM_HOTKEY
        {
            if (mode == Mode.Expanded) Collapse(); else OpenPinned(-1);
            handled = true;
        }
        else if (msg == 0x0312 && w.ToInt32() == 2)    // the clipboard picker hotkey
        {
#if !FRIENDS
            pasteTarget = Native.GetForegroundWindow();
#endif
            pickerMode = true;
            OpenPinned(TabClipboard);
            handled = true;
        }
#if !FRIENDS
        else if (msg == 0x00FF)    // WM_INPUT: only Caps Lock matters
        {
            uint size = 64;
            if (Native.GetRawInputData(l, 0x10000003, rawBuffer, ref size, 24) != unchecked((uint)-1) && Marshal.ReadInt32(rawBuffer) == 1)
            {
                int flags = Marshal.ReadInt16(rawBuffer, 26), vk = Marshal.ReadInt16(rawBuffer, 30);
                if (vk == 0x14 && (flags & 1) == 1) Later(40, ShowCapsLock);
            }
        }
#endif
        else if (msg == 0x031D)    // WM_CLIPBOARDUPDATE
        {
            clipAttempts = 0;
            clipTimer.Stop();
            clipTimer.Start();
        }
        else if (openMessage != 0 && msg == (int)openMessage)
        {
            OpenPinned(w.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    void OpenPinned(int tabIndex)
    {
        forceShowUntil = DateTime.Now.AddSeconds(10);
        if (hidden) { hidden = false; Show(); }
        Expand(true);
        if (tabIndex >= 0 && tabIndex < TabCount) SelectTab(tabIndex);
    }
}
