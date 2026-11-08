using System.Windows;

namespace Shiguang;

public sealed partial class Photo
{
    public string MediaKind { get; set; } = "image";
    public string ContentKind { get; set; } = "photo";
    public double Duration { get; set; }
    public string Added { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public string PreviewState { get; set; } = "ready";
    public bool ManualKind { get; set; }
    public string Tags { get; set; } = "";
    public long Revision { get; set; }
    public string ThumbKey => ThumbPath + "|" + Revision;
    public string LargePath => System.IO.Path.Combine(AppPaths.Data, "previews", Id + "-" + Modified + ".jpg");
    public bool IsVideo => MediaKind == "video";
    public bool IsPanorama => ContentKind == "panorama";
    public bool IsScreenshot => ContentKind == "screenshot";
    public string KindLabel => IsVideo ? "视频" : IsPanorama ? "全景图" : IsScreenshot ? "截图" : "照片";
    public string IconKind => IsVideo ? "video" : IsPanorama ? "image_multiple" : IsScreenshot ? "screenshot" : "image";
    public string TypeLabel => KindLabel + " · " + System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public string DurationLabel => Duration > 0 ? TimeSpan.FromSeconds(Duration).ToString(Duration >= 3600 ? @"h\:mm\:ss" : @"mm\:ss") : "视频";
    public string MetaLabel => PreviewState == "pending" ? "正在生成预览" : Error.Length > 0 ? "预览不可用" : IsVideo ? TypeLabel + " · " + DurationLabel : TypeLabel;
    public string CaptureLabel => DateLabel + " · " + DateSource;
    public Visibility VideoVisibility => IsVideo ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AlertVisibility => Error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FavoriteVisibility => Favorite ? Visibility.Visible : Visibility.Collapsed;
    public void RefreshFrom(Photo p)
    {
        Taken = p.Taken; DateSource = p.DateSource; Width = p.Width; Height = p.Height; Error = p.Error;
        MediaKind = p.MediaKind; ContentKind = p.ContentKind; Duration = p.Duration; PreviewState = p.PreviewState;
        Revision = p.Revision; Tags = p.Tags; Favorite = p.Favorite; Notify("");
    }
}
public sealed record MediaFilter(string View = "timeline", string Album = "", string Kind = "all", string Year = "", string Month = "", string Day = "", string Search = "");
public sealed record TimeBucket(string Key, string Label, int Count, string Cover, string SecondCover = "")
{
    public string CountLabel => $"{Count:N0} 个项目";
}
public sealed record GalleryItem(Photo Photo, double Width, double Height);
public sealed record GalleryRow(string Label, IReadOnlyList<GalleryItem> Items)
{
    public Visibility LabelVisibility => Label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}
