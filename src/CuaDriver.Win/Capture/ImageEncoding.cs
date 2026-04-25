using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace CuaDriver.Win.Capture;

internal static class ImageEncoding
{
    public static byte[] EncodePng(Image image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static byte[] EncodeJpeg(Image image, long quality)
    {
        using var ms = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 1, 95));
        image.Save(ms, encoder, parameters);
        return ms.ToArray();
    }
}
