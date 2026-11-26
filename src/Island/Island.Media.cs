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
    // ---------------------------------------------------------------- Media

    Grid BuildMedia()
    {
        var page = UI.Columns(UI.Px(150), UI.Px(18), UI.Star);
        artBig = new Border { Width = 150, Height = 150, CornerRadius = new CornerRadius(20), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), BorderBrush = UI.B("#22FFFFFF") };
        RenderOptions.SetBitmapScalingMode(artBig, BitmapScalingMode.HighQuality);
        page.Children.Add(UI.At(artBig, 0));

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        mediaApp = UI.Text("", 11.5, expandedDim, FontWeights.SemiBold);
        mediaTitle = UI.Text("Nothing playing", 18, expandedFg, FontWeights.SemiBold);
        mediaTitle.Margin = new Thickness(0, 2, 0, 0);
        mediaArtist = UI.Text("", 13.5, expandedDim, FontWeights.Normal);
        info.Children.Add(mediaApp);
        info.Children.Add(mediaTitle);
        info.Children.Add(mediaArtist);

        progressRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        progressRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        progressRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        progressTrack = new Grid { Height = 16, Cursor = Cursors.Hand };
        var trackVisual = new Border { Height = 5, VerticalAlignment = VerticalAlignment.Center, Background = UI.B("#2EFFFFFF"), CornerRadius = new CornerRadius(2.5) };
        progressTrack.Children.Add(trackVisual);
        progressFill = new Border { Background = expandedFg, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Height = 5, VerticalAlignment = VerticalAlignment.Center };
        progressTrack.Children.Add(progressFill);
        progressThumb = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = UI.Fg, Stroke = UI.B("#1C1C24"), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, RenderTransform = new TranslateTransform() };
        progressThumbShift = (TranslateTransform)progressThumb.RenderTransform;
        progressTrack.Children.Add(progressThumb);
        progressTrack.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { progressDragging = true; progressTrack.CaptureMouse(); SeekMedia(e.GetPosition(progressTrack).X); e.Handled = true; };
        progressTrack.MouseMove += delegate(object s, MouseEventArgs e) { if (progressDragging) SeekMedia(e.GetPosition(progressTrack).X); };
        progressTrack.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { progressDragging = false; progressTrack.ReleaseMouseCapture(); };
        progressRow.Children.Add(UI.Row(progressTrack, 0));
        var times = UI.Columns(UI.Star, UI.Auto);
        times.Margin = new Thickness(0, 5, 0, 0);
        posText = UI.Text("", 11, expandedDim, FontWeights.Normal);
        durText = UI.Text("", 11, expandedDim, FontWeights.Normal);
        times.Children.Add(UI.At(posText, 0));
        times.Children.Add(UI.At(durText, 1));
        progressRow.Children.Add(UI.Row(times, 1));
        info.Children.Add(progressRow);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        controls.Children.Add(UI.Button(UI.Icon("", 20, expandedFg), 52, 42, 21, null, delegate { MediaCommand(0); }));
        playGlyph = UI.Icon("", 26, expandedFg);
        controls.Children.Add(UI.Button(playGlyph, 62, 42, 21, null, delegate { MediaCommand(1); }));
        controls.Children.Add(UI.Button(UI.Icon("", 20, expandedFg), 52, 42, 21, null, delegate { MediaCommand(2); }));
        info.Children.Add(controls);
        page.Children.Add(UI.At(info, 2));
        return page;
    }

    void UpdateMediaUi()
    {
        if (!built) return;
        mediaApp.Text = appName ?? "";
        mediaTitle.Text = title ?? "Nothing playing";
        mediaArtist.Text = artist ?? "";
        playGlyph.Text = playing ? "" : "";
        if (artwork != null) { artBig.Background = new ImageBrush(artwork) { Stretch = Stretch.UniformToFill }; artBig.Child = null; }
        else if (mediaAppIcon != null) { artBig.Background = new ImageBrush(mediaAppIcon) { Stretch = Stretch.Uniform }; artBig.Child = null; }
        else { artBig.Background = UI.Panel_; artBig.Child = UI.Icon("", 40, expandedDim); }
        UpdateProgress();
    }

    async void SeekMedia(double x)
    {
        var s = session;
        if (s == null || title == null) return;
        try
        {
            var tl = s.GetTimelineProperties();
            var len = tl.EndTime - tl.StartTime;
            if (len.TotalSeconds < 1 || progressTrack == null || progressTrack.ActualWidth < 1) return;
            double ratio = Math.Max(0, Math.Min(1, x / progressTrack.ActualWidth));
            var target = tl.StartTime + TimeSpan.FromSeconds(len.TotalSeconds * ratio);
            await s.TryChangePlaybackPositionAsync(target.Ticks);
        }
        catch (Exception) { }
        UpdateProgress();
    }
    void UpdateProgress()
    {
        if (!built) return;
        var s = session;
        TimeSpan pos = TimeSpan.Zero, len = TimeSpan.Zero;
        try
        {
            if (s != null && title != null)
            {
                var tl = s.GetTimelineProperties();
                len = tl.EndTime - tl.StartTime;
                pos = tl.Position - tl.StartTime;
                if (playing) pos += DateTimeOffset.Now - tl.LastUpdatedTime;
            }
        }
        catch (Exception) { }
        if (len.TotalSeconds < 1) { progressRow.Visibility = Visibility.Collapsed; return; }
        if (pos > len) pos = len;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        progressRow.Visibility = Visibility.Visible;
        double w = Math.Max(0, ((FrameworkElement)progressFill.Parent).ActualWidth) * pos.TotalSeconds / len.TotalSeconds;
        if (Math.Abs(w - lastProgressWidth) > 0.5)
        {
            lastProgressWidth = w;
            progressFill.BeginAnimation(WidthProperty, Anim(w, 120, null, 0));
            if (progressThumbShift != null && progressTrack != null)
                progressThumbShift.BeginAnimation(TranslateTransform.XProperty, Anim(Math.Max(0, Math.Min(progressTrack.ActualWidth - 8, w - 4)), 120, null, 0));
        }
        posText.Text = Format(pos);
        durText.Text = "-" + Format(len - pos);
    }
}
