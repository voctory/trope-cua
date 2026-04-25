using System.Drawing;
using CuaDriver.Win.Capture;
using CuaDriver.Win.Tooling;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Recording;

internal static class RecordingClickMarker
{
    public static void Write(string destination, string screenshotPath, PointF screenPoint, WindowInfo window)
    {
        try
        {
            using var image = Image.FromFile(screenshotPath);
            using var bitmap = new Bitmap(image);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var localX = screenPoint.X - window.Bounds.X;
            var localY = screenPoint.Y - window.Bounds.Y;
            var scaleX = bitmap.Width / (float)Math.Max(1, window.Bounds.Width);
            var scaleY = bitmap.Height / (float)Math.Max(1, window.Bounds.Height);
            var x = localX * scaleX;
            var y = localY * scaleY;

            using var fill = new SolidBrush(Color.FromArgb(210, 255, 64, 64));
            using var outline = new Pen(Color.White, 2);
            graphics.FillEllipse(fill, x - 8, y - 8, 16, 16);
            graphics.DrawEllipse(outline, x - 8, y - 8, 16, 16);
            AtomicFile.WriteAllBytes(destination, ImageEncoding.EncodePng(bitmap));
        }
        catch
        {
            // Marker is best-effort.
        }
    }
}
