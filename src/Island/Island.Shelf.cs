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
    // ---------------------------------------------------------------- shelf

    Grid BuildShelf()
    {
        var page = new Grid();
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = UI.Star });
        var head = UI.Columns(UI.Auto, UI.Star);
        head.Margin = new Thickness(4, 0, 0, 8);
        head.Children.Add(UI.At(UI.Text("Shelf", 13, UI.Fg, FontWeights.SemiBold), 0));
        var hint = UI.Text("Drag a file out to use it. Pin files, folders or apps to keep them as launchers.", 12, UI.Dim, FontWeights.Normal);
        hint.Margin = new Thickness(10, 0, 0, 0);
        head.Children.Add(UI.At(hint, 1));
        page.Children.Add(UI.Row(head, 0));
        shelfList = new StackPanel();
        page.Children.Add(UI.Row(new ScrollViewer { Content = shelfList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }, 1));
        return page;
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (draggingOut || !e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Copy;
        dragInside = true;
        leaveTimer.Stop();
        if (mode != Mode.Expanded) Expand(false);
        if (tab != TabShelf) SelectTab(TabShelf);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        dragInside = false;
        if (draggingOut) { e.Effects = DragDropEffects.None; return; }
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths == null) { e.Effects = DragDropEffects.None; return; }
        foreach (var p in paths)
            if (!shelf.Any(s => string.Equals(s.Path, p, StringComparison.OrdinalIgnoreCase)))
                shelf.Insert(0, new ShelfItem { Path = p });
        e.Effects = (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.Copy;
        SaveShelf();
        RenderShelf();
        leaveTimer.Start();
    }

    static bool Exists(string path) { return File.Exists(path) || Directory.Exists(path); }

    static ImageSource FileIcon(string path)
    {
        try
        {
            IntPtr icon = Native.LargeIcon(path);
            if (icon == IntPtr.Zero)
            {
                var info = new Native.SHFILEINFO();
                Native.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), 0x100);    // SHGFI_ICON, large
                icon = info.hIcon;
            }
            if (icon == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            Native.DestroyIcon(icon);
            src.Freeze();
            return src;
        }
        catch (Exception) { return null; }
    }

    void RenderShelf()
    {
        if (!built) return;
        shelfList.Children.Clear();
        if (shelf.Count == 0)
        {
            var drop = new Border
            {
                Height = 150, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 255, 255, 255)), Background = UI.Card,
            };
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(UI.Icon("", 28, UI.Dim));
            var t = UI.Text("Drop files, folders or apps here", 14, UI.Dim, FontWeights.SemiBold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(t);
            drop.Child = s;
            shelfList.Children.Add(drop);
            return;
        }

        // Pinned files, folders and apps sit at the top as one-click launch tiles.
        var pinned = shelf.Where(x => x.Pinned).ToList();
        if (pinned.Count > 0)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            foreach (var it in pinned) wrap.Children.Add(LaunchTile(it));
            shelfList.Children.Add(wrap);
        }
        foreach (var it in shelf.Where(x => !x.Pinned)) shelfList.Children.Add(ShelfRow(it));
    }
}
