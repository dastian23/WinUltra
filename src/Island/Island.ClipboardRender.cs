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
    void RenderClips()
    {
        if (!built) return;
        clipList.Children.Clear();
        clipCount.Text = (clips.Count == 0 ? "" : clips.Count + (clips.Count == 1 ? " item" : " items") + "   ") + (pickerKeys.Length > 0 ? pickerKeys + PickerHint : "");
        if (clips.Count == 0)
        {
            var empty = UI.Text("Copy some text or an image and it shows up here.", 13, UI.Dim, FontWeights.Normal);
            empty.Margin = new Thickness(4, 30, 0, 0);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            clipList.Children.Add(empty);
            return;
        }
        foreach (var c in clips.Where(x => x.Pinned).Concat(clips.Where(x => !x.Pinned)))
        {
            var clip = c;
            var row = UI.Columns(UI.Star, UI.Auto, UI.Auto);
            UIElement body;
            if (clip.IsImage)
            {
                if (clip.Thumb == null) LoadImage(clip);
                body = new Image { Source = clip.Thumb, Height = 56, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
            }
            else
            {
                string preview = clip.Text.Trim();
                if (preview.Length > 400) preview = preview.Substring(0, 400);
                if (clip.Truncated) preview += "  (truncated)";
                body = new TextBlock { Text = preview, FontSize = 13, Foreground = UI.Fg, FontFamily = UI.TextFont, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 38, VerticalAlignment = VerticalAlignment.Center };
            }
            row.Children.Add(UI.At(body, 0));
            row.Children.Add(UI.At(UI.Button(UI.Icon(clip.Pinned ? "" : "", 13, clip.Pinned ? UI.Orange : UI.Dim), 30, 30, 15, null, delegate { TogglePin(clip); }), 1));
            row.Children.Add(UI.At(UI.Button(UI.Icon("", 12, UI.Dim), 30, 30, 15, null, delegate { RemoveClip(clip); }), 2));
            var card = UI.Button(row, 0, 0, 14, UI.Card, delegate { CopyBack(clip); });
            card.Padding = new Thickness(12, 7, 6, 7);
            card.Margin = new Thickness(0, 0, 0, 6);
            card.BorderThickness = new Thickness(1);
            card.BorderBrush = UI.Rim;
            clipList.Children.Add(card);
        }
    }

    void CopyBack(Clip clip)
    {
        try
        {
            ignoreClipUntil = DateTime.Now.AddMilliseconds(600);
            if (clip.IsImage)
            {
                if (!LoadImage(clip)) return;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(clip.Png);
                bi.EndInit();
                bi.Freeze();
                Clipboard.SetImage(bi);
            }
            else Clipboard.SetText(clip.Text);
            clips.Remove(clip);
            clips.Insert(0, clip);
            RenderClips();
#if FRIENDS
            if (pickerMode)
            {
                var copied = MakePopup("\uE8FB", UI.Green, "Copied", false);
                copied.Value.Text = "Press Ctrl+V";
                copied.Value.Foreground = UI.Dim;
                Collapse();
                ShowPopup(copied.Root, 300, PopH, 1800, false);
                return;
            }
#else
            if (pickerMode)
            {
                // Opened with the picker hotkey: put it straight into the window you were typing in.
                IntPtr target = pasteTarget;
                Collapse();
                Later(90, delegate
                {
                    if (target != IntPtr.Zero) Native.SetForegroundWindow(target);
                    Later(60, Native.SendPaste);
                });
                return;
            }
#endif
            var p = MakePopup("", UI.Green, "Copied", false);
            Collapse();
            ShowPopup(p.Root, 200, PopH, 1200, false);
        }
        catch (Exception ex) { Store.Log("copy back: " + ex.Message); }
    }

    void TogglePin(Clip clip)
    {
        clip.Pinned = !clip.Pinned;
        if (clip.IsImage && clip.Pinned && clip.File == null)
        {
            try
            {
                Directory.CreateDirectory(Store.ImageDir);
                clip.File = Guid.NewGuid().ToString("N") + ".png";
                File.WriteAllBytes(System.IO.Path.Combine(Store.ImageDir, clip.File), Store.Seal(clip.Png));
            }
            catch (Exception) { clip.File = null; }
        }
        if (!clip.Pinned) DeleteClipFile(clip);
        SaveClips();
        RenderClips();
    }

    void RemoveClip(Clip clip)
    {
        clips.Remove(clip);
        if (clip.Pinned) { DeleteClipFile(clip); SaveClips(); }
        RenderClips();
    }

    void DeleteClipFile(Clip clip)
    {
        if (clip.File == null) return;
        try
        {
            File.Delete(System.IO.Path.Combine(Store.ImageDir, clip.File));
            clip.File = null;
        }
        catch (Exception ex) { Store.Log("clip delete: " + ex.Message); }
    }

    // Only pinned clips are saved; text is sealed here, images are sealed when pinned.
    void SaveClips()
    {
        try
        {
            var list = clips.Where(c => c.Pinned).Select(c => new SavedClip
            {
                Kind = c.IsImage ? "image" : "text",
                Text = c.IsImage ? null : Convert.ToBase64String(Store.Seal(Encoding.UTF8.GetBytes(c.Text))),
                Encrypted = !c.IsImage,
                Truncated = c.Truncated,
                File = c.File
            }).ToList();
            Store.Save(Store.ClipsFile, list);
        }
        catch (Exception ex) { Store.Log("save clips: " + ex.Message); }
    }

    void LoadClips()
    {
        var list = Store.Load<List<SavedClip>>(Store.ClipsFile);
        if (list == null) return;
        foreach (var s in list)
        {
            if (s.Kind == "image")
            {
                // Decode lazy: pinned images are read the first time they are shown or pasted.
                if (string.IsNullOrEmpty(s.File)) continue;
                if (File.Exists(System.IO.Path.Combine(Store.ImageDir, s.File)))
                    clips.Add(new Clip { IsImage = true, Pinned = true, File = s.File });
            }
            else if (!string.IsNullOrEmpty(s.Text))
            {
                try
                {
                    // Pins written before encryption was added are plain text and are read as-is.
                    string text = s.Encrypted ? Encoding.UTF8.GetString(Store.Open(Convert.FromBase64String(s.Text))) : s.Text;
                    if (!string.IsNullOrEmpty(text)) clips.Add(new Clip { Text = text, Pinned = true, Truncated = s.Truncated });
                }
                catch (Exception ex) { Store.Log("clip text: " + ex.Message); }
            }
        }
    }

    bool LoadImage(Clip clip)
    {
        if (clip.Png != null) return true;
        if (string.IsNullOrEmpty(clip.File)) return false;
        try
        {
            byte[] raw = File.ReadAllBytes(System.IO.Path.Combine(Store.ImageDir, clip.File));
            if (raw.Length > MaxImageBytes) return false;
            byte[] png;
            try { png = Store.Open(raw); }
            catch (Exception) { png = raw; }    // an image saved before pin encryption was added
            if (png.Length > MaxImageBytes) return false;
            clip.Png = png;
            clip.Hash = HashOf(png);
            clip.Thumb = Thumbnail(png);
            return true;
        }
        catch (Exception ex) { Store.Log("clip image: " + ex.Message); return false; }
    }
}
