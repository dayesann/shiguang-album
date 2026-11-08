using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shiguang;

public static partial class Imaging
{
    [GeneratedRegex(@"(?<!\d)((?:19|20)\d{2})[-_]?([01]\d)[-_]?([0-3]\d)(?!\d)")]
    private static partial Regex DatePattern();

    public static Photo Read(string path, string root, Photo? previous = null)
    {
        var fi = new FileInfo(path);
        var p = new Photo { Id = previous?.Id ?? Guid.NewGuid().ToString("N"), Path = path, Root = previous?.Root ?? root, Size = fi.Length, Modified = fi.LastWriteTimeUtc.Ticks, Favorite = previous?.Favorite ?? false };
        DateTime? shot = null;
        DateTime? filenameDate = null;
        var m = DatePattern().Match(System.IO.Path.GetFileNameWithoutExtension(path));
        if (m.Success && DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) filenameDate = parsed;
        try
        {
            int orientation = 1;
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                p.Width = frame.PixelWidth; p.Height = frame.PixelHeight;
                if ((long)p.Width * p.Height > 180_000_000) throw new IOException("图像尺寸过大，请使用外部查看器");
                if (frame.Metadata is BitmapMetadata metadata)
                {
                    try { shot = ParseDate(metadata.GetQuery("/app1/ifd/exif/{ushort=36867}")?.ToString()); } catch { }
                    try { shot ??= ParseDate(metadata.DateTaken); } catch { }
                    foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
                        try { if (metadata.GetQuery(query) is object value) { orientation = Convert.ToInt32(value); break; } } catch { }
                }
            }
            var thumb = Decode(path, 480, orientation);
            if (orientation is >= 5 and <= 8) (p.Width, p.Height) = (p.Height, p.Width);
            var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
            encoder.Frames.Add(BitmapFrame.Create(thumb));
            var temp = p.ThumbPath + ".tmp";
            using (var output = File.Create(temp)) encoder.Save(output);
            File.Move(temp, p.ThumbPath, true);
            var metrics = Analyze(thumb);
            p.VisualHash = metrics.Hash.ToString("X16"); p.Sharpness = metrics.Sharpness; p.Brightness = metrics.Brightness;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            p.Error = System.IO.Path.GetExtension(path).Equals(".heic", StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetExtension(path).Equals(".heif", StringComparison.OrdinalIgnoreCase)
                ? "无法解码 HEIC：请检查 HEIF/HEVC 扩展，或文件是否完整。" : "无法读取图像：" + e.Message;
            if (File.Exists(p.ThumbPath)) File.Delete(p.ThumbPath);
        }
        p.Taken = (shot ?? filenameDate ?? fi.LastWriteTime).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        p.DateSource = shot.HasValue ? "拍摄时间" : filenameDate.HasValue ? "文件名推断" : "修改时间推断";
        if (shot.HasValue && filenameDate.HasValue && Math.Abs((shot.Value.Date - filenameDate.Value.Date).TotalDays) > 1) p.DateSource = "日期冲突";
        if (previous?.DateSource == "手动确认") { p.Taken = previous.Taken; p.DateSource = previous.DateSource; }
        fi.Refresh();
        if (fi.Length != p.Size || fi.LastWriteTimeUtc.Ticks != p.Modified) throw new IOException("扫描期间文件发生变化，请重新扫描");
        return p;
    }
    static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[] { "yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy:MM:dd HH:mm:ssK" };
        if (DateTime.TryParseExact(value.Trim('\0', ' '), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out d))
            if (d.Year >= 1900 && d <= DateTime.Now.AddDays(2)) return DateTime.SpecifyKind(d, DateTimeKind.Unspecified);
        return null;
    }
    public static BitmapSource Decode(string path, int width, int orientation = 1)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var image = new BitmapImage(); image.BeginInit(); image.StreamSource = stream; image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; image.DecodePixelWidth = width; image.EndInit(); image.Freeze();
        if (orientation == 1) return image;
        Matrix matrix = orientation switch
        {
            2 => new(-1, 0, 0, 1, 0, 0), 3 => new(-1, 0, 0, -1, 0, 0), 4 => new(1, 0, 0, -1, 0, 0),
            5 => new(0, 1, 1, 0, 0, 0), 6 => new(0, 1, -1, 0, 0, 0), 7 => new(0, -1, -1, 0, 0, 0), 8 => new(0, -1, 1, 0, 0, 0), _ => Matrix.Identity
        };
        var transformed = new TransformedBitmap(image, new MatrixTransform(matrix)); transformed.Freeze(); return transformed;
    }
    public static BitmapSource Full(Photo photo)
    {
        int orientation = 1;
        int decodeWidth;
        using (var stream = File.Open(photo.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var f = decoder.Frames[0];
            decodeWidth = Math.Max(1, (int)(f.PixelWidth * Math.Min(1d, 6144d / Math.Max(f.PixelWidth, f.PixelHeight))));
            if (decoder.Frames[0].Metadata is BitmapMetadata meta)
                foreach (var q in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" }) try { if (meta.GetQuery(q) is object o) { orientation = Convert.ToInt32(o); break; } } catch { }
        }
        return Decode(photo.Path, decodeWidth, orientation);
    }
    public static byte[] Gray(BitmapSource source, int w, int h)
    {
        var scaled = new TransformedBitmap(source, new ScaleTransform((double)w / source.PixelWidth, (double)h / source.PixelHeight));
        var gray = new FormatConvertedBitmap(scaled, PixelFormats.Gray8, null, 0);
        var pixels = new byte[w * h]; gray.CopyPixels(pixels, w, 0); return pixels;
    }
    public static (ulong Hash, double Sharpness, double Brightness) Analyze(BitmapSource source)
    {
        var small = Gray(source, 9, 8); ulong hash = 0;
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) if (small[y * 9 + x] > small[y * 9 + x + 1]) hash |= 1UL << (y * 8 + x);
        var px = Gray(source, 128, 128); double sum = 0, squared = 0; int n = 0;
        for (int y = 1; y < 127; y++) for (int x = 1; x < 127; x++) { int k = y * 128 + x; double v = px[k - 1] + px[k + 1] + px[k - 128] + px[k + 128] - 4 * px[k]; sum += v; squared += v * v; n++; }
        return (hash, Math.Max(0, squared / n - sum * sum / n / n), px.Average(b => (double)b));
    }
    public static bool Similar(Photo a, Photo b)
    {
        if (a.Height == 0 || b.Height == 0 || Math.Abs((double)a.Width / a.Height - (double)b.Width / b.Height) > .04) return false;
        var x = Gray(Decode(a.ThumbPath, 64), 32, 32); var y = Gray(Decode(b.ThumbPath, 64), 32, 32);
        double error = 0; for (int i = 0; i < x.Length; i++) error += (x[i] - y[i]) * (x[i] - y[i]);
        return Math.Sqrt(error / x.Length) <= 22;
    }
}
