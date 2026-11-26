using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

enum TabIcon { Media, Controls, Stats, Clipboard, Shelf, Timer }

static class TabIcons
{
    public const double TabIconSize = 20;
    static double dpiScale = 1.0;
    public static double DpiScale { get { return dpiScale; } set { dpiScale = value; } }

    static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>(StringComparer.Ordinal);

    public static void Load()
    {
    }

    public static BitmapSource Get(TabIcon icon, bool selected)
    {
        return Get(icon, selected, dpiScale);
    }

    public static BitmapSource Get(TabIcon icon, bool selected, double dpi)
    {
        int size = (int)Math.Round(TabIconSize * dpi);
        if (size < 1) size = 1;
        string key = icon.ToString() + ":" + size.ToString();
        BitmapSource hit;
        if (cache.TryGetValue(key, out hit)) return hit;

        using (var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(System.Drawing.Color.Transparent);

            var bounds = new RectangleF(0, 0, size, size);
            DrawTabIcon(graphics, icon.ToString(), bounds, System.Windows.Media.Colors.White);

            IntPtr handle = bitmap.GetHbitmap();
            try
            {
                var result = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                result.Freeze();
                cache[key] = result;
                return result;
            }
            finally
            {
                DeleteObject(handle);
            }
        }
    }

    public static void DrawTabIcon(Graphics g, string name, RectangleF bounds, System.Windows.Media.Color color)
    {
        using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B), 1.5f * bounds.Width / 24f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;

            Func<float, float> X = value => bounds.X + value * bounds.Width / 24f;
            Func<float, float> Y = value => bounds.Y + value * bounds.Height / 24f;
            Func<float, float> S = value => value * bounds.Width / 24f;

            if (name == "Media")
            {
                g.DrawEllipse(pen, X(3), Y(14), S(6), S(6));
                g.DrawEllipse(pen, X(13), Y(14), S(6), S(6));
                g.DrawLine(pen, X(9), Y(17), X(9), Y(4));
                g.DrawLine(pen, X(9), Y(4), X(19), Y(4));
                g.DrawLine(pen, X(19), Y(4), X(19), Y(17));
                g.DrawLine(pen, X(9), Y(8), X(19), Y(8));
            }
            else if (name == "Controls")
            {
                g.DrawEllipse(pen, X(12), Y(4), S(4), S(4));
                g.DrawLine(pen, X(4), Y(6), X(12), Y(6));
                g.DrawLine(pen, X(16), Y(6), X(20), Y(6));
                g.DrawEllipse(pen, X(6), Y(10), S(4), S(4));
                g.DrawLine(pen, X(4), Y(12), X(6), Y(12));
                g.DrawLine(pen, X(10), Y(12), X(20), Y(12));
                g.DrawEllipse(pen, X(15), Y(16), S(4), S(4));
                g.DrawLine(pen, X(4), Y(18), X(15), Y(18));
                g.DrawLine(pen, X(19), Y(18), X(20), Y(18));
            }
            else if (name == "Stats")
            {
                DrawRoundedRect(g, pen, X(3), Y(12), S(6), S(8), S(1));
                DrawRoundedRect(g, pen, X(15), Y(8), S(6), S(12), S(1));
                DrawRoundedRect(g, pen, X(9), Y(4), S(6), S(16), S(1));
                g.DrawLine(pen, X(4), Y(20), X(18), Y(20));
            }
            else if (name == "Clipboard")
            {
                DrawRoundedRect(g, pen, X(5), Y(5), S(14), S(16), S(2));
                DrawRoundedRect(g, pen, X(9), Y(3), S(6), S(4), S(2));
            }
            else if (name == "Shelf")
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLine(X(5), Y(4), X(9), Y(4));
                    path.AddLine(X(9), Y(4), X(12), Y(7));
                    path.AddLine(X(12), Y(7), X(19), Y(7));
                    path.AddArc(X(17), Y(7), S(4), S(4), 270, 90);
                    path.AddLine(X(21), Y(9), X(21), Y(17));
                    path.AddArc(X(17), Y(15), S(4), S(4), 0, 90);
                    path.AddLine(X(19), Y(19), X(5), Y(19));
                    path.AddArc(X(3), Y(15), S(4), S(4), 90, 90);
                    path.AddLine(X(3), Y(17), X(3), Y(6));
                    path.AddArc(X(3), Y(4), S(4), S(4), 180, 90);
                    path.CloseFigure();
                    g.DrawPath(pen, path);
                }
            }
            else if (name == "Timer")
            {
                g.DrawEllipse(pen, X(3), Y(3), S(18), S(18));
                g.DrawLine(pen, X(12), Y(7), X(12), Y(12));
                g.DrawLine(pen, X(12), Y(12), X(15), Y(15));
            }
        }
    }

    static void DrawRoundedRect(Graphics g, System.Drawing.Pen pen, float x, float y, float width, float height, float radius)
    {
        using (var path = new GraphicsPath())
        {
            float diameter = radius * 2;
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            g.DrawPath(pen, path);
        }
    }

    public static void Dispose()
    {
        cache.Clear();
    }

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);
}
