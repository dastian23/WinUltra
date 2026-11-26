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
    // ---------------------------------------------------------------- media

    async void InitMedia()
    {
        try
        {
            mediaManager = await MediaManager.RequestAsync();
            propsHandler = (s, a) => Ui(RefreshMedia);
            playbackHandler = (s, a) => Ui(RefreshMedia);
            mediaCurrentSessionChangedHandler = (s, a) => Ui(HookSession);
            mediaSessionsChangedHandler = (s, a) => Ui(HookSession);
            mediaManager.CurrentSessionChanged += mediaCurrentSessionChangedHandler;
            mediaManager.SessionsChanged += mediaSessionsChangedHandler;
            HookSession();
        }
        catch (Exception ex) { Store.Log("media: " + ex.Message); }
    }

    void HookSession()
    {
        try
        {
            if (session != null)
            {
                session.MediaPropertiesChanged -= propsHandler;
                session.PlaybackInfoChanged -= playbackHandler;
            }
            session = mediaManager.GetCurrentSession();
            if (session != null)
            {
                session.MediaPropertiesChanged += propsHandler;
                session.PlaybackInfoChanged += playbackHandler;
            }
        }
        catch (Exception ex) { Store.Log("media session: " + ex.Message); session = null; }
        RefreshMedia();
    }

    static string PrettyApp(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        string s = id;
        int bang = s.LastIndexOf('!');
        if (bang >= 0) s = s.Substring(bang + 1);
        int slash = s.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) s = s.Substring(slash + 1);
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
        if (s.Length > 0) s = char.ToUpper(s[0]) + s.Substring(1);
        return s == "App" ? "" : s;
    }

    // The average colour of the artwork, brightened, tints the island's glow.
    static Color AverageColor(BitmapSource art)
    {
        var small = new FormatConvertedBitmap(new TransformedBitmap(art, new ScaleTransform(8.0 / art.PixelWidth, 8.0 / art.PixelHeight)), PixelFormats.Bgra32, null, 0);
        int w = small.PixelWidth, h = small.PixelHeight;
        var px = new byte[w * h * 4];
        small.CopyPixels(px, w * 4, 0);
        double r = 0, g = 0, b = 0;
        for (int i = 0; i < px.Length; i += 4) { b += px[i]; g += px[i + 1]; r += px[i + 2]; }
        double n = w * h, max = Math.Max(1, Math.Max(r, Math.Max(g, b)) / n);
        double k = 255 / max;
        return Color.FromRgb((byte)Math.Min(255, r / n * k), (byte)Math.Min(255, g / n * k), (byte)Math.Min(255, b / n * k));
    }

    async void RefreshMedia()
    {
        int version = ++mediaVersion;
        var s = session;
        string newTitle = null, newArtist = null, newAppId = null;
        bool isPlaying = false;
        ImageSource art = artwork;
        try
        {
            if (s != null)
            {
                isPlaying = s.GetPlaybackInfo().PlaybackStatus == PlaybackStatus.Playing;
                var props = await s.TryGetMediaPropertiesAsync();
                if (version != mediaVersion) return;
                newTitle = props.Title;
                newArtist = props.Artist;
                string key = s.SourceAppUserModelId + "|" + newTitle + "|" + newArtist;
                if (key != mediaKey)
                {
                    art = null;
                    if (props.Thumbnail != null)
                    {
                        var ms = new MemoryStream();
                        using (var ras = await props.Thumbnail.OpenReadAsync())
                        using (var st = ras.AsStreamForRead())
                            st.CopyTo(ms);
                        ms.Position = 0;
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.DecodePixelWidth = 300;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();
                        art = bi;
                    }
                    if (version != mediaVersion) return;
                    mediaKey = key;
                    try { SetTint(Color.FromRgb(0x00, 0x00, 0x00)); } catch (Exception) { }
                }
                appName = PrettyApp(s.SourceAppUserModelId);
                newAppId = s.SourceAppUserModelId;
            }
        }
        catch (Exception ex) { Store.Log("media read: " + ex.Message); }
        if (version != mediaVersion) return;

        if (s == null || string.IsNullOrEmpty(newTitle)) { newTitle = null; newArtist = null; art = null; mediaKey = null; isPlaying = false; appName = null; newAppId = null; }
        if (newAppId != mediaAppId)
        {
            mediaAppId = newAppId;
            mediaAppIcon = AppIcons.For(newAppId);
        }
        artwork = art;
        playing = isPlaying;
        title = newTitle;
        artist = newArtist;
        if (playing)
        {
            if (micApps.Count > 0) { micLevelTimer.Stop(); ResetMicLevel(); }
        }
        else if (micApps.Count > 0 && !micLevelTimer.IsEnabled)
        {
            micLevelTimer.Start();
        }
        UpdateMediaUi();
        UpdateCollapsed();
        UpdateTickRate();
    }

    async void MediaCommand(int which)
    {
        var s = session;
        if (s == null) return;
        try
        {
            if (which == 0) await s.TrySkipPreviousAsync();
            else if (which == 1) await s.TryTogglePlayPauseAsync();
            else await s.TrySkipNextAsync();
        }
        catch (Exception) { }
    }
}
