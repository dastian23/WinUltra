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
        mediaApp = UI.Text("", 11.5, UI.Dim, FontWeights.SemiBold);
        mediaTitle = UI.Text("Nothing playing", 18, UI.Fg, FontWeights.SemiBold);
        mediaTitle.Margin = new Thickness(0, 2, 0, 0);
        mediaArtist = UI.Text("", 13.5, UI.Dim, FontWeights.Normal);
        info.Children.Add(mediaApp);
        info.Children.Add(mediaTitle);
        info.Children.Add(mediaArtist);

        progressRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        progressRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        progressRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var track = new Grid { Height = 5 };
        track.Children.Add(new Border { Background = UI.B("#2EFFFFFF"), CornerRadius = new CornerRadius(2.5) });
        progressFill = new Border { Background = UI.Fg, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Children.Add(progressFill);
        progressRow.Children.Add(UI.Row(track, 0));
        var times = UI.Columns(UI.Star, UI.Auto);
        times.Margin = new Thickness(0, 5, 0, 0);
        posText = UI.Text("", 11, UI.Dim, FontWeights.Normal);
        durText = UI.Text("", 11, UI.Dim, FontWeights.Normal);
        times.Children.Add(UI.At(posText, 0));
        times.Children.Add(UI.At(durText, 1));
        progressRow.Children.Add(UI.Row(times, 1));
        info.Children.Add(progressRow);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        controls.Children.Add(UI.Button(UI.Icon("", 20, UI.Fg), 52, 42, 21, null, delegate { MediaCommand(0); }));
        playGlyph = UI.Icon("", 26, UI.Fg);
        controls.Children.Add(UI.Button(playGlyph, 62, 42, 21, null, delegate { MediaCommand(1); }));
        controls.Children.Add(UI.Button(UI.Icon("", 20, UI.Fg), 52, 42, 21, null, delegate { MediaCommand(2); }));
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
        else { artBig.Background = UI.Panel_; artBig.Child = UI.Icon("", 40, UI.Dim); }
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
        progressFill.BeginAnimation(WidthProperty, Anim(w, 900, null, 0));
        posText.Text = Format(pos);
        durText.Text = "-" + Format(len - pos);
    }
}
