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

// ---------------------------------------------------------------- The island

// A damped spring, like iOS uses: a quick start, a slight overshoot and a soft settle.
class Spring : EasingFunctionBase
{
    double damping = 0.78;

    public static readonly Spring Soft = Make(0.86), Bouncy = Make(0.7);

    static Spring Make(double damping)
    {
        var s = new Spring { damping = damping, EasingMode = EasingMode.EaseIn };
        s.Freeze();
        return s;
    }

    protected override double EaseInCore(double t)
    {
        double w = 6.6 / damping;
        double wd = w * Math.Sqrt(1 - damping * damping);
        return 1 - Math.Exp(-damping * w * t) * (Math.Cos(wd * t) + damping * w / wd * Math.Sin(wd * t));
    }

    protected override Freezable CreateInstanceCore() { return new Spring { damping = damping }; }
}
