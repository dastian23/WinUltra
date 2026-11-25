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

// A small line graph that remembers the last 40 readings (60 seconds at 1.5 s per reading).
class Graph : FrameworkElement
{
    readonly double[] data = new double[40];
    int count;
    readonly Pen pen;
    readonly Brush area;
    public bool AutoScale;    // for network speed: the top of the graph follows the busiest recent reading

    public Graph(Color color)
    {
        pen = new Pen(new SolidColorBrush(color), 1.6) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        var gradient = new LinearGradientBrush(Color.FromArgb(90, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        gradient.Freeze();
        area = gradient;
        Height = 40;
    }

    public void Add(double v)
    {
        if (count < data.Length) data[count++] = v;
        else { Array.Copy(data, 1, data, 0, data.Length - 1); data[data.Length - 1] = v; }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (count < 2 || w <= 0) return;
        double step = w / (data.Length - 1);
        double x0 = w - (count - 1) * step;
        double top = 100;
        if (AutoScale)
        {
            top = 64 * 1024;
            for (int i = 0; i < count; i++) top = Math.Max(top, data[i] * 1.2);
        }
        var line = new StreamGeometry();
        var fillShape = new StreamGeometry();
        using (var c = line.Open())
        using (var f = fillShape.Open())
        {
            f.BeginFigure(new Point(x0, h), true, true);
            for (int i = 0; i < count; i++)
            {
                var p = new Point(x0 + i * step, h - 1 - Math.Max(0, Math.Min(top, data[i])) / top * (h - 2));
                if (i == 0) c.BeginFigure(p, false, false); else c.LineTo(p, true, true);
                f.LineTo(p, false, false);
            }
            f.LineTo(new Point(w, h), false, false);
        }
        line.Freeze();
        fillShape.Freeze();
        dc.DrawGeometry(area, null, fillShape);
        dc.DrawGeometry(null, pen, line);
    }
}
