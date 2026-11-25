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
    // ---------------------------------------------------------------- camera/mic dot, Bluetooth, brightness, Caps Lock

    void UpdatePrivacy()
    {
        // Registry scans and audio-session COM calls are too slow for the dispatcher.
        if (privacyBusy) return;
        privacyBusy = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            var cam = new List<string>();
            var mic = new List<string>();
            try
            {
                cam = PrivacyWatch.InUse("webcam").Concat(PrivacyWatch.LiveCamera()).Distinct().ToList();
                mic = PrivacyWatch.InUse("microphone").Concat(PrivacyWatch.LiveMicrophone()).Distinct().ToList();
            }
            catch (Exception ex) { Store.Log("privacy: " + ex.Message); }
            Ui(delegate { privacyBusy = false; ApplyPrivacy(cam, mic); });
        });
    }

    void ApplyPrivacy(List<string> cam, List<string> mic)
    {
        string started = cam.FirstOrDefault(a => !camApps.Contains(a));
        bool camStarted = started != null;
        if (started == null) started = mic.FirstOrDefault(a => !micApps.Contains(a));
        camApps = cam;
        micApps = mic;
        bool any = cam.Count > 0 || mic.Count > 0;
        // Green for the camera, orange for the microphone alone, as on the iPhone.
        privacyDot.Fill = cam.Count > 0 ? UI.Green : UI.Orange;
        privacyGlow.Color = cam.Count > 0 ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0xFF, 0x9F, 0x0A);
        privacyDot.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (any && !privacyPulsing)
        {
            var pulse = new DoubleAnimation(1, 0.45, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            Timeline.SetDesiredFrameRate(pulse, 20);
            privacyDot.BeginAnimation(UIElement.OpacityProperty, pulse);
            privacyPulsing = true;
        }
        else if (!any && privacyPulsing)
        {
            privacyDot.BeginAnimation(UIElement.OpacityProperty, null);    // stops the pulse, so nothing redraws while idle
            privacyPulsing = false;
        }
        if (privacyLabel != null)
        {
            privacyLabel.Text = any ? "● " + string.Join(", ", cam.Concat(mic).Distinct()) : "";
            privacyLabel.Foreground = cam.Count > 0 ? UI.Green : UI.Orange;
        }
        if (started != null)
        {
            var p = camStarted ? MakePopup("", UI.Green, "Camera in use", false) : MakePopup("", UI.Orange, "Microphone in use", false);
            p.Value.Text = started;
            ShowPopup(p.Root, PopW, PopH, 2600, false);
        }
    }

    void InitBluetoothWatch()
    {
        try
        {
            var selectors = new[]
            {
                Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromConnectionStatus(Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected),
                Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected),
            };
            foreach (var selector in selectors)
            {
                var watcher = Windows.Devices.Enumeration.DeviceInformation.CreateWatcher(selector, new[] { "System.Devices.Aep.ContainerId" }, Windows.Devices.Enumeration.DeviceInformationKind.AssociationEndpoint);
                watcher.Added += (s, info) =>
                {
                    string id = info.Id, name = info.Name;
                    object container;
                    info.Properties.TryGetValue("System.Devices.Aep.ContainerId", out container);
                    Ui(delegate { OnBluetoothAdded(id, name, container); });
                };
                watcher.Removed += (s, update) => { string id = update.Id; Ui(delegate { OnBluetoothRemoved(id); }); };
                watcher.Updated += (s, update) => { };
                watcher.EnumerationCompleted += (s, o) => Ui(delegate { btCompleted++; });    // devices already connected at start-up don't pop
                btWatchers.Add(watcher);
                watcher.Start();
            }
        }
        catch (Exception ex) { Store.Log("bluetooth watch: " + ex.Message); }
    }

    bool BluetoothReady { get { return btWatchers.Count > 0 && btCompleted >= btWatchers.Count; } }

    async void OnBluetoothAdded(string id, string name, object container)
    {
        bool known = btNames.ContainsKey(id);
        btNames[id] = name;
        if (!BluetoothReady || known || string.IsNullOrEmpty(name)) return;
        if (name == lastBtName && (DateTime.Now - lastBtAt).TotalSeconds < 5) return;    // dual-mode devices arrive twice
        lastBtName = name;
        lastBtAt = DateTime.Now;
        string battery = "";
        try
        {
            if (container is Guid)
            {
                var found = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync("System.Devices.ContainerId:=\"{" + container + "}\"", new[] { BatteryKey }, Windows.Devices.Enumeration.DeviceInformationKind.Device);
                foreach (var d in found)
                {
                    object v;
                    if (d.Properties.TryGetValue(BatteryKey, out v) && v is byte) { battery = (byte)v + "%"; break; }
                }
            }
        }
        catch (Exception) { }
        var p = MakePopup("", UI.Blue, name, false);
        p.Value.Text = battery.Length > 0 ? battery : "Connected";
        ShowPopup(p.Root, PopW, PopH, 2600, false);
    }

    void OnBluetoothRemoved(string id)
    {
        string name;
        if (!btNames.TryGetValue(id, out name)) return;
        btNames.Remove(id);
        if (!BluetoothReady || string.IsNullOrEmpty(name)) return;
        if (name == lastBtName && (DateTime.Now - lastBtAt).TotalSeconds < 2) return;
        lastBtName = name;
        lastBtAt = DateTime.Now;
        var p = MakePopup("", UI.Dim, name, false);
        p.Value.Text = "Disconnected";
        p.Value.Foreground = UI.Dim;
        ShowPopup(p.Root, PopW, PopH, 2200, false);
    }

    // The brightness keys raise a WMI event from the display driver; nothing is polled.
    void InitBrightnessWatch()
    {
        try
        {
            brightWatcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            brightWatcher.EventArrived += (s, e) =>
            {
                int level = Convert.ToInt32(e.NewEvent["Brightness"]);
                Ui(delegate { OnBrightnessChanged(level); });
            };
            brightWatcher.Start();
        }
        catch (Exception ex) { brightWatcher = null; Store.Log("brightness watch: " + ex.Message); }
    }

    void OnBrightnessChanged(int level)
    {
        if (mode == Mode.Expanded)
        {
            if (built && !brightSlider.Dragging) brightSlider.Value = level / 100.0;
            return;
        }
        if (DateTime.Now < ownBrightnessUntil) return;
        if (brightPopupCache == null) brightPopupCache = MakePopup("", UI.Fg, "", true);
        var p = brightPopupCache;
        p.Value.Text = level.ToString();
        p.Fill.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(level / 100.0, 260, Spring.Soft, 0));
        ShowPopup(p.Root, PopW, PopH, 1800, false);
    }

    void ShowCapsLock()
    {
        bool on = (Native.GetKeyState(0x14) & 1) != 0;
        var p = MakePopup("", on ? UI.Orange : UI.Dim, "Caps Lock", false);
        p.Value.Text = on ? "On" : "Off";
        p.Value.Foreground = on ? UI.Orange : UI.Dim;
        ShowPopup(p.Root, 250, PopH, 1400, false);
    }
}
