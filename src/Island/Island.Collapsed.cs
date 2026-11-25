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
    // The resting state: a compact pill while something is live (music, a timer, charging), otherwise the line.
    void ApplyCollapsed(bool animate)
    {
        if (mode != Mode.Collapsed) return;
        bool line = !HasStatus();
        lineMode = line;
        hitStrip.Visibility = line ? Visibility.Visible : Visibility.Collapsed;
        double w = line ? LineW : PillW, h = line ? LineH : PillH;
        FadeTo(glass, 0, 300);    // the resting pill is solid black, like the iPhone's
        FadeTo(baseLayer, 1, 200);
        if (line)
        {
            HideLayer(collapsedLayer);
            StopBars();
            FadeTo(lineGlow, 0.9, 260);
            FadeTo(rimOverlay, 0, 200);
            FadeTo(aurora, 0, 260);
        }
        else
        {
            FadeTo(lineGlow, 0, 160);
            FadeTo(rimOverlay, 1, 300);
            FadeTo(aurora, 0.55, 400);
            ShowLayer(collapsedLayer, 140);
            if (playing && !timerRunning && !stopwatch.IsRunning && !hidden) StartBars();
        }
        Action done = delegate
        {
            // At rest the window hugs the line or pill, so the music bars redraw a small area.
            if (mode == Mode.Collapsed) Resize(line ? 262 : PillW + 84, line ? 30 : PillH + Gap + 14);
            trimTimer.Stop();
            trimTimer.Start();
        };
        if (!animate) { island.Width = w; island.Height = h; lineGlow.Opacity = line ? 0.9 : 0; done(); return; }
        Resize(Math.Max(Width, w + 30), Math.Max(Height, h + Gap + 12));    // room for the spring's overshoot
        if (Math.Abs(island.ActualWidth - w) > 0.5 || Math.Abs(island.ActualHeight - h) > 0.5) AnimateTo(w, h, false, done);
        else done();
    }

    // ---------------------------------------------------------------- collapsed pill

    void BuildCollapsed()
    {
        var g = UI.Columns(UI.Auto, UI.Star, UI.Auto);
        g.Margin = new Thickness(7, 0, 14, 0);
        var left = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(pillBadge);
        left.Children.Add(pillArt);
        left.Children.Add(pillIcon);
        g.Children.Add(UI.At(left, 0));
        var right = new Grid { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(pillText);
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

    bool HasStatus() { return timerRunning || stopwatch.IsRunning || playing || (onAc && batteryPercent >= 0 && batteryPercent < 100 && charging); }

    bool charging;

    // Decides what the compact pill shows: a running timer, the stopwatch, music, or charging.
    void UpdateCollapsed()
    {
        bool wasLine = lineMode;
        pillArt.Visibility = Visibility.Collapsed;
        pillIcon.Visibility = Visibility.Collapsed;
        pillText.Visibility = Visibility.Collapsed;
        pillBars.Visibility = Visibility.Collapsed;

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
            if (artwork != null) { pillArt.Background = new ImageBrush(artwork) { Stretch = Stretch.UniformToFill }; pillArt.Visibility = Visibility.Visible; }
            else { pillIcon.Text = ""; pillIcon.Foreground = UI.Fg; pillIcon.Visibility = Visibility.Visible; }
            pillBars.Visibility = Visibility.Visible;
        }
        else if (HasStatus())
        {
            pillIcon.Text = ""; pillIcon.Foreground = UI.Green; pillIcon.Visibility = Visibility.Visible;
            pillText.Text = batteryPercent + "%"; pillText.Foreground = UI.Green; pillText.Visibility = Visibility.Visible;
        }

        pillBadge.Visibility = pillIcon.Visibility;
        if (!playing || timerRunning || stopwatch.IsRunning) StopBars();
        if (mode == Mode.Collapsed && (wasLine != !HasStatus() || (playing && !barsRunning && !timerRunning && !stopwatch.IsRunning))) ApplyCollapsed(true);
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
