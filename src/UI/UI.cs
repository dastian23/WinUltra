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

// ---------------------------------------------------------------- Small UI pieces

static class UI
{
    public static readonly FontFamily TextFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly Brush Fg = B("#F5F5F7"), Dim = B("#9A9AA6"), Card = B("#14FFFFFF"), Rim = B("#1CFFFFFF"), Panel_ = B("#2A2A33"),
        Selected = B("#30FFFFFF"), Blue = B("#3D7BFF"), Orange = B("#FF9F0A"), Green = B("#30D158"), Red = B("#FF453A"), Purple = B("#9F7BFF");

    static void Spring(ScaleTransform s, double to, double ms)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = global::Spring.Soft };
        Timeline.SetDesiredFrameRate(a, 120);
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // A bright edge that catches the light at the top-left and fades round to the bottom-right.
    public static readonly Brush Specular = MakeSpecular();

    static Brush MakeSpecular()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 255, 255, 255), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x1A, 255, 255, 255), 0.5));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, 255, 255, 255), 1));
        b.Freeze();
        return b;
    }

    // A tinted glass fill: a lighter top fading to the colour itself.
    public static Brush GlassFill(Color c, byte alpha)
    {
        var top = Color.FromArgb(alpha, (byte)(c.R + (255 - c.R) * 0.3), (byte)(c.G + (255 - c.G) * 0.3), (byte)(c.B + (255 - c.B) * 0.3));
        var bottom = Color.FromArgb((byte)(alpha * 0.8), c.R, c.G, c.B);
        var b = new LinearGradientBrush(top, bottom, 90);
        b.Freeze();
        return b;
    }

    // A round glass badge for an icon: tinted fill, bright rim and a soft shine across the top.
    // Icons go into the Grid that is its Child.
    public static Border GlassBadge(double size, double shine)
    {
        var shineBrush = new LinearGradientBrush(Color.FromArgb(0x66, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);
        shineBrush.Freeze();
        var g = new Grid();
        g.Children.Add(new System.Windows.Shapes.Ellipse { Height = shine, Margin = new Thickness(size * 0.12, 1.5, size * 0.12, 0), VerticalAlignment = VerticalAlignment.Top, Fill = shineBrush, IsHitTestVisible = false });
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
            Background = GlassFill(Color.FromRgb(0x9A, 0x9A, 0xAA), 0x3C), BorderBrush = Specular, BorderThickness = new Thickness(1), Child = g,
        };
    }

    public static Brush B(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        b.Freeze();
        return b;
    }

    public static TextBlock Text(string s, double size, Brush fg, FontWeight weight)
    {
        return new TextBlock { Text = s, FontSize = size, Foreground = fg, FontWeight = weight, FontFamily = TextFont, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    }

    public static TextBlock Icon(string glyph, double size, Brush fg)
    {
        return new IconBlock { Text = glyph, FontSize = size, Foreground = fg, FontFamily = IconFont, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    }

    // An icon whose drawn shape, not its line box, is centred. The icon font sits its glyphs high in
    // the line, so a plainly centred icon looked higher than the words next to it.
    public class IconBlock : TextBlock
    {
    static readonly Dictionary<string, double> offsets = new Dictionary<string, double>();
    static readonly Typeface face = new Typeface(UI.IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    readonly TranslateTransform shift = new TranslateTransform();

    static IconBlock()
    {
        PropertyChangedCallback recentre = (d, e) => { var b = (IconBlock)d; b.shift.Y = Offset(b.Text, b.FontSize); };
        TextProperty.OverrideMetadata(typeof(IconBlock), new FrameworkPropertyMetadata(string.Empty, recentre));
        FontSizeProperty.OverrideMetadata(typeof(IconBlock), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, recentre));
    }

    public IconBlock() { RenderTransform = shift; }

    static double Offset(string glyph, double size)
    {
        if (string.IsNullOrEmpty(glyph)) return 0;
        string key = glyph + "|" + size;
        double y;
        if (offsets.TryGetValue(key, out y)) return y;
        try
        {
#pragma warning disable 618
            var text = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, Brushes.White);
#pragma warning restore 618
            var box = text.BuildGeometry(new Point(0, 0)).Bounds;
            y = box.IsEmpty ? 0 : text.Height / 2 - (box.Top + box.Height / 2);
        }
        catch (Exception) { y = 0; }
        offsets[key] = y;
        return y;
    }
    }

    // A clickable rounded box. It lightens on hover and stops the click reaching whatever is behind it.
    public static Border Button(UIElement child, double width, double height, double radius, Brush background, Action click)
    {
        var b = new Border { Child = child, CornerRadius = new CornerRadius(radius), Background = background ?? Brushes.Transparent, Cursor = Cursors.Hand };
        if (width > 0) b.Width = width;
        if (height > 0) b.Height = height;
        bool pressed = false;
        var scale = new ScaleTransform(1, 1);
        b.RenderTransform = scale;
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        b.MouseEnter += delegate { b.Opacity = 0.8; };
        b.MouseLeave += delegate { b.Opacity = 1; if (pressed) Spring(scale, 1, 420); pressed = false; };
        b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { pressed = true; e.Handled = true; Spring(scale, 0.94, 260); };
        b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (!pressed) return;
            pressed = false;
            Spring(scale, 1, 480);
            if (click != null) click();
        };
        return b;
    }

    public static Border Panel(UIElement child, Thickness padding)
    {
        return new Border { Child = child, Background = Card, BorderBrush = Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = padding };
    }

    public static Grid Columns(params GridLength[] widths)
    {
        var g = new Grid();
        foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return g;
    }

    public static T At<T>(T e, int column) where T : UIElement { Grid.SetColumn(e, column); return e; }
    public static T Row<T>(T e, int row) where T : UIElement { Grid.SetRow(e, row); return e; }
    public static readonly GridLength Auto = GridLength.Auto, Star = new GridLength(1, GridUnitType.Star);
    public static GridLength Px(double v) { return new GridLength(v); }
}
