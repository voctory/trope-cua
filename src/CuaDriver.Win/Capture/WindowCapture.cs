using System.Drawing;
using System.Drawing.Imaging;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Capture;

public sealed record CapturedImage(byte[] Data, int Width, int Height, int OriginalWidth, int OriginalHeight, double ScaleFactor, string MimeType, string Route)
{
    public double ResizeRatio => Width > 0 ? OriginalWidth / (double)Width : 1.0;
}

public static class WindowCapture
{
    public static CapturedImage Capture(IntPtr hwnd, int maxImageDimension, long quality = 85, string format = "jpeg")
    {
        var rect = NativeMethods.GetBestWindowRect(hwnd);
        if (rect.IsEmpty)
            throw new InvalidOperationException("Window has empty bounds.");

        using var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        var printed = false;
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            try
            {
                printed = NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        if (!printed)
        {
            using var fallback = Graphics.FromImage(bitmap);
            fallback.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(rect.Width, rect.Height), CopyPixelOperation.SourceCopy);
        }

        using var final = ResizeIfNeeded(bitmap, maxImageDimension);
        var (data, mimeType) = Encode(final, format, quality);
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        var route = printed ? "gdi.printwindow" : "gdi.copyfromscreen.window";
        return new CapturedImage(data, final.Width, final.Height, bitmap.Width, bitmap.Height, scale, mimeType, route);
    }

    public static CapturedImage CaptureVirtualScreen(int maxImageDimension, long quality = 95, string format = "png")
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Virtual screen has empty bounds.");

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        using var final = ResizeIfNeeded(bitmap, maxImageDimension);
        var (data, mimeType) = Encode(final, format, quality);
        return new CapturedImage(data, final.Width, final.Height, bitmap.Width, bitmap.Height, 1.0, mimeType, "gdi.copyfromscreen.virtual_screen");
    }

    private static Bitmap ResizeIfNeeded(Bitmap bitmap, int maxDimension)
    {
        if (maxDimension <= 0)
            return (Bitmap)bitmap.Clone();

        var longest = Math.Max(bitmap.Width, bitmap.Height);
        if (longest <= maxDimension)
            return (Bitmap)bitmap.Clone();

        var ratio = maxDimension / (double)longest;
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * ratio));
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(resized);
        g.DrawImage(bitmap, 0, 0, width, height);
        return resized;
    }

    private static (byte[] Data, string MimeType) Encode(Bitmap bitmap, string format, long quality)
    {
        return NormalizeFormat(format) switch
        {
            "png" => (ImageEncoding.EncodePng(bitmap), "image/png"),
            _ => (ImageEncoding.EncodeJpeg(bitmap, quality), "image/jpeg")
        };
    }

    private static string NormalizeFormat(string format)
    {
        var normalized = format.Trim().ToLowerInvariant();
        if (normalized is "jpg")
            return "jpeg";
        return normalized is "png" or "jpeg" ? normalized : "png";
    }
}
