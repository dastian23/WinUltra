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
    // ---------------------------------------------------------------- Stats

    Grid BuildStats()
    {
        var page = UI.Columns(UI.Star, UI.Px(8), UI.Star, UI.Px(8), UI.Star, UI.Px(8), UI.Star);
        cpuGraph = new Graph(Color.FromRgb(0x6F, 0x8C, 0xFF));
        gpuGraph = new Graph(Color.FromRgb(0xB9, 0x7C, 0xFF));
        netGraph = new Graph(Color.FromRgb(0x5E, 0xD0, 0xE0)) { AutoScale = true };
        cpuValue = UI.Text("–", 20, expandedFg, FontWeights.Light);
        gpuValue = UI.Text("–", 20, expandedFg, FontWeights.Light);
        netDown = UI.Text("–", 14.4, expandedFg, FontWeights.Normal);
        page.Children.Add(UI.At(GraphCard("CPU", cpuValue, null, cpuGraph), 0));
        var gpuTemp = UI.Text("", 11.2, expandedDim, FontWeights.SemiBold);
        gpuValue.Tag = gpuTemp;
        page.Children.Add(UI.At(GraphCard("GPU", gpuValue, gpuTemp, gpuGraph), 2));
        netUp = UI.Text("", 11.2, expandedDim, FontWeights.Normal);
        page.Children.Add(UI.At(GraphCard("Network", netDown, netUp, netGraph, true), 4));

        var b = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        b.Children.Add(UI.Text("Battery", 12, expandedDim, FontWeights.SemiBold));
        battBig = UI.Text("–", 20, expandedFg, FontWeights.Light);
        b.Children.Add(battBig);
        battStatus = UI.Text("", 12, expandedDim, FontWeights.Normal);
        b.Children.Add(battStatus);
        var track = new Grid { Height = 8, Margin = new Thickness(0, 34, 0, 0) };
        track.Children.Add(new Border { Background = UI.B("#2EFFFFFF"), CornerRadius = new CornerRadius(4) });
        battFill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Children.Add(battFill);
        b.Children.Add(track);
        var card = UI.Panel(b, new Thickness(12, 12, 12, 12));
        page.Children.Add(UI.At(card, 6));
        return page;
    }

    Border GraphCard(string label, TextBlock value, TextBlock extra, Graph graph) { return GraphCard(label, value, extra, graph, false); }

    // extraBelow puts the second reading under the first (upload under download) instead of beside it.
    Border GraphCard(string label, TextBlock value, TextBlock extra, Graph graph, bool extraBelow)
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = UI.Star });
        g.Children.Add(UI.Row(UI.Text(label, 12, expandedDim, FontWeights.SemiBold), 0));
        if (extraBelow)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
            stack.Children.Add(value);
            stack.Children.Add(extra);
            g.Children.Add(UI.Row(stack, 1));
        }
        else
        {
            var line = UI.Columns(UI.Auto, UI.Star);
            line.Children.Add(UI.At(value, 0));
            if (extra != null) { extra.Margin = new Thickness(6, 4, 0, 0); line.Children.Add(UI.At(extra, 1)); }
            g.Children.Add(UI.Row(line, 1));
        }
        graph.Margin = new Thickness(0, 8, 0, 0);
        graph.Height = double.NaN;
        g.Children.Add(UI.Row(graph, 2));
        return UI.Panel(g, new Thickness(12, 12, 12, 12));
    }

    // 1536 -> "1.5 KB/s", 2500000 -> "2.4 MB/s".
    public static string Speed(double bytes)
    {
        if (bytes < 0) return "–";
        if (bytes < 1024 * 1024) return Math.Round(bytes / 1024) + " KB/s";
        return (bytes / 1024 / 1024).ToString("0.0") + " MB/s";
    }

    void UpdateBattery()
    {
        if (!built) return;
        batteryLabel.Text = batteryPercent < 0 ? "" : batteryPercent + "%" + (onAc ? "  ⚡" : "");
        batteryLabel.Foreground = onAc ? UI.Green : (batteryPercent >= 0 && batteryPercent <= 20 ? UI.Red : expandedDim);
        battBig.Text = batteryPercent < 0 ? "No battery" : batteryPercent + "%";
        battStatus.Text = !onAc ? "On battery" : charging ? "Charging" : "Plugged in";
        var color = batteryPercent >= 0 && batteryPercent <= 20 && !onAc ? UI.Red : UI.Green;
        battFill.Background = color;
        double full = Math.Max(0, ((FrameworkElement)battFill.Parent).ActualWidth);
        if (full <= 0) full = 120;
        battFill.BeginAnimation(WidthProperty, Anim(full * Math.Max(0, batteryPercent) / 100.0, 600, Spring.Soft, 0));
    }

    // Starts readings that only matter while the panel is open.
    void StartLive()
    {
        UpdateBattery();
        UpdateRadioTiles();
        SetTile(nightTile, NightLight.State() == 1 ? 1 : 0, UI.Orange);
        SetTile(muteTile, volume.Available ? (volume.Muted ? 1 : 0) : -1, UI.Red);
        if (volume.Available)
        {
            volSlider.Value = volume.Level;
            SetVolumeGlyph(volume.Level, volume.Muted);
        }
        ThreadPool.QueueUserWorkItem(delegate
        {
            int b = Brightness.Get();
            Ui(delegate
            {
                brightSlider.IsEnabled = b >= 0;
                brightSlider.Opacity = b >= 0 ? 1 : 0.4;
                if (b >= 0 && !brightSlider.Dragging) brightSlider.Value = b / 100.0;
            });
        });
        UpdateSensors();
        UpdateProgress();
        UpdateTickRate();
    }

    void StopLive()
    {
        UpdateSensors();
        UpdateTickRate();
    }

    // CPU, GPU and network readings (including the GPU temperature) run only while the Stats tab is on screen.
    void UpdateSensors()
    {
        bool want = mode == Mode.Expanded && tab == TabStats;
        if (want && sensorTimer == null) sensorTimer = new System.Threading.Timer(SensorTick, null, 0, 1500);
        else if (!want && sensorTimer != null) { sensorTimer.Dispose(); sensorTimer = null; }
    }

    void SensorTick(object state)
    {
        if (!Monitor.TryEnter(sensorGate)) return;
        try
        {
            double cpu, gpu, down, up;
            int temp;
            sensors.Sample(out cpu, out gpu, out temp, out down, out up);
            Ui(delegate
            {
                if (down >= 0) { netGraph.Add(down); netDown.Text = "\u2193 " + Speed(down); netUp.Text = "\u2191 " + Speed(up); }
                if (cpu >= 0) { cpuGraph.Add(cpu); cpuValue.Text = Math.Round(cpu) + "%"; }
                if (gpu >= 0) { gpuGraph.Add(gpu); gpuValue.Text = Math.Round(gpu) + "%"; }
                ((TextBlock)gpuValue.Tag).Text = temp >= 0 ? temp + " °C" : "";
            });
        }
        catch (Exception ex) { Store.Log("sensors: " + ex.Message); }
        finally { Monitor.Exit(sensorGate); }
    }
}
