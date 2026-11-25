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

// An iOS-style fat slider: a rounded track whose white fill follows the mouse.
class BarSlider : Grid
{
    readonly Border fill = new Border { Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xD8, 0xDA, 0xE6), 90), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(16) };
    public readonly TextBlock Glyph;
    double value;
    public event Action<double> Changed;

    public BarSlider(string glyph, Action iconClick)
    {
        Height = 42;
        Cursor = Cursors.Hand;
        Children.Add(new Border { Background = UI.Card, BorderBrush = UI.Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16) });
        Children.Add(fill);
        Glyph = UI.Icon(glyph, 16, UI.B("#8E8E93"));
        Glyph.HorizontalAlignment = HorizontalAlignment.Left;
        Glyph.Margin = new Thickness(14, 0, 0, 0);
        Glyph.Background = Brushes.Transparent;
        if (iconClick != null) Glyph.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; iconClick(); };
        Children.Add(Glyph);
        SizeChanged += delegate { Redraw(); };
        MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { CaptureMouse(); FromMouse(e); e.Handled = true; };
        MouseMove += delegate(object s, MouseEventArgs e) { if (IsMouseCaptured) FromMouse(e); };
        MouseLeftButtonUp += delegate { ReleaseMouseCapture(); };
        MouseWheel += delegate(object s, MouseWheelEventArgs e) { Set(value + (e.Delta > 0 ? 0.02 : -0.02)); e.Handled = true; };
    }

    public bool Dragging { get { return IsMouseCaptured; } }
    public double Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); Redraw(); } }

    void FromMouse(MouseEventArgs e) { Set(e.GetPosition(this).X / Math.Max(1, ActualWidth)); }

    void Set(double v)
    {
        Value = v;
        var h = Changed;
        if (h != null) h(value);
    }

    void Redraw() { fill.Width = ActualWidth * value; }
}

// The iOS Control Center slider: a tall frosted pill whose white fill rises from the bottom.
// Dragging follows the finger exactly; changes made elsewhere glide in on a spring.
class VerticalBarSlider : Grid
{
    readonly Border fill = new Border { VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
    readonly Border shape = new Border();
    public readonly TextBlock Glyph;
    readonly Brush glyphLight = UI.B("#E6FFFFFF"), glyphDark = UI.B("#5A5A66");
    readonly RectangleGeometry clipShape = new RectangleGeometry();
    double value;
    public event Action<double> Changed;

    public VerticalBarSlider(string glyph, Action iconClick)
    {
        Cursor = Cursors.Hand;
        var fillBrush = new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE2, 0xE4, 0xEE), 90);
        fillBrush.Freeze();
        fill.Background = fillBrush;
        shape.Background = UI.Card;
        Children.Add(shape);
        Children.Add(fill);
        // Inset so the slider's rounded clip doesn't shave the rim off along the curves.
        Children.Add(new Border { BorderBrush = UI.Specular, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(23.25), Margin = new Thickness(0.75), IsHitTestVisible = false, Opacity = 0.7 });
        Glyph = UI.Icon(glyph, 18, glyphLight);
        Glyph.VerticalAlignment = VerticalAlignment.Bottom;
        Glyph.Margin = new Thickness(0, 0, 0, 16);
        Glyph.Background = Brushes.Transparent;
        if (iconClick != null) Glyph.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; iconClick(); };
        Children.Add(Glyph);
        Clip = clipShape;
        SizeChanged += delegate
        {
            clipShape.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
            clipShape.RadiusX = clipShape.RadiusY = 24;
            fill.BeginAnimation(HeightProperty, null);
            Redraw();
        };
        MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { CaptureMouse(); FromMouse(e); e.Handled = true; };
        MouseMove += delegate(object s, MouseEventArgs e) { if (IsMouseCaptured) FromMouse(e); };
        MouseLeftButtonUp += delegate { ReleaseMouseCapture(); };
        MouseWheel += delegate(object s, MouseWheelEventArgs e) { Set(value + (e.Delta > 0 ? 0.03 : -0.03), true); e.Handled = true; };
    }

    public bool Dragging { get { return IsMouseCaptured; } }

    // Setting Value from outside (a key press, the volume pop-up) animates the fill.
    public double Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); Glide(); } }

    void FromMouse(MouseEventArgs e) { Set(1 - e.GetPosition(this).Y / Math.Max(1, ActualHeight), false); }

    void Set(double v, bool glide)
    {
        value = Math.Max(0, Math.Min(1, v));
        if (glide) Glide(); else { fill.BeginAnimation(HeightProperty, null); Redraw(); }
        var h = Changed;
        if (h != null) h(value);
    }

    void Glide()
    {
        var a = new DoubleAnimation(ActualHeight * value, TimeSpan.FromMilliseconds(420)) { EasingFunction = Spring.Soft };
        Timeline.SetDesiredFrameRate(a, 120);
        fill.BeginAnimation(HeightProperty, a);
        Glyph.Foreground = value > 0.2 ? glyphDark : glyphLight;
    }

    void Redraw()
    {
        fill.Height = ActualHeight * value;
        Glyph.Foreground = value > 0.2 ? glyphDark : glyphLight;
    }
}
