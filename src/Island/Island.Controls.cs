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
    // ---------------------------------------------------------------- Controls

    Grid BuildControls()
    {
        // iOS Control Center: a 2x2 grid of toggles on the left, two tall sliders on the right.
        var page = UI.Columns(UI.Star, UI.Px(12), UI.Px(72), UI.Px(10), UI.Px(72));
        var grid = UI.Columns(UI.Star, UI.Px(10), UI.Star);
        grid.RowDefinitions.Add(new RowDefinition { Height = UI.Star });
        grid.RowDefinitions.Add(new RowDefinition { Height = UI.Px(10) });
        grid.RowDefinitions.Add(new RowDefinition { Height = UI.Star });
        wifiTile = Tile("\uE701", "Wi-Fi", delegate { ToggleRadio(wifi); });
        btTile = Tile("\uE702", "Bluetooth", delegate { ToggleRadio(bluetooth); });
        nightTile = Tile("\uE708", "Night light", ToggleNightLight);
        muteTile = Tile("\uE74F", "Mute", delegate { if (volume.Available) { volume.Muted = !volume.Muted; SetVolumeGlyph(volume.Level, volume.Muted); } });
        grid.Children.Add(UI.Row(UI.At(wifiTile, 0), 0));
        grid.Children.Add(UI.Row(UI.At(btTile, 2), 0));
        grid.Children.Add(UI.Row(UI.At(nightTile, 0), 2));
        grid.Children.Add(UI.Row(UI.At(muteTile, 2), 2));
        page.Children.Add(UI.At(grid, 0));

        brightSlider = new VerticalBarSlider("\uE706", null);
        brightSlider.Changed += v => { ownBrightnessUntil = DateTime.Now.AddSeconds(1.5); Brightness.SetLater((int)Math.Round(v * 100)); };
        page.Children.Add(UI.At(brightSlider, 2));
        volSlider = new VerticalBarSlider("\uE767", delegate { if (volume.Available) { volume.Muted = !volume.Muted; SetVolumeGlyph(volume.Level, volume.Muted); } });
        volSlider.Changed += v => { if (volume.Available) { volume.Level = (float)v; SetVolumeGlyph((float)v, volume.Muted); } };
        page.Children.Add(UI.At(volSlider, 4));
        return page;
    }

    // A glass tile: a round icon badge that lights up in colour when on, the name, and "On"/"Off".
    Border Tile(string glyph, string label, Action click)
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = UI.Star });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = UI.B("#2EFFFFFF"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = UI.Icon(glyph, 15, expandedFg) };
        g.Children.Add(UI.Row(badge, 0));
        var words = new StackPanel();
        words.Children.Add(UI.Text(label, 12.8, expandedFg, FontWeights.SemiBold));
        var state = UI.Text("Off", 11.2, expandedDim, FontWeights.Normal);
        words.Children.Add(state);
        g.Children.Add(UI.Row(words, 1));
        var b = UI.Button(g, 0, 0, 22, UI.Card, click);
        b.Padding = new Thickness(12, 11, 12, 10);
        b.BorderThickness = new Thickness(1);
        b.BorderBrush = UI.Rim;
        b.Tag = Tuple.Create(badge, state);
        return b;
    }

    void SetTile(Border tile, int state, Brush onColor)
    {
        var parts = (Tuple<Border, TextBlock>)tile.Tag;
        parts.Item1.Background = state == 1 ? onColor : UI.B("#2EFFFFFF");
        parts.Item2.Text = state < 0 ? "Not available" : state == 1 ? "On" : "Off";
        tile.Background = state == 1 ? UI.B("#24FFFFFF") : UI.Card;
        tile.Opacity = state < 0 ? 0.55 : 1;
    }

    void SetVolumeGlyph(float level, bool muted)
    {
        if (muteTile != null) SetTile(muteTile, muted ? 1 : 0, UI.B("#60A5FA"));
        volSlider.Glyph.Text = muted ? "" : level < 0.01 ? "" : level < 0.34 ? "" : level < 0.67 ? "" : "";
    }
}
