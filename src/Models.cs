using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;

namespace Shiguang;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed partial class Photo : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string Root { get; set; } = "";
    public long Size { get; set; }
    public long Modified { get; set; }
    public string Taken { get; set; } = "";
    public string DateSource { get; set; } = "待确认";
    public int Width { get; set; }
    public int Height { get; set; }
    public string Hash { get; set; } = "";
    string visualHash = "";
    public string VisualHash
    {
        get => visualHash;
        set
        {
            visualHash = value;
            VisualHashBits = value.Length == 16 && ulong.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var bits) ? bits : 0;
        }
    }
    internal ulong VisualHashBits { get; private set; }
    public double Sharpness { get; set; }
    public double Brightness { get; set; }
    public string Error { get; set; } = "";
    public string Status { get; set; } = "active";
    public string OriginalPath { get; set; } = "";
    public string TrashDate { get; set; } = "";
    public bool Favorite { get; set; }
    internal int DuplicateVisit { get; set; }
    bool selected;
    public bool Selected { get => selected; set { if (selected == value) return; selected = value; Notify(); } }
    public string ThumbPath => AppPaths.Thumb(Id);
    public string Name => System.IO.Path.GetFileName(OriginalPath.Length > 0 && Status == "trash" ? OriginalPath : Path);
    public string Month => Taken.Length >= 7 ? Taken[..7] : "日期待确认";
    public string Details => $"{Width} × {Height}  ·  {FormatSize(Size)}";
    public string DateLabel => Taken.Length >= 10 ? Taken[..10] : "日期待确认";
    public string Badge => Error.Length > 0 ? "无法解码" : DateSource is "拍摄时间" or "手动确认" ? (Favorite ? "♥" : "") : "日期待确认";
    public double Quality => Math.Log(1 + (double)Width * Height) * 8 + Math.Log(1 + Sharpness) * 3 - Math.Abs(Brightness - 125) / 12;
    public static string FormatSize(long size) => size >= 1073741824 ? $"{size / 1073741824d:0.0} GB" : size >= 1048576 ? $"{size / 1048576d:0.0} MB" : $"{size / 1024d:0} KB";
}

public sealed record Album(string Id, string Name, int Count)
{
    public string Label => $"{Name}   {Count:N0}";
}
public sealed record PhotoRow(string Label, IReadOnlyList<Photo> Photos)
{
    public System.Windows.Visibility LabelVisibility => Label.Length == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
}
public sealed record JobProgress(string Message, int Completed = 0, int Total = 0, string PhotoId = "");
public sealed record ScanResult(int Added, int Updated, int Skipped, int Failed, int Missing);
public sealed record DuplicateGroup(string Id, string Kind, List<Photo> Photos, string Keeper)
{
    public string Label => $"{(Kind == "exact" ? "完全重复" : "近似候选")} · {Photos.Count} 张\n{Photos.FirstOrDefault(p => p.Id == Keeper)?.Name ?? Photos.FirstOrDefault()?.Name ?? "未命名项目"}";
}
public sealed class ArchiveItem : Observable
{
    public required Photo Photo { get; init; }
    public string Source => Photo.Path;
    public string Destination { get; set; } = "";
    public string DateSource => Photo.DateSource;
    public string Date => Photo.DateLabel;
    public string Note { get; set; } = "";
    bool selected;
    public bool Selected { get => selected; set { selected = value; Notify(); } }
}
public sealed record FileOperation(string Id, string Batch, string Kind, string PhotoId, string Source, string Target, string Hash, string State, string Error, string Created, string SourceRoot = "", string TargetRoot = "", string KeeperId = "");

public static class AppPaths
{
    public static string Data { get; private set; } = "";
    public static string Excluded { get; private set; } = "";
    public static void Init(string data, string excluded)
    {
        Data = System.IO.Path.GetFullPath(data);
        Excluded = System.IO.Path.GetFullPath(excluded);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(System.IO.Path.Combine(Data, "thumbnails"));
    }
    public static void ValidateId(string id)
    {
        // Keep legacy non-GUID identifiers, but never allow path syntax in a file key.
        if (string.IsNullOrEmpty(id) || id.Length > 160 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new IOException("媒体库含不安全的项目标识，已停止操作。");
    }
    public static string CachePath(string directory, string id, string suffix)
    {
        ValidateId(id);
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Data, directory));
        var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, id + suffix + ".jpg"));
        if (!Within(target, root)) throw new IOException("缓存路径超出媒体库。");
        LibraryService.NoLinks(target);
        return target;
    }
    public static string Thumb(string id) => CachePath("thumbnails", id, "");
    public static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
