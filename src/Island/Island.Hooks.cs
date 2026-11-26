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
    void InstallWinEventHooks()
    {
        try
        {
            winEventProc = OnWinEvent;
            winEventHookForeground = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            winEventHookLocation = Native.SetWinEventHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            keyboardHookProc = OnKeyboardHook;
            keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardHookProc, IntPtr.Zero, 0);
            if ((Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0 && (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) == 0) Ui(delegate { SetSeeThrough(true); });
            EvaluateFullScreen();
        }
        catch (Exception ex) { Store.Log("hooks: " + ex.Message); }
    }

    void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;
        if (eventType == Native.EVENT_OBJECT_LOCATIONCHANGE)
        {
            IntPtr foreground = Native.GetForegroundWindow();
            if (hwnd != foreground) return;
        }
        Ui(EvaluateFullScreen);
    }

    void EvaluateFullScreen()
    {
        bool full = false;
        try
        {
            int state;
            if (Native.SHQueryUserNotificationState(out state) == 0 && (state == Native.QUNS_RUNNING_D3D_FULL_SCREEN || state == Native.QUNS_BUSY)) full = true;

            IntPtr foreground = Native.GetForegroundWindow();
            bool shell = false;
            string cls = null;
            if (!full && foreground != IntPtr.Zero && foreground != hwnd)
            {
                var className = new StringBuilder(64);
                Native.GetClassName(foreground, className, className.Capacity);
                cls = className.ToString();
                shell = cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
                if (!shell)
                {
                    Native.RECT rect;
                    if (Native.GetWindowRect(foreground, out rect))
                    {
                        IntPtr foregroundMonitor = Native.MonitorFromWindow(foreground, Native.MONITOR_DEFAULTTONEAREST);
                        IntPtr thisMonitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
                        if (foregroundMonitor != IntPtr.Zero && foregroundMonitor == thisMonitor)
                        {
                            var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
                            if (Native.GetMonitorInfo(foregroundMonitor, ref info))
                            {
                                full = rect.Left <= info.rcMonitor.Left && rect.Top <= info.rcMonitor.Top &&
                                       rect.Right >= info.rcMonitor.Right && rect.Bottom >= info.rcMonitor.Bottom;
                            }
                        }
                    }
                }
            }

            if (Native.IsZoomed(foreground)) full = false;

            bool windowWasPresent = windowPresent;
            windowPresent = foreground != IntPtr.Zero && foreground != hwnd && !shell;
            if (windowPresent != windowWasPresent && mode == Mode.Collapsed) ApplyCollapsed(true);
        }
        catch (Exception ex) { Store.Log("fullscreen: " + ex.Message); }

        if (full && mode == Mode.Expanded && !pinnedOpen && !draggingOut && !dragInside && !CursorOverIsland()) Collapse();
    }

    void SetSeeThrough(bool on)
    {
        if (on == seeThrough) return;
        seeThrough = on;
        try
        {
            int style = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            if (on) style |= Native.WS_EX_TRANSPARENT;
            else style &= ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, style);
            Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
            FadeTo(island, on ? 0.45 : 1.0, 180);
        }
        catch (Exception ex) { Store.Log("see-through: " + ex.Message); }
    }

    IntPtr OnKeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            if (message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN ||
                message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP)
            {
                var key = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                bool isCtrl = key.vkCode == Native.VK_CONTROL || key.vkCode == Native.VK_LCONTROL || key.vkCode == Native.VK_RCONTROL;
                bool isShift = key.vkCode == Native.VK_SHIFT || key.vkCode == Native.VK_LSHIFT || key.vkCode == Native.VK_RSHIFT;
                bool down = message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN;

                if (isCtrl)
                {
                    ctrlKeyDown = down;
                    if (down && shiftKeyDown && !ctrlShiftArmed && !ctrlShiftUsed) ctrlShiftArmed = true;
                    if (!down && ctrlShiftArmed && !ctrlShiftUsed) Ui(delegate { if (mode == Mode.Expanded) Collapse(); else OpenPinned(-1); });
                    if (!down) { ctrlShiftArmed = false; ctrlShiftUsed = false; }
                }

                if (isShift)
                {
                    if (down)
                    {
                        shiftKeyDown = true;
                        if (ctrlKeyDown && !ctrlShiftArmed && !ctrlShiftUsed) ctrlShiftArmed = true;
                        if (!ctrlKeyDown) Ui(delegate { SetSeeThrough(true); });
                    }
                    else
                    {
                        shiftKeyDown = false;
                        if (ctrlShiftArmed && !ctrlShiftUsed) Ui(delegate { if (mode == Mode.Expanded) Collapse(); else OpenPinned(-1); });
                        ctrlShiftArmed = false;
                        ctrlShiftUsed = false;
                        Ui(delegate { SetSeeThrough(false); });
                    }
                }

                if (!isCtrl && !isShift && down)
                {
                    if (ctrlShiftArmed) ctrlShiftUsed = true;
                }

                if (key.vkCode == Native.VK_ESCAPE && down && (Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) == 0)
                {
                    Ui(delegate { if (mode == Mode.Expanded) Collapse(); });
                }
            }
        }
        return Native.CallNextHookEx(keyboardHook, nCode, wParam, lParam);
    }
}
