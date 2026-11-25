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
    static RadialGradientBrush Glow(Color c)
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, c.R, c.G, c.B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, c.R, c.G, c.B), 0.55));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
        return b;
    }

    void SetTint(Color c)
    {
        tint = c;
        var a = new ColorAnimation(Color.FromArgb(0x50, c.R, c.G, c.B), TimeSpan.FromMilliseconds(600));
        auroraA.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, a);
        auroraA.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Color.FromArgb(0x18, c.R, c.G, c.B), TimeSpan.FromMilliseconds(600)));
        auroraA.GradientStops[2].Color = Color.FromArgb(0, c.R, c.G, c.B);
    }

    DispatcherTimer MakeTimer(int ms, EventHandler tick)
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += tick;
        return t;
    }

    void Later(int ms, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += delegate { t.Stop(); action(); };
        t.Start();
    }

    void Ui(Action a) { Dispatcher.BeginInvoke(a); }

    // Sizes the (mostly see-through) window around the island, keeping it centred.
    void Resize(double w, double h)
    {
        if (Math.Abs(Width - w) > 0.5) { Width = w; Left = (SystemParameters.PrimaryScreenWidth - w) / 2; }
        if (Math.Abs(Height - h) > 0.5) Height = h;
    }

    void Reshape()
    {
        double w = island.ActualWidth, h = island.ActualHeight;
        double r = Math.Min(h / 2, 28);
        island.CornerRadius = new CornerRadius(r);
        clip.Rect = new Rect(0, 0, w, h);
        clip.RadiusX = r;
        clip.RadiusY = r;
        // Keep the glass pinned to the screen while the island grows around it.
        glassBrush.Viewport = new Rect((w - ExpW) / 2, 0, ExpW, ExpH);
        rimOverlay.CornerRadius = new CornerRadius(Math.Max(0, r - 0.75));
        // The camera/mic dot rides just right of the island, like the iPhone's.
        privacyShift.X = w / 2 + 14;
        privacyDot.Margin = new Thickness(0, Gap + h / 2 - 5.5, 0, 0);
    }

    // The solid base hides under the glass, unless frosted glass is switched off.
    double BaseUnderGlass
    {
        get
        {
#if FRIENDS
            return WinUltra.App.Options.FrostedGlass ? 0 : 1;
#else
            return 0;
#endif
        }
    }

    // Takes a fresh blurred copy of the screen where the open panel will be.
    void CaptureBackdrop()
    {
#if FRIENDS
        if (!WinUltra.App.Options.FrostedGlass) { glassBrush.ImageSource = null; return; }
#endif
        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source == null) return;
            double s = source.CompositionTarget.TransformToDevice.M11;
            int x = (int)Math.Round((SystemParameters.PrimaryScreenWidth - ExpW) / 2 * s), y = (int)Math.Round((Top + Gap) * s);
            glassBrush.ImageSource = Backdrop.Capture(x, y, (int)Math.Round(ExpW * s), (int)Math.Round(ExpH * s));
        }
        catch (Exception ex) { Store.Log("backdrop: " + ex.Message); }
    }
}
