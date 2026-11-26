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
    // ---------------------------------------------------------------- timer and stopwatch

    Grid BuildTimer()
    {
        var page = UI.Columns(UI.Px(300), UI.Px(10), UI.Star);

        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        timerText = UI.Text("15:00", 34, expandedFg, FontWeights.Light);
        timerText.HorizontalAlignment = HorizontalAlignment.Center;
        timerText.FontFeatures();
        t.Children.Add(timerText);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        foreach (int m in new[] { 1, 5, 10, 25 })
        {
            int minutes = m;
            presets.Children.Add(Pad(UI.Button(Centered(UI.Text(m + " min", 12, expandedFg, FontWeights.SemiBold)), 56, 28, 14, UI.Card, delegate { StartTimer(minutes); })));
        }
        t.Children.Add(presets);
        var custom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        custom.Children.Add(Pad(UI.Button(UI.Icon("", 11, expandedFg), 28, 28, 14, UI.Card, delegate { customMinutes = Math.Max(1, customMinutes - (customMinutes > 10 ? 5 : 1)); RenderTimer(); })));
        customLabel = UI.Text("15 min", 12.5, expandedFg, FontWeights.SemiBold);
        customLabel.Width = 52;
        customLabel.TextAlignment = TextAlignment.Center;
        custom.Children.Add(customLabel);
        custom.Children.Add(Pad(UI.Button(UI.Icon("", 11, expandedFg), 28, 28, 14, UI.Card, delegate { customMinutes = Math.Min(600, customMinutes + (customMinutes >= 10 ? 5 : 1)); RenderTimer(); })));
        custom.Children.Add(Pad(UI.Button(Centered(UI.Text("Start", 12, UI.Fg, FontWeights.SemiBold)), 56, 28, 14, UI.Orange, delegate { StartTimer(customMinutes); })));
        t.Children.Add(custom);
        var tControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        timerPauseLabel = UI.Text("Pause", 12, expandedFg, FontWeights.SemiBold);
        tControls.Children.Add(Pad(UI.Button(Centered(UI.Text("Focus 25", 12, UI.Fg, FontWeights.SemiBold)), 78, 28, 14, UI.Purple, StartFocus)));
        tControls.Children.Add(Pad(UI.Button(Centered(timerPauseLabel), 72, 28, 14, UI.Card, PauseTimer)));
        tControls.Children.Add(Pad(UI.Button(Centered(UI.Text("Cancel", 12, UI.Fg, FontWeights.SemiBold)), 72, 28, 14, UI.Red, CancelTimer)));
        t.Children.Add(tControls);
        page.Children.Add(UI.At(UI.Panel(t, new Thickness(8)), 0));

        var w = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        w.Children.Add(Centered(UI.Text("Stopwatch", 12, expandedDim, FontWeights.SemiBold)));
        swText = UI.Text("0:00.0", 30, expandedFg, FontWeights.Light);
        swText.HorizontalAlignment = HorizontalAlignment.Center;
        swText.FontFeatures();
        w.Children.Add(swText);
        var wControls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        swStartLabel = UI.Text("Start", 12, UI.Fg, FontWeights.SemiBold);
        wControls.Children.Add(Pad(UI.Button(Centered(swStartLabel), 66, 28, 14, UI.Green, delegate
        {
            if (stopwatch.IsRunning) stopwatch.Stop(); else stopwatch.Start();
            RenderTimer();
            UpdateTickRate();
            UpdateCollapsed();
        })));
        wControls.Children.Add(Pad(UI.Button(Centered(UI.Text("Reset", 12, expandedFg, FontWeights.SemiBold)), 66, 28, 14, UI.Card, delegate { stopwatch.Reset(); RenderTimer(); UpdateTickRate(); UpdateCollapsed(); })));
        w.Children.Add(wControls);
        page.Children.Add(UI.At(UI.Panel(w, new Thickness(8)), 2));
        return page;
    }

    static UIElement Centered(TextBlock t) { t.HorizontalAlignment = HorizontalAlignment.Center; return t; }
    static Border Pad(Border b) { b.Margin = new Thickness(3, 0, 3, 0); return b; }

    TimeSpan TimerLeft() { return timerPaused ? timerLeft : timerEnd - DateTime.Now; }

    void StartTimer(int minutes)
    {
        timerRunning = true;
        timerPaused = false;
        timerEnd = DateTime.Now.AddMinutes(minutes);
        RenderTimer();
        UpdateTickRate();
        UpdateCollapsed();
    }

    void PauseTimer()
    {
        if (!timerRunning) return;
        if (timerPaused) { timerEnd = DateTime.Now + timerLeft; timerPaused = false; }
        else { timerLeft = timerEnd - DateTime.Now; timerPaused = true; }
        RenderTimer();
    }

    // A 25-minute focus session: the pill turns purple and the island holds back notification pop-ups.
    void StartFocus()
    {
        StartTimer(25);
        focusMode = true;
        RenderTimer();
        UpdateCollapsed();
    }

    void CancelTimer()
    {
        focusMode = false;
        timerRunning = false;
        timerPaused = false;
        RenderTimer();
        UpdateTickRate();
        UpdateCollapsed();
    }

    void RenderTimer()
    {
        if (!built) return;
        timerText.Text = timerRunning ? Format(TimerLeft()) : Format(TimeSpan.FromMinutes(customMinutes));
        timerText.Foreground = timerRunning ? (focusMode ? UI.Purple : UI.Orange) : expandedFg;
        timerPauseLabel.Text = timerPaused ? "Resume" : "Pause";
        customLabel.Text = customMinutes + " min";
        var e = stopwatch.Elapsed;
        swText.Text = string.Format("{0}:{1:00}.{2}", (int)e.TotalMinutes, e.Seconds, e.Milliseconds / 100);
        swStartLabel.Text = stopwatch.IsRunning ? "Stop" : "Start";
    }

    // The tick only runs while something on screen needs it: a timer, the stopwatch, or the song position.
    void UpdateTickRate()
    {
        bool songVisible = mode == Mode.Expanded && tab == TabMedia && playing;
        bool needed = timerRunning || stopwatch.IsRunning || songVisible;
        if (!needed) { tickTimer.Stop(); return; }
        int ms = mode == Mode.Expanded && tab == TabTimer && stopwatch.IsRunning ? 100 : 1000;
        if (tickTimer.Interval.TotalMilliseconds != ms) tickTimer.Interval = TimeSpan.FromMilliseconds(ms);
        if (!tickTimer.IsEnabled) tickTimer.Start();
    }

    void Tick()
    {
        if (timerRunning && !timerPaused && DateTime.Now >= timerEnd)
        {
            timerRunning = false;
            TimerDone();
            UpdateCollapsed();
        }
        if (mode == Mode.Expanded && tab == TabTimer) RenderTimer();
        if (mode == Mode.Expanded && tab == TabMedia) UpdateProgress();
        if (mode == Mode.Collapsed)
        {
            if (timerRunning) pillText.Text = Format(TimerLeft());
            else if (stopwatch.IsRunning) pillText.Text = Format(stopwatch.Elapsed);
        }
        UpdateTickRate();
    }

    void TimerDone()
    {
        bool wasFocus = focusMode;
        focusMode = false;
        try { new SoundPlayer(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Alarm01.wav")).Play(); }
        catch (Exception) { SystemSounds.Exclamation.Play(); }
        RenderTimer();
        if (mode == Mode.Expanded) { SelectTab(TabTimer); return; }
        var p = wasFocus ? MakePopup("\uE708", UI.Purple, "Focus done", false) : MakePopup(ClockGlyph, UI.Orange, "Timer done", false);
        p.Value.Text = "";
        ShowPopup(p.Root, 240, PopH, 6000, true);
    }
}
