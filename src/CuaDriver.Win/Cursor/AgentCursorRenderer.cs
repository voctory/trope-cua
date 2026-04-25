using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CuaDriver.Win.Cursor;

internal static class AgentCursorRenderer
{
    public const int Supersample = 3;

    public static void ConfigureHighQuality(Graphics g)
    {
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
    }

    public static void DrawBloom(Graphics g, PointF p, float scale, double breath)
    {
        DrawGaussianGlow(
            g,
            p,
            (float)((64 + 3 * breath) * scale),
            (float)((19.5 + breath) * scale),
            Color.FromArgb(188, 232, 252),
            centerAlpha: (int)Math.Round(70 + 16 * breath));

        DrawGaussianGlow(
            g,
            p,
            (float)((30 + breath) * scale),
            (float)((8.5 + 0.5 * breath) * scale),
            Color.FromArgb(238, 248, 255),
            centerAlpha: (int)Math.Round(42 + 10 * breath));
    }

    public static void DrawCursor(Graphics g, PointF p, double heading, float scale)
    {
        using var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new PointF(14 * scale, 0),
            new PointF(-8 * scale, -9 * scale),
            new PointF(-3 * scale, 0),
            new PointF(-8 * scale, 9 * scale),
        });

        using var matrix = new Matrix();
        matrix.Rotate((float)((heading + Math.PI) * 180 / Math.PI));
        matrix.Translate(p.X, p.Y, MatrixOrder.Append);
        path.Transform(matrix);

        var bounds = path.GetBounds();
        if (bounds.Width < 1 || bounds.Height < 1)
            return;

        using var brush = new LinearGradientBrush(bounds, Color.White, Color.White, 135f)
        {
            InterpolationColors = new ColorBlend
            {
                Colors =
                [
                    Color.FromArgb(219, 238, 255),
                    Color.FromArgb(94, 192, 232),
                    Color.FromArgb(84, 205, 160)
                ],
                Positions = [0.0f, 0.53f, 1.0f]
            }
        };
        using var outline = new Pen(Color.White, 1.5f * scale) { LineJoin = LineJoin.Round };
        g.FillPath(brush, path);
        g.DrawPath(outline, path);
    }

    private static void DrawGaussianGlow(
        Graphics g,
        PointF p,
        float radius,
        float sigma,
        Color color,
        int centerAlpha)
    {
        if (radius <= 0 || sigma <= 0 || centerAlpha <= 0)
            return;

        var renderScale = Supersample;
        var diameter = Math.Max(1, (int)Math.Ceiling(radius * 2 * renderScale));
        var center = (diameter - 1) / 2.0;
        var maxRadius = radius * renderScale;
        var sigmaPixels = sigma * renderScale;
        var maxRadiusSquared = maxRadius * maxRadius;
        var sigmaDenominator = 2 * sigmaPixels * sigmaPixels;

        using var bitmap = new Bitmap(diameter, diameter, PixelFormat.Format32bppPArgb);
        var rect = new Rectangle(0, 0, diameter, diameter);
        var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var stride = data.Stride;
            var strideAbs = Math.Abs(stride);
            var buffer = new byte[strideAbs * diameter];

            for (var y = 0; y < diameter; y++)
            {
                var dy = y - center;
                var row = stride >= 0 ? y * stride : (diameter - 1 - y) * strideAbs;
                for (var x = 0; x < diameter; x++)
                {
                    var dx = x - center;
                    var r2 = dx * dx + dy * dy;
                    if (r2 > maxRadiusSquared)
                        continue;

                    var alpha = (int)Math.Round(centerAlpha * Math.Exp(-r2 / sigmaDenominator));
                    if (alpha <= 0)
                        continue;

                    var index = row + x * 4;
                    buffer[index] = (byte)(color.B * alpha / 255);
                    buffer[index + 1] = (byte)(color.G * alpha / 255);
                    buffer[index + 2] = (byte)(color.R * alpha / 255);
                    buffer[index + 3] = (byte)alpha;
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        var dest = new RectangleF(p.X - radius, p.Y - radius, radius * 2, radius * 2);
        g.DrawImage(bitmap, dest);
    }
}
