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
    public Island()
    {
        Title = WinUltra.App.IslandTitle;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = WinW;
        Height = SmallWinH;
        Top = 0;
        Left = (SystemParameters.PrimaryScreenWidth - WinW) / 2;
        FontFamily = UI.TextFont;
        UseLayoutRounding = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);

        // Every pixel of the island is opaque: this screen blends see-through layered-window
        // pixels in linear light, which washes dark glass out. The glass is a blurred copy of
        // the screen behind (see Backdrop) under a dark tint, with a bright rim on top.
        island.HorizontalAlignment = HorizontalAlignment.Center;
        island.VerticalAlignment = VerticalAlignment.Top;
        island.Margin = new Thickness(0, Gap, 0, 0);
        island.Width = IdlePillW;
        island.Height = PillH;
        // The solid dark base is its own layer, faded out while the glass shows: painted as the
        // island's background it left a dark fringe round the anti-aliased edge of the glass.
        baseLayer.Background = new LinearGradientBrush(Color.FromRgb(0x1C, 0x1C, 0x24), Color.FromRgb(0x07, 0x07, 0x0A), 90);
        baseLayer.IsHitTestVisible = false;
        island.Background = Brushes.Transparent;
        var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x9A, 255, 255, 255), 0));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 255, 255, 255), 0.45));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 255, 255, 255), 0.7));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 255, 255, 255), 1));
        // The bright rim is drawn just inside the shape: on the edge itself the rounded clip
        // cut it in half along the curves, which made the corners look stepped.
        rimOverlay.BorderBrush = rim;
        rimOverlay.BorderThickness = new Thickness(1);
        rimOverlay.Margin = new Thickness(0.75);
        rimOverlay.IsHitTestVisible = false;
        rimOverlay.Opacity = 0;

        glass.Children.Add(new Border { Background = glassBrush });
        var tintBrush = new LinearGradientBrush(Color.FromArgb(0x8C, 0x10, 0x10, 0x16), Color.FromArgb(0xB8, 0x08, 0x08, 0x0C), 90);
        glass.Children.Add(new Border { Background = tintBrush });
        glass.Opacity = 0;
        glass.IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(glass, BitmapScalingMode.Linear);
        island.Clip = clip;
        island.SizeChanged += delegate { Reshape(); };

        auroraA = Glow(tint);
        auroraB = Glow(Color.FromRgb(0xB0, 0x4B, 0xFF));
        aurora.Children.Add(new Border { Width = 360, Height = 240, Background = auroraA, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-120, -110, 0, 0) });
        aurora.Children.Add(new Border { Width = 380, Height = 260, Background = auroraB, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -150, -140) });
        aurora.Opacity = 0;
        aurora.IsHitTestVisible = false;

        // The idle state: a thin line with a soft blue-violet-pink sheen.
        var lineBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        lineBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x6F, 0x8C, 0xFF), 0));
        lineBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xB9, 0x7C, 0xFF), 0.5));
        lineBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x8F, 0xCF), 1));
        lineBrush.Freeze();
        lineGlow.Background = lineBrush;
        lineGlow.Opacity = 0;
        lineGlow.IsHitTestVisible = false;

        var sheen = new Border { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, Height = 60 };
        sheen.Background = new LinearGradientBrush(Color.FromArgb(0x16, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);

        var layers = new Grid();
        layers.Children.Add(baseLayer);
        layers.Children.Add(glass);
        layers.Children.Add(aurora);
        layers.Children.Add(sheen);
        layers.Children.Add(lineGlow);
        layers.Children.Add(collapsedLayer);
        layers.Children.Add(popupLayer);
        layers.Children.Add(expandedLayer);
        layers.Children.Add(rimOverlay);
        island.Child = layers;

        // The line is too thin to aim at, so an invisible strip around it catches the mouse.
        hitStrip.Width = 200;
        hitStrip.Height = 16;
        hitStrip.VerticalAlignment = VerticalAlignment.Top;
        hitStrip.HorizontalAlignment = HorizontalAlignment.Center;
        hitStrip.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        root.Children.Add(hitStrip);
        root.Children.Add(island);
        privacyTransform.Children.Add(privacyPulse);
        privacyTransform.Children.Add(privacyShift);
        privacyDot.RenderTransform = privacyTransform;
        root.Children.Add(privacyDot);
        root.AllowDrop = true;
        Content = root;

        hoverTimer = MakeTimer(120, delegate { hoverTimer.Stop(); Expand(false); });
        leaveTimer = MakeTimer(380, delegate { leaveTimer.Stop(); MaybeCollapse(); });
        popupTimer = MakeTimer(2000, delegate { popupTimer.Stop(); EndPopup(); });
        pollTimer = MakeTimer(1500, delegate { Poll(); });
        tickTimer = MakeTimer(1000, delegate { Tick(); });
        trimTimer = MakeTimer(3000, delegate { trimTimer.Stop(); if (mode != Mode.Expanded) Native.TrimMemory(); });
        clipTimer = MakeTimer(90, delegate { clipTimer.Stop(); ReadClipboard(); });
        noteTimer = MakeTimer(3000, delegate { PollNotifications(); });
        privacyTimer = MakeTimer(300, delegate { privacyTimer.Stop(); UpdatePrivacy(); });
        clockTimer = MakeTimer(1000, delegate { UpdateClock(); });
        clockTimer.Start();
        micLevelTimer = MakeTimer(60, delegate { UpdateMicLevel(); });

        BuildCollapsed();
        foreach (var layer in new[] { collapsedLayer, popupLayer, expandedLayer })
        {
            layer.Visibility = Visibility.Collapsed;
            layer.Opacity = 0;
            layer.RenderTransformOrigin = new Point(0.5, 0);
            layer.RenderTransform = new ScaleTransform(1, 1);
        }

        root.MouseEnter += delegate
        {
            leaveTimer.Stop();
            pinnedOpen = false;
            if (mode != Mode.Expanded) hoverTimer.Start();
        };
        root.MouseLeave += delegate
        {
            hoverTimer.Stop();
            if (mode == Mode.Expanded) leaveTimer.Start();
        };
        root.MouseLeftButtonUp += delegate { if (mode != Mode.Expanded) Expand(false); };
        root.DragEnter += OnDragOver;
        root.DragOver += OnDragOver;
        root.DragLeave += delegate { dragInside = false; if (dragPreviousTab >= 0) { int previous = dragPreviousTab; dragPreviousTab = -1; if (tab == TabShelf) SelectTab(previous); } leaveTimer.Stop(); leaveTimer.Start(); };
        root.Drop += OnDrop;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += delegate { Cleanup(); };
        displayHandler = delegate
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            }));
        };
        SystemEvents.DisplaySettingsChanged += displayHandler;
    }
}
