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
    // ---------------------------------------------------------------- states and animation

    static DoubleAnimation Anim(double to, double ms, IEasingFunction ease, double delayMs)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delayMs) };
        Timeline.SetDesiredFrameRate(a, Fps);
        return a;
    }

    void AnimateTo(double w, double h, bool bouncy, Action done)
    {
        int gen = ++animGen;
        double ms = bouncy ? 620 : 520;
        IEasingFunction ease = bouncy ? (IEasingFunction)Spring.Bouncy : Spring.Soft;
        var aw = Anim(w, ms, ease, 0);
        var ah = Anim(h, ms, ease, 0);
        ah.Completed += delegate
        {
            if (gen != animGen) return;
            // Hold the final size without keeping the animation clocks alive.
            island.BeginAnimation(WidthProperty, null);
            island.BeginAnimation(HeightProperty, null);
            island.Width = w;
            island.Height = h;
            Reshape();
            if (done != null) done();
        };
        island.BeginAnimation(WidthProperty, aw);
        island.BeginAnimation(HeightProperty, ah);
    }

    // Content fades in while growing from 94% to full size, so it seems to bloom out of the island.
    void ShowLayer(Grid layer, int delayMs)
    {
        layer.Visibility = Visibility.Visible;
        layer.IsHitTestVisible = true;
        layer.BeginAnimation(OpacityProperty, Anim(1, 260, new CubicEase { EasingMode = EasingMode.EaseOut }, delayMs));
        var s = (ScaleTransform)layer.RenderTransform;
        s.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 520, Spring.Soft, delayMs * 0.5));
        s.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 520, Spring.Soft, delayMs * 0.5));
        if (layer.Opacity < 0.05) { s.ScaleX = 0.94; s.ScaleY = 0.94; }
    }

    void HideLayer(Grid layer)
    {
        if (layer.Visibility != Visibility.Visible) return;
        layer.IsHitTestVisible = false;
        var a = Anim(0, 120, new CubicEase { EasingMode = EasingMode.EaseIn }, 0);
        a.Completed += delegate
        {
            if (layer.IsHitTestVisible) return;    // shown again meanwhile
            layer.Visibility = Visibility.Collapsed;
            var s = (ScaleTransform)layer.RenderTransform;
            s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            s.ScaleX = 0.94;
            s.ScaleY = 0.94;
        };
        layer.BeginAnimation(OpacityProperty, a);
    }

    void FadeTo(UIElement e, double opacity, double ms)
    {
        e.BeginAnimation(OpacityProperty, Anim(opacity, ms, new CubicEase { EasingMode = EasingMode.EaseOut }, 0));
    }

    void Expand(bool pinned)
    {
        pinnedOpen = pinned;
        pinnedAt = DateTime.Now;
        if (mode == Mode.Expanded) return;
        if (!built) BuildExpanded();
        if (mode == Mode.Collapsed) CaptureBackdrop();    // from a pop-up, keep the glass it already has
        Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);    // back above any other always-on-top window
        popupTimer.Stop();
        mode = Mode.Expanded;
        Resize(WinW, BigWinH);
        hitStrip.Visibility = Visibility.Collapsed;
        HideLayer(collapsedLayer);
        HideLayer(popupLayer);
        StopBars();
        FadeTo(lineGlow, 0, 160);
        FadeTo(rimOverlay, 1, 300);
        FadeTo(glass, 1, 320);
        FadeTo(expandedFill, 1, 320);
        FadeTo(baseLayer, BaseUnderGlass, 320);
        FadeTo(aurora, 0.25, 500);
        FadeTo(contentTint, 0, 200);
        FadeTo(contentTint, 0, 200);
        ShowLayer(expandedLayer, 110);
        AnimateTo(ExpW, ExpH, true, null);
        StartLive();
    }

    void MaybeCollapse()
    {
        if (mode != Mode.Expanded || draggingOut || dragInside) return;
        if (pinnedOpen) return;
        if (CursorOverIsland()) return;
        Collapse();
    }

    void Collapse()
    {
        if (mode != Mode.Expanded) return;
        mode = Mode.Collapsed;
        pinnedOpen = false;
        pickerMode = false;
        StopLive();
        HideLayer(expandedLayer);
        FadeTo(expandedFill, 0, 200);
        ApplyCollapsed(true);
    }

    bool CursorOverIsland()
    {
        Native.POINT p;
        if (!Native.GetCursorPos(out p) || PresentationSource.FromVisual(island) == null) return false;
        var local = island.PointFromScreen(new Point(p.X, p.Y));
        if (local.X >= 0 && local.Y >= -Gap && local.X <= island.ActualWidth && local.Y <= island.ActualHeight) return true;
        if (hitStrip.Visibility != Visibility.Visible) return false;
        local = hitStrip.PointFromScreen(new Point(p.X, p.Y));
        return local.X >= 0 && local.Y >= 0 && local.X <= hitStrip.ActualWidth && local.Y <= hitStrip.ActualHeight;
    }

    void ShowPopup(UIElement content, double w, double h, int ms, bool force)
    {
        if (mode == Mode.Expanded) return;
        if (popupLayer.Children.Count != 1 || popupLayer.Children[0] != content)
        {
            popupLayer.Children.Clear();
            popupLayer.Children.Add(content);
            if (mode == Mode.Popup) { popupLayer.Opacity = 0; ShowLayer(popupLayer, 60); }
        }
        bool fresh = mode != Mode.Popup;
        if (fresh)
        {
            CaptureBackdrop();
            Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);    // back above any other always-on-top window
        }
        mode = Mode.Popup;
        hitStrip.Visibility = Visibility.Collapsed;
        if (fresh)
        {
            StopBars();
            HideLayer(collapsedLayer);
            FadeTo(expandedFill, 0, 160);
            FadeTo(lineGlow, 0, 140);
            FadeTo(rimOverlay, 1, 260);
            FadeTo(glass, 1, 260);
            FadeTo(baseLayer, BaseUnderGlass, 260);
            FadeTo(aurora, 0.5, 400);
            ShowLayer(popupLayer, 90);
        }
        if (fresh) Resize(WinW, SmallWinH);
        if (Math.Abs(island.Width - w) > 0.5 || Math.Abs(island.Height - h) > 0.5 || fresh) AnimateTo(w, h, true, null);
        popupTimer.Interval = TimeSpan.FromMilliseconds(ms);
        popupTimer.Stop();
        popupTimer.Start();
    }

    void EndPopup()
    {
        if (mode != Mode.Popup) return;
        mode = Mode.Collapsed;
        HideLayer(popupLayer);
        ApplyCollapsed(true);
    }
}
