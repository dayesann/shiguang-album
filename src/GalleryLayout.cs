namespace Shiguang;

// Rows, rather than individual files, remain virtualized by the WPF gallery.
public static class GalleryLayout
{
    public const double Gap = 6;
    public static List<GalleryRow> Build(IReadOnlyList<Photo> photos, double available, bool groupDates, double target = 170)
    {
        available = Math.Max(200, available);
        var result = new List<GalleryRow>();
        var pending = new List<Photo>(); double ratios = 0; string label = "", lastDate = "";
        static double Ratio(Photo p) => p.Width > 0 && p.Height > 0 ? Math.Clamp((double)p.Width / p.Height, .12, 12) : 1;
        void Flush(bool fill)
        {
            if (pending.Count == 0) return;
            // Four DIP per tile are reserved for the selection border, outside the image.
            double fit = (available - pending.Count * (Gap + 4)) / ratios;
            double height = fill ? fit : Math.Min(target, fit);
            result.Add(new(label, pending.Select(p => new GalleryItem(p, height * Ratio(p) + 4, height)).ToArray()));
            label = ""; pending.Clear(); ratios = 0;
        }
        foreach (var photo in photos)
        {
            if (groupDates && photo.DateLabel != lastDate)
            {
                Flush(false); lastDate = photo.DateLabel;
                label = DateTime.TryParse(photo.DateLabel, out var date) ? date.ToString("yyyy年 M月 d日") : "日期待确认";
            }
            pending.Add(photo); ratios += Ratio(photo);
            if (ratios * target + pending.Count * (Gap + 4) >= available) Flush(true);
        }
        Flush(false);
        return result;
    }
}
