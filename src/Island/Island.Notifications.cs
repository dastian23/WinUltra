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
    // ---------------------------------------------------------------- notifications

    async void InitNotifications()
    {
        try
        {
            listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
            if (await listener.RequestAccessAsync() != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed) { listener = null; return; }
            var list = await listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
            seenNotes = new HashSet<uint>(list.Select(n => n.Id));
            // Windows offers no change event to unpackaged apps, so the island asks for the list.
            // Each ask costs about 35 ms of CPU, so it asks every 3 s while you are using the PC,
            // every 20 s once you have been idle for 2 minutes, and not at all behind a full-screen app.
            noteTimer.Start();
        }
        catch (Exception ex) { listener = null; Store.Log("notifications: " + ex.Message); }
    }

    async void PollNotifications()
    {
        var input = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.LASTINPUTINFO)) };
        bool idle = Native.GetLastInputInfo(ref input) && (uint)Environment.TickCount - input.dwTime > 120000;
        var interval = TimeSpan.FromMilliseconds(idle ? 20000 : 3000);
        if (noteTimer.Interval != interval) noteTimer.Interval = interval;
        if (listener == null || notePolling || hidden) return;
#if FRIENDS
        if (!WinUltra.App.Options.Notifications) return;
#endif
        notePolling = true;
        try
        {
            var list = await listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
            var ids = new HashSet<uint>();
            Windows.UI.Notifications.UserNotification newest = null;
            foreach (var n in list)
            {
                ids.Add(n.Id);
                if (!seenNotes.Contains(n.Id) && (newest == null || n.CreationTime > newest.CreationTime)) newest = n;
            }
            seenNotes = ids;
            int state;
            bool quiet = Native.SHQueryUserNotificationState(out state) == 0 && state != 5 && state != 7;    // busy, full screen or quiet time
            if (newest != null && !quiet && !hidden && !focusMode) await ShowNotification(newest);    // Focus holds them back
        }
        catch (Exception ex) { Store.Log("notification poll: " + ex.Message); }
        notePolling = false;
    }

    async Task ShowNotification(Windows.UI.Notifications.UserNotification n)
    {
        string app = "", head = "", body = "";
        ImageSource logo = null;
        try { app = n.AppInfo.DisplayInfo.DisplayName; } catch (Exception) { }
        try
        {
            var binding = n.Notification.Visual.GetBinding(Windows.UI.Notifications.KnownNotificationBindings.ToastGeneric);
            if (binding != null)
            {
                var texts = binding.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                if (texts.Count > 0) head = texts[0];
                if (texts.Count > 1) body = string.Join("  ", texts.Skip(1));
            }
        }
        catch (Exception) { }
        try
        {
            var reference = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(48, 48));
            var ms = new MemoryStream();
            using (var ras = await reference.OpenReadAsync())
            using (var st = ras.AsStreamForRead())
                st.CopyTo(ms);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 64;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            logo = bi;
        }
        catch (Exception) { }
        if (string.IsNullOrEmpty(head)) head = "New notification";

        var g = UI.Columns(UI.Px(38), UI.Star);
        g.Margin = new Thickness(14, 0, 22, 0);
        var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center, Background = UI.Panel_ };
        if (logo != null) icon.Child = new Image { Source = logo, Width = 26, Height = 26 };
        else icon.Child = UI.Icon("", 16, UI.Fg);
        g.Children.Add(UI.At(icon, 0));
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        s.Children.Add(UI.Text(app, 11, UI.Dim, FontWeights.SemiBold));
        s.Children.Add(UI.Text(head, 13.5, UI.Fg, FontWeights.SemiBold));
        if (body.Length > 0) s.Children.Add(UI.Text(body.Replace('\n', ' '), 12, UI.Dim, FontWeights.Normal));
        g.Children.Add(UI.At(s, 1));
        ShowPopup(g, NoteW, NoteH, 4200, false);
    }
}
