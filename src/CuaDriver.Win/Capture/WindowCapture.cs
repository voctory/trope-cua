using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using CuaDriver.Win.Win32;

namespace CuaDriver.Win.Capture;

public sealed record CapturedImage(byte[] Data, int Width, int Height, int OriginalWidth, int OriginalHeight, double ScaleFactor, string MimeType, string Route);

public sealed class WindowCapture
{
    public CapturedImage Capture(IntPtr hwnd, int maxImageDimension, long quality = 85)
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
        var data = EncodeJpeg(final, quality);
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        return new CapturedImage(data, final.Width, final.Height, bitmap.Width, bitmap.Height, scale, "image/jpeg", "gdi.printwindow");
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

    private static byte[] EncodeJpeg(Bitmap bitmap, long quality)
    {
        using var ms = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 1, 100));
        bitmap.Save(ms, encoder, parameters);
        return ms.ToArray();
    }
}
