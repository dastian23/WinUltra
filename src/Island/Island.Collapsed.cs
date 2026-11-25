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
    // The resting state: a compact pill with the clock, or a wider one while something is live.
    void ApplyCollapsed(bool animate)
    {
        if (mode != Mode.Collapsed) return;
        bool active = HasStatus();
        lastPillActive = active;
        hitStrip.Visibility = Visibility.Collapsed;
        double w = active ? PillW : IdlePillW, h = PillH;
        FadeTo(glass, 0, 300);    // the resting pill is solid black, like the iPhone's
        FadeTo(baseLayer, 1, 200);
        FadeTo(lineGlow, 0, 160);
        FadeTo(rimOverlay, 1, 300);
        FadeTo(aurora, active ? 0.55 : 0.35, 400);
        ShowLayer(collapsedLayer, 140);
        if (active && playing && micApps.Count == 0 && !timerRunning && !stopwatch.IsRunning && !hidden) StartBars();
        else StopBars();
        Action done = delegate
        {
            // At rest the window hugs the pill, so the music bars redraw a small area.
            if (mode == Mode.Collapsed) Resize(w + 84, PillH + Gap + 14);
            trimTimer.Stop();
            trimTimer.Start();
        };
        if (!animate) { island.Width = w; island.Height = h; done(); return; }
        Resize(Math.Max(Width, w + 30), Math.Max(Height, h + Gap + 12));    // room for the spring's overshoot
        if (Math.Abs(island.ActualWidth - w) > 0.5 || Math.Abs(island.ActualHeight - h) > 0.5) AnimateTo(w, h, false, done);
        else done();
    }

    // ---------------------------------------------------------------- collapsed pill

    void BuildCollapsed()
    {
        var g = UI.Columns(UI.Auto, UI.Star, UI.Auto);
        g.Margin = new Thickness(10, 0, 12, 0);
        var left = pillLeft;
        left.Children.Add(pillBadge);
        left.Children.Add(pillArt);
        left.Children.Add(pillIcon);
        g.Children.Add(UI.At(left, 0));
        var center = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
        pillText.TextAlignment = TextAlignment.Center;
        pillText.TextTrimming = TextTrimming.CharacterEllipsis;
        pillText.VerticalAlignment = VerticalAlignment.Center;
        pillText.HorizontalAlignment = HorizontalAlignment.Stretch;
        center.Children.Add(pillText);
        g.Children.Add(UI.At(center, 1));
        for (int i = 0; i < micHistory.Length; i++)
        {
            pillMicBars.Children.Add(new Border { Width = 3, Height = 3, Margin = new Thickness(1.5, 0, 1.5, 0), CornerRadius = new CornerRadius(1.5), Background = UI.Green, VerticalAlignment = VerticalAlignment.Center });
        }
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(pillMicBars);
        right.Children.Add(pillBars);
        g.Children.Add(UI.At(right, 2));
        pillText.FontFeatures();
        double[] speeds = { 0.42, 0.33, 0.51, 0.38 };
        foreach (double s in speeds)
        {
            var bar = new Border { Width = 3, Height = 14, Margin = new Thickness(1.5, 0, 1.5, 0), CornerRadius = new CornerRadius(1.5), Background = UI.Green, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 0.4), Tag = s };
            pillBars.Children.Add(bar);
        }
        collapsedLayer.Children.Add(g);
        UpdateCollapsed();
    }

    bool HasStatus()
    {
        return timerRunning || stopwatch.IsRunning || playing || micApps.Count > 0 || camApps.Count > 0
            || (onAc && batteryPercent >= 0 && batteryPercent < 100 && charging);
    }

    bool charging;

    void UpdateClock()
    {
        int minute = DateTime.Now.Minute;
        if (minute == clockMinute) return;
        clockMinute = minute;
        if (mode == Mode.Collapsed && !hidden && !HasStatus()) pillText.Text = DateTime.Now.ToString("HH:mm");
        int msToNextMinute = 60000 - (DateTime.Now.Second * 1000 + DateTime.Now.Millisecond);
        if (msToNextMinute < 100) msToNextMinute += 60000;
        if (clockTimer.Interval.TotalMilliseconds != msToNextMinute)
            clockTimer.Interval = TimeSpan.FromMilliseconds(msToNextMinute);
    }

    void ShowPillIcon(ImageSource icon, ImageSource fallback, string glyph)
    {
        var src = icon ?? fallback;
        if (src != null)
        {
            pillArt.Background = new ImageBrush(src) { Stretch = Stretch.Uniform };
            pillArt.Visibility = Visibility.Visible;
        }
        else
        {
            pillIcon.Text = glyph;
            pillIcon.Foreground = UI.Fg;
            pillIcon.Visibility = Visibility.Visible;
        }
    }

    void ShowMic()
    {
        bool on = micApps.Count > 0;
        pillMicBars.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) ResetMicLevel();
    }

    void UpdateMicLevel()
    {
        if (micApps.Count == 0) { micLevelTimer.Stop(); ResetMicLevel(); return; }
        float peak = PrivacyWatch.MicrophonePeak();
        float target = peak > 0.005f ? Math.Min(1f, (float)Math.Sqrt(peak) * 1.7f) : 0f;
        micLevel = Math.Max(target, micLevel * 0.68f);
        for (int i = micHistory.Length - 1; i > 0; i--) micHistory[i] = micHistory[i - 1];
        micHistory[0] = micLevel;
        for (int i = 0; i < pillMicBars.Children.Count; i++)
            ((Border)pillMicBars.Children[i]).Height = 3 + micHistory[i] * 14;
    }

    void ResetMicLevel()
    {
        micLevel = 0;
        for (int i = 0; i < micHistory.Length; i++) micHistory[i] = 0;
        foreach (Border bar in pillMicBars.Children) bar.Height = 3;
    }

    // Decides what the compact pill shows: a running timer, the stopwatch, music, a microphone, or charging.
    void UpdateCollapsed()
    {
        bool wasActive = lastPillActive;
        pillArt.Visibility = Visibility.Collapsed;
        pillArt.Background = null;
        pillIcon.Visibility = Visibility.Collapsed;
        pillText.Visibility = Visibility.Collapsed;
        pillBars.Visibility = Visibility.Collapsed;
        pillMicBars.Visibility = Visibility.Collapsed;
        pillText.FontSize = 14;
        ShowMic();

        if (timerRunning)
        {
            var tone = focusMode ? UI.Purple : UI.Orange;
            pillIcon.Text = focusMode ? "\uE708" : ClockGlyph; pillIcon.Foreground = tone; pillIcon.Visibility = Visibility.Visible;
            pillText.Text = Format(TimerLeft()); pillText.Foreground = tone; pillText.Visibility = Visibility.Visible;
        }
        else if (stopwatch.IsRunning)
        {
            pillIcon.Text = ClockGlyph; pillIcon.Foreground = UI.Fg; pillIcon.Visibility = Visibility.Visible;
            pillText.Text = Format(stopwatch.Elapsed); pillText.Foreground = UI.Fg; pillText.Visibility = Visibility.Visible;
        }
        else if (playing)
        {
            ShowPillIcon(mediaAppIcon, artwork, "");
            pillText.Text = title ?? appName ?? "";
            pillText.Foreground = UI.Fg;
            pillText.Visibility = Visibility.Visible;
            pillBars.Visibility = micApps.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (micApps.Count > 0)
        {
            ShowPillIcon(micAppIcon, null, "\uE720");
            pillText.Text = micApps[0];
            pillText.Foreground = UI.Green;
            pillText.Visibility = Visibility.Visible;
        }
        else if (camApps.Count > 0)
        {
            pillIcon.Text = "\uE722"; pillIcon.Foreground = UI.Green; pillIcon.Visibility = Visibility.Visible;
            pillText.Text = camApps[0]; pillText.Foreground = UI.Green; pillText.Visibility = Visibility.Visible;
        }
        else if (HasStatus())
        {
            pillIcon.Text = ""; pillIcon.Foreground = UI.Green; pillIcon.Visibility = Visibility.Visible;
            pillText.Text = batteryPercent + "%"; pillText.Foreground = UI.Green; pillText.Visibility = Visibility.Visible;
        }
        else
        {
            pillText.Text = DateTime.Now.ToString("HH:mm");
            pillText.Foreground = UI.Fg;
            pillText.Visibility = Visibility.Visible;
            clockMinute = DateTime.Now.Minute;
        }

        pillBadge.Visibility = pillIcon.Visibility;
        pillLeft.Visibility = pillArt.Visibility == Visibility.Visible || pillIcon.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        if (!playing || timerRunning || stopwatch.IsRunning || micApps.Count > 0) StopBars();
        bool active = HasStatus();
        if (mode == Mode.Collapsed && (active != wasActive || (playing && !barsRunning && !timerRunning && !stopwatch.IsRunning))) ApplyCollapsed(true);
        lastPillActive = active;
    }

    void StartBars()
    {
        if (barsRunning) return;
        barsRunning = true;
        foreach (Border bar in pillBars.Children)
        {
            var a = new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds((double)bar.Tag)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            Timeline.SetDesiredFrameRate(a, 15);
            ((ScaleTransform)bar.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }

    void StopBars()
    {
        if (!barsRunning) return;
        barsRunning = false;
        foreach (Border bar in pillBars.Children) ((ScaleTransform)bar.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
    }
}
