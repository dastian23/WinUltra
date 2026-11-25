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
    Border LaunchTile(ShelfItem item)
    {
        bool there = Exists(item.Path);
        if (item.Icon == null && there) item.Icon = FileIcon(item.Path);
        var g = new Grid { Width = 70 };
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        s.Children.Add(item.Icon != null && there ? (UIElement)new Image { Source = item.Icon, Width = 32, Height = 32 } : UI.Icon("", 22, UI.Dim));
        string name = System.IO.Path.GetFileNameWithoutExtension(item.Path.TrimEnd('\\'));
        if (string.IsNullOrEmpty(name)) name = item.Path;
        var label = UI.Text(name, 11.2, there ? UI.Fg : UI.Dim, FontWeights.Normal);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.MaxWidth = 64;
        label.Margin = new Thickness(0, 5, 0, 0);
        s.Children.Add(label);
        g.Children.Add(s);
        var unpin = UI.Button(UI.Icon("", 10, UI.Fg), 20, 20, 10, UI.B("#66000000"), delegate { item.Pinned = false; SaveShelf(); RenderShelf(); });
        unpin.HorizontalAlignment = HorizontalAlignment.Right;
        unpin.VerticalAlignment = VerticalAlignment.Top;
        unpin.Margin = new Thickness(0, -6, -2, 0);
        unpin.Visibility = Visibility.Hidden;
        unpin.ToolTip = "Unpin";
        g.Children.Add(unpin);
        var tile = new Border { Child = g, Background = UI.Card, BorderBrush = UI.Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(4, 10, 4, 8), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand, ToolTip = item.Path };
        tile.MouseEnter += delegate { unpin.Visibility = Visibility.Visible; };
        tile.MouseLeave += delegate { unpin.Visibility = Visibility.Hidden; };
        HookDragAndOpen(tile, item, there);
        return tile;
    }

    Border ShelfRow(ShelfItem item)
    {
        bool there = Exists(item.Path);
        if (item.Icon == null && there) item.Icon = FileIcon(item.Path);
        var row = UI.Columns(UI.Px(40), UI.Star, UI.Auto, UI.Auto, UI.Auto);
        UIElement icon = item.Icon != null && there ? (UIElement)new Image { Source = item.Icon, Width = 32, Height = 32 } : UI.Icon("", 20, UI.Dim);
        row.Children.Add(UI.At(icon, 0));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
        text.Children.Add(UI.Text(System.IO.Path.GetFileName(item.Path.TrimEnd('\\')), 13, there ? UI.Fg : UI.Dim, FontWeights.SemiBold));
        text.Children.Add(UI.Text(there ? System.IO.Path.GetDirectoryName(item.Path) : "Missing: moved or deleted", 11, there ? UI.Dim : UI.Red, FontWeights.Normal));
        row.Children.Add(UI.At(text, 1));

        // Quick actions appear while the mouse is on the row.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        if (there)
        {
            string path = item.Path;
            actions.Children.Add(ActionButton("", "Copy path", b => { Clipboard.SetText(path); Done(b); }));
            actions.Children.Add(ActionButton("", "Show in folder", b => { try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch (Exception) { } }));
            actions.Children.Add(ActionButton("", "Zip", b => ZipItem(path, b)));
            if (IsImage(path)) actions.Children.Add(ActionButton("", "Make a smaller copy", b => ShrinkImage(path, b)));
        }
        row.Children.Add(UI.At(actions, 2));
        var pin = UI.Button(UI.Icon("", 13, UI.Dim), 30, 30, 15, null, delegate { item.Pinned = true; SaveShelf(); RenderShelf(); });
        pin.ToolTip = "Pin as a launcher";
        row.Children.Add(UI.At(pin, 3));
        row.Children.Add(UI.At(UI.Button(UI.Icon("", 12, UI.Dim), 30, 30, 15, null, delegate { shelf.Remove(item); SaveShelf(); RenderShelf(); }), 4));
        var card = new Border { Child = row, Background = UI.Card, BorderBrush = UI.Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 5, 6, 5), Margin = new Thickness(0, 0, 0, 6), Cursor = there ? Cursors.SizeAll : Cursors.Arrow };
        card.MouseEnter += delegate { actions.Visibility = Visibility.Visible; };
        card.MouseLeave += delegate { actions.Visibility = Visibility.Collapsed; };
        HookDragAndOpen(card, item, there);
        return card;
    }

    // Drag out to use the file; a plain click opens it.
    void HookDragAndOpen(Border card, ShelfItem item, bool there)
    {
        card.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { dragStart = e.GetPosition(this); dragCandidate = item; };
        card.MouseMove += delegate(object s, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragCandidate != item) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragCandidate = null;
            DragOut(item, card);
        };
        card.MouseLeftButtonUp += delegate
        {
            if (dragCandidate != item) return;
            dragCandidate = null;
            if (there) try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); } catch (Exception) { }
        };
    }

    Border ActionButton(string glyph, string tip, Action<Border> act)
    {
        Border b = null;
        b = UI.Button(UI.Icon(glyph, 12.8, UI.Fg), 30, 30, 15, null, delegate { act(b); });
        b.ToolTip = tip;
        return b;
    }

    // Shows a green tick on an action button for a moment.
    void Done(Border b)
    {
        var t = b.Child as TextBlock;
        if (t == null) return;
        string old = t.Text;
        t.Text = "";
        t.Foreground = UI.Green;
        Later(1200, delegate { t.Text = old; t.Foreground = UI.Fg; });
    }

    static bool IsImage(string path)
    {
        string e = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp" || e == ".gif" || e == ".tif" || e == ".tiff";
    }
}
