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
    void InitPower()
    {
        ReadPower();
        wasOnAc = onAc;
        try
        {
            batteryHandler = (s, a) => Ui(OnPowerChanged);
            Windows.Devices.Power.Battery.AggregateBattery.ReportUpdated += batteryHandler;
        }
        catch (Exception) { batteryHandler = null; }
        powerHandler = (s, a) => { if (a.Mode == PowerModes.StatusChange) Ui(OnPowerChanged); };
        SystemEvents.PowerModeChanged += powerHandler;
        UpdateCollapsed();
        Later(60000, PowerPoll);
    }

    void PowerPoll()
    {
        OnPowerChanged();
        Later(60000, PowerPoll);
    }

    void ReadPower()
    {
        Native.SYSTEM_POWER_STATUS s;
        if (!Native.GetSystemPowerStatus(out s)) return;
        onAc = s.ACLineStatus == 1;
        charging = (s.BatteryFlag & 8) != 0;
        batteryPercent = s.BatteryLifePercent == 255 || (s.BatteryFlag & 128) != 0 ? -1 : s.BatteryLifePercent;
    }

    void OnPowerChanged()
    {
        int before = batteryPercent;
        ReadPower();
        if (built) UpdateBattery();
        if (onAc != wasOnAc)
        {
            wasOnAc = onAc;
            var p = onAc ? MakePopup("", UI.Green, "Charger connected", false) : MakePopup("", UI.Fg, "On battery", false);
            p.Value.Text = batteryPercent >= 0 ? batteryPercent + "%" : "";
            ShowPopup(p.Root, PopW, PopH, 2400, false);
            lowWarned = 101;
        }
        else if (!onAc && batteryPercent >= 0 && before != batteryPercent && (batteryPercent <= 20 && lowWarned > 20 || batteryPercent <= 10 && lowWarned > 10))
        {
            lowWarned = batteryPercent;
            var p = MakePopup("", UI.Red, "Battery low", false);
            p.Value.Text = batteryPercent + "%";
            ShowPopup(p.Root, PopW, PopH, 3000, false);
        }
        UpdateCollapsed();
    }
}
