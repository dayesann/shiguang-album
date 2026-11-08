using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Shiguang;

public sealed class ThumbnailImage : Image
{
    public static readonly DependencyProperty FileProperty = DependencyProperty.Register(nameof(File), typeof(string), typeof(ThumbnailImage), new PropertyMetadata("", Changed));
    public string File { get => (string)GetValue(FileProperty); set => SetValue(FileProperty, value); }
    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.Register(nameof(DecodeWidth), typeof(double), typeof(ThumbnailImage), new PropertyMetadata(480d, Changed));
    public double DecodeWidth { get => (double)GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }
    static readonly SemaphoreSlim Workers = new(3);
    static readonly object Gate = new();
    static readonly Dictionary<string, BitmapSource> Cache = [];
    static readonly Queue<string> Order = new();
    static long cacheBytes;
    int version;
    public ThumbnailImage()
    {
        Loaded += (_, _) => Load();
        Unloaded += (_, _) => { version++; Source = null; };
    }
    static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) { var i = (ThumbnailImage)d; i.version++; i.Source = null; if (i.IsLoaded) i.Load(); }
    public static void ClearCache() { lock (Gate) { Cache.Clear(); Order.Clear(); cacheBytes = 0; } }
    async void Load()
    {
        int current = ++version; int pixels = DecodeWidth > 640 ? 1600 : 480; string cacheKey = File + "|decode=" + pixels; string path = File.Split('|')[0];
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (Gate) if (Cache.TryGetValue(cacheKey, out var value)) { Source = value; return; }
        try
        {
            await Workers.WaitAsync();
            BitmapSource? source = null;
            try { if (current != version || !IsLoaded) return; source = await Task.Run(() => Imaging.Decode(path, pixels)); }
            finally { Workers.Release(); }
            lock (Gate)
            {
                if (!Cache.ContainsKey(cacheKey)) { Cache[cacheKey] = source; Order.Enqueue(cacheKey); cacheBytes += (long)source.PixelWidth * source.PixelHeight * Math.Max(1, source.Format.BitsPerPixel / 8); }
                while (Order.Count > 64 || cacheBytes > 64L * 1024 * 1024)
                {
                    var key = Order.Dequeue(); if (Cache.Remove(key, out var old)) cacheBytes -= (long)old.PixelWidth * old.PixelHeight * Math.Max(1, old.Format.BitsPerPixel / 8);
                }
            }
            if (current == version && IsLoaded) Source = source;
        }
        catch { /* A missing thumbnail gets the placeholder, never an empty-file deletion. */ }
    }
}
