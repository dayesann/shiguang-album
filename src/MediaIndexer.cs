using System.Globalization;
using System.Text.RegularExpressions;

namespace Shiguang;

// Enumeration only: scanning never loads an original-media decoder.
public sealed partial class MediaIndexer(Catalog catalog)
{
    public readonly ManualResetEventSlim Pause = new(true);
    public static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".webm", ".wmv", ".mts", ".m2ts", ".3gp" };
    [GeneratedRegex(@"(?<!\d)((?:19|20)\d{2})[-_]?([01]\d)[-_]?([0-3]\d)(?!\d)")]
    private static partial Regex FilenameDate();
    public static Photo Skeleton(string file, string root, Photo? old = null)
    {
        var fi = new FileInfo(file); var p = old ?? new Photo();
        p.Path = file; p.Root = old?.Root ?? root; p.Size = fi.Length; p.Modified = fi.LastWriteTimeUtc.Ticks;
        p.MediaKind = Videos.Contains(System.IO.Path.GetExtension(file)) ? "video" : "image";
        if (!p.ManualKind) p.ContentKind = GuessKind(file, 0, 0);
        if (p.DateSource != "手动确认")
        {
            var m = FilenameDate().Match(System.IO.Path.GetFileNameWithoutExtension(file));
            var fromName = m.Success && DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            p.Taken = (fromName ? DateTime.ParseExact(m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value, "yyyyMMdd", CultureInfo.InvariantCulture) : fi.LastWriteTime).ToString("yyyy-MM-dd HH:mm:ss");
            p.DateSource = fromName ? "文件名推断" : "修改时间推断";
        }
        p.Status = "active"; p.PreviewState = "pending"; p.Error = ""; p.Hash = ""; p.VisualHash = ""; return p;
    }
    public static string GuessKind(string file, int width, int height)
    {
        var name = file.ToLowerInvariant();
        if (name.Contains("screenshot") || name.Contains("screen shot") || name.Contains("截图") || name.Contains("屏幕快照")) return "screenshot";
        if (name.Contains("全景") || name.Contains("panorama") || name.Contains("pano_") || (height > 0 && (double)width / height >= 2.8)) return "panorama";
        return "photo";
    }
    public ScanResult Scan(string root, IProgress<JobProgress> progress, CancellationToken ct)
    {
        if (catalog.Operations().Count > 0) throw new IOException("存在未完成文件操作，请先在设置的任务恢复中处理，再重新扫描。");
        root = System.IO.Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new IOException("来源目录不可访问：" + root);
        LibraryService.NoLinks(root);
        if (Excluded(root)) throw new IOException("请添加实际媒体目录，程序、缓存与测试目录会被跳过。");
        catalog.AddRoot(root);
        int added = 0, updated = 0, skipped = 0, failed = 0, missing = 0, visited = 0;
        var batch = new List<Photo>(160); bool changed = false;
        void Flush() { if (batch.Count == 0) return; catalog.SaveIndexBatch(batch); batch.Clear(); }
        var dirs = new Stack<string>(); dirs.Push(root);
        while (dirs.Count > 0)
        {
            ct.ThrowIfCancellationRequested(); Pause.Wait(ct); var dir = dirs.Pop();
            try
            {
                foreach (var child in Directory.EnumerateDirectories(dir))
                    try { if (!Excluded(child) && !System.IO.Path.GetFileName(child).StartsWith('.') && (File.GetAttributes(child) & (FileAttributes.ReparsePoint | FileAttributes.System)) == 0) dirs.Push(child); } catch { failed++; }
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    ct.ThrowIfCancellationRequested(); Pause.Wait(ct);
                    if (!LibraryService.Extensions.Contains(System.IO.Path.GetExtension(file)) && !Videos.Contains(System.IO.Path.GetExtension(file))) continue;
                    try
                    {
                        if (LibraryService.Reparse(file)) { skipped++; continue; }
                        var fi = new FileInfo(file); var old = catalog.ByPath(file);
                        if (old?.Status == "trash") continue;
                        if (old is { Status: "active" } && old.Size == fi.Length && old.Modified == fi.LastWriteTimeUtc.Ticks && old.PreviewState != "error" && (old.PreviewState == "pending" || File.Exists(old.ThumbPath))) skipped++;
                        else { batch.Add(Skeleton(file, root, old)); if (old == null) added++; else updated++; changed = true; }
                        if (batch.Count >= 160) Flush();
                        if (++visited % 100 == 0) progress.Report(new($"建立索引 {visited:N0} 项 · 新增 {added:N0}", visited));
                    }
                    catch (OperationCanceledException) { Flush(); throw; }
                    catch (Exception e) { failed++; LibraryService.Log("索引", file, e); }
                }
            }
            catch (OperationCanceledException) { Flush(); throw; }
            catch (Exception e) { failed++; LibraryService.Log("目录索引", dir, e); }
            Flush(); // Persist the last partial batch before a directory boundary can be canceled.
        }
        Flush();
        foreach (var p in catalog.RootPhotos(root))
        {
            ct.ThrowIfCancellationRequested();
            try { File.GetAttributes(p.Path); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { if (p.Status != "missing") { catalog.MarkMissing(p.Id); missing++; } }
            catch { }
        }
        if (changed || missing > 0) catalog.ClearDuplicates();
        return new(added, updated, skipped, failed, missing);
    }
    static bool Excluded(string path) => AppPaths.Within(path, AppPaths.Data) || AppPaths.Within(path, AppPaths.Excluded) || File.Exists(System.IO.Path.Combine(path, ".shiguang-ignore")) || path.Contains("\\.shiguang-trash", StringComparison.OrdinalIgnoreCase);
}
