using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTuner;

public static class TaskbarAuraColorPolicy
{
    public static Color? ResolvePrimaryColor(ImageSource? source)
    {
        if (source is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return null;

        try
        {
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var stride = checked(converted.PixelWidth * 4);
            var pixels = new byte[checked(stride * converted.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0);
            var buckets = new Dictionary<int, (double Score, int Count, long Red, long Green, long Blue)>();

            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                var alpha = pixels[offset + 3];
                var maximum = Math.Max(red, Math.Max(green, blue));
                var minimum = Math.Min(red, Math.Min(green, blue));
                var chroma = maximum == 0 ? 0 : (maximum - minimum) / (double)maximum;
                if (alpha < 80 || maximum < 42 || maximum > 248 || chroma < 0.18) continue;

                var key = ((red >> 4) << 8) | ((green >> 4) << 4) | (blue >> 4);
                buckets.TryGetValue(key, out var bucket);
                bucket.Score += 0.35 + chroma;
                bucket.Count++;
                bucket.Red += red;
                bucket.Green += green;
                bucket.Blue += blue;
                buckets[key] = bucket;
            }

            if (buckets.Count == 0) return null;
            var winner = buckets.Values.OrderByDescending(bucket => bucket.Score).First();
            return Color.FromRgb(
                (byte)(winner.Red / winner.Count),
                (byte)(winner.Green / winner.Count),
                (byte)(winner.Blue / winner.Count));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    public static RadialGradientBrush CreateBrush(Color color)
    {
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.9,
            RadiusY = 1
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(140, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(92, color.R, color.G, color.B), 0.32));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(28, color.R, color.G, color.B), 0.72));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        return brush;
    }
}
