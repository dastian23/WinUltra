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

// ---------------------------------------------------------------- self test

static class SelfTest
{
    static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op) { return op.AsTask().Result; }

    public static void Run(string outFile)
    {
        var r = new StringBuilder();
        Action<string> say = s => r.AppendLine(s);

        say("Dynamic Island self test, " + DateTime.Now);

        try
        {
            var mgr = Wait(MediaManager.RequestAsync());
            var s = mgr.GetCurrentSession();
            if (s == null) say("Media: no media session open right now (reading works, nothing is playing).");
            else
            {
                var p = Wait(s.TryGetMediaPropertiesAsync());
                say("Media: app=" + s.SourceAppUserModelId + " status=" + s.GetPlaybackInfo().PlaybackStatus + " title=\"" + p.Title + "\" artist=\"" + p.Artist + "\" artwork=" + (p.Thumbnail != null));
            }
            say("Media sessions open: " + mgr.GetSessions().Count);
        }
        catch (Exception ex) { say("Media: FAILED " + ex.Message); }

        try
        {
            var v = new Volume();
            if (!v.Init()) say("Volume: FAILED to open the default speakers");
            else
            {
                float before = v.Level;
                float step = before > 0.5f ? before - 0.02f : before + 0.02f;
                v.Level = step;
                Thread.Sleep(200);
                float stepped = v.Level;
                v.Level = before;
                Thread.Sleep(200);
                say(string.Format("Volume: was {0:0.000}, stepped to {1:0.000}, put back to {2:0.000}, muted={3}", before, stepped, v.Level, v.Muted));
            }
        }
        catch (Exception ex) { say("Volume: FAILED " + ex.Message); }

        try
        {
            int before = Brightness.Get();
            if (before < 0) say("Brightness: not available");
            else
            {
                int step = before >= 95 ? before - 5 : before + 5;
                Brightness.Set(step);
                Thread.Sleep(700);
                int stepped = Brightness.Get();
                Brightness.Set(before);
                Thread.Sleep(700);
                say("Brightness: was " + before + ", stepped to " + stepped + ", put back to " + Brightness.Get());
            }
        }
        catch (Exception ex) { say("Brightness: FAILED " + ex.Message); }

        try
        {
            say("Radio access: " + Wait(Radio.RequestAccessAsync()));
            foreach (var radio in Wait(Radio.GetRadiosAsync())) say("Radio: " + radio.Kind + " \"" + radio.Name + "\" " + radio.State);
        }
        catch (Exception ex) { say("Radios: FAILED " + ex.Message); }

        say("Night light: " + (NightLight.State() == 1 ? "on" : NightLight.State() == 0 ? "off" : "blob not recognised (button will open Settings)"));
        try
        {
            var l = Windows.UI.Notifications.Management.UserNotificationListener.Current;
            say("Notifications: access " + Wait(l.RequestAccessAsync()) + ", " + Wait(l.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast)).Count + " in the notification centre");
        }
        catch (Exception ex) { say("Notifications: FAILED " + ex.Message); }

        if (Environment.GetCommandLineArgs().Contains("--nightlight"))
        {
            // Flip it, read it back, then put the original bytes back exactly.
            var original = NightLight.Snapshot();
            int before = NightLight.State();
            bool ok = NightLight.Set(before != 1);
            Thread.Sleep(2500);
            int after = NightLight.State();
            NightLight.Restore(original);
            Thread.Sleep(1000);
            say("Night light toggle: was " + before + ", write ok=" + ok + ", read back " + after + ", restored to " + NightLight.State());
        }

        try
        {
            var sensors = new Sensors();
            sensors.Init();
            for (int i = 0; i < 3; i++)
            {
                Thread.Sleep(1000);
                double cpu, gpu, down, up;
                int temp;
                sensors.Sample(out cpu, out gpu, out temp, out down, out up);
                say(string.Format("Sensors: CPU {0:0.0}%  GPU {1:0.0}%  GPU temp {2}  network down {3} up {4}", cpu, gpu, temp < 0 ? "n/a" : temp + " C", Island.Speed(down), Island.Speed(up)));
            }
        }
        catch (Exception ex) { say("Sensors: FAILED " + ex.Message); }

        try
        {
            var report = Windows.Devices.Power.Battery.AggregateBattery.GetReport();
            Native.SYSTEM_POWER_STATUS ps;
            Native.GetSystemPowerStatus(out ps);
            say("Battery: " + ps.BatteryLifePercent + "% AC=" + ps.ACLineStatus + " WinRT status=" + report.Status);
        }
        catch (Exception ex) { say("Battery: FAILED " + ex.Message); }

        try
        {
            var pw = new PrivacyWatch();
            bool reported = false;
            pw.Changed += () => reported = true;
            pw.Start();
            Thread.Sleep(1500);
            Func<List<string>, string> show = l => l.Count == 0 ? "none" : string.Join(", ", l);
            say("Camera monitor: " + (pw.CameraMonitorRunning ? "running" : "NOT running") + (reported ? ", first report received" : ", no report yet")
                + "; camera in use: " + show(PrivacyWatch.LiveCamera()) + "; microphone in use: " + show(PrivacyWatch.LiveMicrophone())
                + "; registry says camera: " + show(PrivacyWatch.InUse("webcam")) + ", microphone: " + show(PrivacyWatch.InUse("microphone")));
        }
        catch (Exception ex) { say("Camera monitor: FAILED " + ex.Message); }

        File.WriteAllText(outFile, r.ToString());
    }
}
