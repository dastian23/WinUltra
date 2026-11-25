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
    // ---------------------------------------------------------------- pop-ups

    Popup MakePopup(string glyph, Brush color, string text, bool withBar)
    {
        var p = new Popup();
        var g = UI.Columns(UI.Auto, UI.Auto, UI.Star, UI.Auto);
        g.Margin = new Thickness(16, 0, 18, 0);
        p.Glyph = UI.Icon(glyph, 17, color);
        g.Children.Add(UI.At(p.Glyph, 0));
        if (text.Length > 0)
        {
            var label = UI.Text(text, 14, UI.Fg, FontWeights.SemiBold);
            label.Margin = new Thickness(10, 0, 12, 0);
            label.MaxWidth = 200;
            g.Children.Add(UI.At(label, 1));
        }
        if (withBar)
        {
            // The bar fills the space between the icon and the number; the fill is scaled, so it
            // is right however wide that space turns out.
            var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(text.Length > 0 ? 0 : 14, 0, 14, 0) };
            track.Children.Add(new Border { Background = UI.B("#33FFFFFF"), CornerRadius = new CornerRadius(3) });
            p.Fill = new Border { Background = color, CornerRadius = new CornerRadius(3), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(0, 1) };
            track.Children.Add(p.Fill);
            g.Children.Add(UI.At(track, 2));
        }
        p.Value = UI.Text("", 14, color, FontWeights.SemiBold);
        g.Children.Add(UI.At(p.Value, 3));
        p.Root = g;
        return p;
    }

    void OnVolumeChanged(float level, bool muted, bool external)
    {
        if (mode == Mode.Expanded)
        {
            if (built && !volSlider.Dragging) volSlider.Value = level;
            if (built) SetVolumeGlyph(level, muted);
            return;
        }
        if (!external) return;
        if (volumePopupCache == null) volumePopupCache = MakePopup("", UI.Fg, "", true);
        var p = volumePopupCache;
        p.Glyph.Text = muted ? "" : "";
        p.Value.Text = muted ? "Muted" : ((int)Math.Round(level * 100)).ToString();
        p.Fill.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(muted ? 0 : level, 260, Spring.Soft, 0));
        p.Fill.Opacity = muted ? 0.4 : 1;
        ShowPopup(p.Root, PopW, PopH, 1800, false);
    }
}
