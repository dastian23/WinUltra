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
    // ---------------------------------------------------------------- radios and night light

    async void InitRadios()
    {
        try
        {
            await Radio.RequestAccessAsync();
            var all = await Radio.GetRadiosAsync();
            foreach (var r in all)
            {
                if (r.Kind == RadioKind.WiFi && wifi == null) wifi = r;
                else if (r.Kind == RadioKind.Bluetooth && bluetooth == null) bluetooth = r;
            }
            if (wifi != null) wifi.StateChanged += (s, a) => Ui(UpdateRadioTiles);
            if (bluetooth != null) bluetooth.StateChanged += (s, a) => Ui(UpdateRadioTiles);
        }
        catch (Exception ex) { Store.Log("radios: " + ex.Message); }
        UpdateRadioTiles();
    }

    void UpdateRadioTiles()
    {
        if (!built) return;
        SetTile(wifiTile, wifi == null ? -1 : wifi.State == RadioState.On ? 1 : 0, UI.Blue);
        SetTile(btTile, bluetooth == null ? -1 : bluetooth.State == RadioState.On ? 1 : 0, UI.Blue);
    }

    async void ToggleRadio(Radio r)
    {
        if (r == null) return;
        try { await r.SetStateAsync(r.State == RadioState.On ? RadioState.Off : RadioState.On); }
        catch (Exception ex) { Store.Log("radio toggle: " + ex.Message); }
        UpdateRadioTiles();
    }

    void ToggleNightLight()
    {
        int state = NightLight.State();
        if (state < 0 || !NightLight.Set(state == 0))
        {
            try { Process.Start("ms-settings:nightlight"); } catch (Exception) { }
            return;
        }
        SetTile(nightTile, state == 0 ? 1 : 0, UI.Orange);
    }
}
