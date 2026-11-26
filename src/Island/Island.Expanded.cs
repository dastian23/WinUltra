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
    // ---------------------------------------------------------------- expanded panel

    void BuildExpanded()
    {
        built = true;
        expandedLayer.Width = ExpW;
        expandedLayer.Height = ExpH;
        expandedLayer.HorizontalAlignment = HorizontalAlignment.Center;
        expandedLayer.VerticalAlignment = VerticalAlignment.Top;

        var panel = new Grid { Margin = new Thickness(16, 12, 16, 14) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = UI.Star });

        // Tab bar: icons, with the name shown only on the open tab.
        var bar = UI.Columns(UI.Star, UI.Auto);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        string[] names = { "Media", "Controls", "Stats", "Clipboard", "Shelf", "Timer" };
        for (int i = 0; i < TabCount; i++)
        {
            int index = i;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(11, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
            tabIcons[i] = new Image { Width = TabIcons.TabIconSize, Height = TabIcons.TabIconSize, Source = TabIcons.Get((TabIcon)i, false), Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(tabIcons[i], BitmapScalingMode.HighQuality);
            content.Children.Add(tabIcons[i]);
            tabLabels[i] = UI.Text(names[i], 12.5, expandedFg, FontWeights.SemiBold);
            tabLabels[i].Margin = new Thickness(7, 0, 0, 0);
            content.Children.Add(tabLabels[i]);
            tabButtons[i] = UI.Button(content, 0, 28, 14, Brushes.Transparent, delegate { SelectTab(index, true); });
            tabButtons[i].Margin = new Thickness(0, 0, 2, 0);
            tabButtons[i].MouseEnter += delegate { hoveredTab = index; UpdateTabVisuals(); };
            tabButtons[i].MouseLeave += delegate { if (hoveredTab == index) { hoveredTab = -1; UpdateTabVisuals(); } };
            tabs.Children.Add(tabButtons[i]);
        }
        bar.Children.Add(UI.At(tabs, 0));
        privacyLabel = UI.Text("", 12, UI.Orange, FontWeights.SemiBold);
        privacyLabel.Margin = new Thickness(0, 0, 12, 0);
        privacyLabel.HorizontalAlignment = HorizontalAlignment.Right;
        privacyLabel.MaxWidth = 150;
        bar.Children.Add(UI.At(privacyLabel, 0));
        batteryLabel = UI.Text("", 12.5, expandedDim, FontWeights.SemiBold);
        batteryLabel.Margin = new Thickness(0, 0, 4, 0);
        bar.Children.Add(UI.At(batteryLabel, 1));
        panel.Children.Add(UI.Row(bar, 0));

        var host = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        pages[TabMedia] = BuildMedia();
        pages[TabControls] = BuildControls();
        pages[TabStats] = BuildStats();
        pages[TabClipboard] = BuildClipboard();
        pages[TabShelf] = BuildShelf();
        pages[TabTimer] = BuildTimer();
        foreach (var p in pages)
        {
            p.RenderTransform = new TranslateTransform();
            host.Children.Add(p);
        }
        panel.Children.Add(UI.Row(host, 1));
        expandedLayer.Children.Add(panel);
        SelectTab(lastSelectedTab >= 0 ? lastSelectedTab : (playing ? TabMedia : TabControls));
        UpdateMediaUi();
    }

    void SelectTab(int index, bool remember = false)
    {
        if (!built) BuildExpanded();
        int from = tab;
        tab = index;
        if (remember) lastSelectedTab = index;
        UpdateTabVisuals();
        if (from != index)
        {
            // Slide in a little from the side the tab sits on, with a quick fade.
            var page = pages[index];
            page.Opacity = 0;
            page.BeginAnimation(OpacityProperty, Anim(1, 200, new CubicEase { EasingMode = EasingMode.EaseOut }, 0));
            var t = (TranslateTransform)page.RenderTransform;
            t.X = index > from ? 14 : -14;
            t.BeginAnimation(TranslateTransform.XProperty, Anim(0, 460, Spring.Soft, 0));
        }
        if (index == TabClipboard) RenderClips();
        if (index == TabShelf) RenderShelf();
        if (index == TabTimer) RenderTimer();
        if (index == TabMedia) UpdateProgress();
        UpdateSensors();
        UpdateTickRate();
    }

    // Hover only highlights a tab; the active page changes only on an explicit click.
    void UpdateTabVisuals()
    {
        for (int i = 0; i < TabCount; i++)
        {
            bool selected = i == tab;
            tabButtons[i].Background = selected ? UI.B("#403F40")
                : (i == hoveredTab ? UI.Selected : Brushes.Transparent);
            tabButtons[i].BorderBrush = selected ? UI.Specular : null;
            tabButtons[i].BorderThickness = new Thickness(selected ? 1 : 0);
            if (tabIcons[i] != null) tabIcons[i].Source = TabIcons.Get((TabIcon)i, selected);
            tabLabels[i].Foreground = selected ? UI.Fg : expandedFg;
            tabLabels[i].Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            pages[i].Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

}
