using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Capture;

public static class DebugCrosshair
{
    public static void WriteCrosshair(WindowCapture capture, WindowInfo window, PointF point, int maxImageDimension, string path)
    {
        var resolvedPath = ExpandPath(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(resolvedPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var shot = capture.Capture(window.Hwnd, maxImageDimension, quality: 100);
        using var ms = new MemoryStream(shot.Data);
        using var image = Image.FromStream(ms);
        using var bitmap = new Bitmap(image);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var width = bitmap.Width;
        var ringRadius = Math.Max(6f, width / 80f);
        var armLength = Math.Max(12f, width / 40f);
        var lineWidth = Math.Max(1.5f, width / 400f);
        var cx = point.X;
        var cy = point.Y;

        using var pen = new Pen(Color.FromArgb(242, 255, 26, 26), lineWidth);
        using var fill = new SolidBrush(Color.FromArgb(242, 255, 26, 26));

        graphics.DrawEllipse(pen, cx - ringRadius, cy - ringRadius, ringRadius * 2, ringRadius * 2);
        graphics.DrawLine(pen, cx - armLength, cy, cx + armLength, cy);
        graphics.DrawLine(pen, cx, cy - armLength, cx, cy + armLength);

        var dotRadius = Math.Max(1.5f, lineWidth * 1.5f);
        graphics.FillEllipse(fill, cx - dotRadius, cy - dotRadius, dotRadius * 2, dotRadius * 2);

        bitmap.Save(resolvedPath, ImageFormat.Png);
    }

    private static string ExpandPath(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return path;
    }
}
