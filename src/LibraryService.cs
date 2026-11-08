using System.IO;
using System.Security.Cryptography;
using System.Numerics;

namespace Shiguang;

public sealed class LibraryService(Catalog catalog)
{
    public readonly ManualResetEventSlim Pause = new(true);
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".bmp", ".webp", ".tif", ".tiff", ".gif" };
    public static bool Reparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    public static void NoLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current != null) { if ((File.Exists(current) || Directory.Exists(current)) && Reparse(current)) throw new IOException("为避免越过符号链接操作文件，请选择实际目录：" + current); current = Path.GetDirectoryName(current); }
    }
    public ScanResult Scan(string root, IProgress<JobProgress> progress, CancellationToken ct)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (root.Length == 2 && root[1] == ':') root += "\\";
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("照片目录不可访问：" + root);
        NoLinks(root);
        if (AppPaths.Within(root, AppPaths.Excluded) || AppPaths.Within(root, AppPaths.Data) || root.Contains(".shiguang-trash", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(root, ".shiguang-ignore"))) throw new IOException("不能把程序、测试目录或最近删除目录作为照片来源");
        if (catalog.Operations().Count > 0) throw new IOException("请先处理未完成的文件操作，再重新扫描。");
        catalog.AddRoot(root);
        int added = 0, updated = 0, skipped = 0, failed = 0, missing = 0, visited = 0;
        bool invalidated = false;
        void Invalidate() { if (!invalidated) { catalog.ClearDuplicates(); invalidated = true; } }
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested(); Pause.Wait(ct);
            var dir = stack.Pop();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (AppPaths.Within(sub, AppPaths.Excluded) || AppPaths.Within(sub, AppPaths.Data) || Path.GetFileName(sub).StartsWith('.') || (File.GetAttributes(sub) & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0 || File.Exists(Path.Combine(sub, ".shiguang-ignore"))) continue;
                    stack.Push(sub);
                }
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    ct.ThrowIfCancellationRequested(); Pause.Wait(ct);
                    if (!Extensions.Contains(Path.GetExtension(file)) || Reparse(file)) continue;
                    try
                    {
                        var before = catalog.ByPath(file);
                        if (before?.Status == "trash") continue;
                        var fi = new FileInfo(file);
                        if (before is { Status: "active" } && before.Size == fi.Length && before.Modified == fi.LastWriteTimeUtc.Ticks && File.Exists(before.ThumbPath)) skipped++;
                        else
                        {
                            Invalidate();
                            var p = Imaging.Read(file, root, before); catalog.Upsert(p);
                            if (before == null) added++; else updated++;
                            if (p.Error.Length > 0) failed++;
                        }
                        visited++;
                        if (visited % 12 == 0) progress.Report(new($"已扫描 {visited:N0} 张 · 新增 {added:N0} · 更新 {updated:N0}", visited));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) { failed++; Log("扫描", file, e); }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { failed++; Log("目录", dir, e); }
        }
        // Mark only confirmed absent files. Access failures are not evidence of deletion.
        foreach (var p in catalog.RootPhotos(root))
        {
            ct.ThrowIfCancellationRequested();
            try { File.GetAttributes(p.Path); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { if (p.Status != "missing") { Invalidate(); catalog.MarkMissing(p.Id); missing++; } }
            catch { }
        }
        return new(added, updated, skipped, failed, missing);
    }
    public List<DuplicateGroup> FindDuplicates(IProgress<JobProgress> progress, CancellationToken ct)
    {
        var active = catalog.AllActive(); var groups = new List<DuplicateGroup>();
        var visual = active
            .Where(p => p.Error.Length == 0 && p.VisualHash.Length == 16)
            .OrderByDescending(p => p.Quality)
            .ToList();

        // Size plus the already-indexed visual fingerprint narrows full-file hashing
        // to a very small candidate set. Previously every file sharing a common size
        // was read end-to-end, which made a library-wide scan feel like a disk check.
        var candidates = visual
            .GroupBy(p => (p.Size, p.Width, p.Height, p.VisualHash))
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToList();
        int count = 0;
        foreach (var p in candidates)
        {
            ct.ThrowIfCancellationRequested(); Pause.Wait(ct);
            try
            {
                CheckUnchanged(p);
                if (p.Hash.Length != 64)
                {
                    p.Hash = CheckedHash(p);
                    catalog.Upsert(p);
                }
            }
            catch (Exception e) { p.Hash = ""; Log("快速去重", p.Path, e); }
            count++;
            if (count % 20 == 0 || count == candidates.Count)
                progress.Report(new($"校验高度相同候选 {count:N0} / {candidates.Count:N0}", count, candidates.Count));
        }
        var exactExtra = new HashSet<string>();
        foreach (var g in candidates.Where(p => p.Hash.Length > 0).GroupBy(p => p.Hash).Where(g => g.Count() > 1))
        {
            var list = g.OrderByDescending(p => p.Favorite).ThenBy(p => p.Path.Length).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToList();
            groups.Add(new(Guid.NewGuid().ToString("N"), "exact", list, list[0].Id));
            foreach (var p in list.Skip(1)) exactExtra.Add(p.Id);
        }

        // Similar-photo screening uses the indexed 64-bit visual fingerprint only.
        // Thumbnails are no longer decoded repeatedly inside this library-wide pass;
        // the user reviews and compares candidates before anything is moved.
        var buckets = new Dictionary<long, List<Photo>>();
        var nearGroups = new Dictionary<string, List<Photo>>();
        visual = visual.Where(p => !exactExtra.Contains(p.Id)).ToList();
        count = 0;
        int visitStamp = 0;
        foreach (var p in visual)
        {
            ct.ThrowIfCancellationRequested(); Pause.Wait(ct);
            var hash = p.VisualHashBits;
            Photo? match = null;
            int currentVisit = ++visitStamp;
            for (int segment = 0; segment < 7 && match == null; segment++)
            {
                long key = ((long)segment << 32) | (long)VisualSegment(hash, segment);
                if (!buckets.TryGetValue(key, out var bucket)) continue;
                foreach (var candidate in bucket)
                {
                    if (candidate.DuplicateVisit == currentVisit) continue;
                    candidate.DuplicateVisit = currentVisit;
                    if (p.Hash.Length > 0 && p.Hash == candidate.Hash) continue;
                    if (BitOperations.PopCount(hash ^ candidate.VisualHashBits) > 6) continue;
                    if (Math.Abs((double)p.Width / Math.Max(1, p.Height) - (double)candidate.Width / Math.Max(1, candidate.Height)) > .04) continue;
                    match = candidate;
                    break;
                }
            }
            if (match != null) { if (!nearGroups.TryGetValue(match.Id, out var list)) nearGroups[match.Id] = list = [match]; list.Add(p); }
            else
            {
                for (int segment = 0; segment < 7; segment++)
                {
                    long key = ((long)segment << 32) | (long)VisualSegment(hash, segment);
                    if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = [];
                    bucket.Add(p);
                }
            }
            if (++count % 250 == 0 || count == visual.Count) progress.Report(new($"匹配近似候选 {count:N0} / {visual.Count:N0}", count, visual.Count));
        }
        foreach (var list in nearGroups.Values) groups.Add(new(Guid.NewGuid().ToString("N"), "similar", list, list[0].Id));
        ct.ThrowIfCancellationRequested(); catalog.SaveGroups(groups); return groups;
    }
    static int VisualSegment(ulong hash, int segment)
    {
        int shift = segment * 9;
        int width = segment == 6 ? 10 : 9;
        return (int)((hash >> shift) & ((1UL << width) - 1));
    }
    public static void CheckUnchanged(Photo p)
    {
        NoLinks(p.Path);
        var fi = new FileInfo(p.Path);
        if (!fi.Exists || fi.Length != p.Size || fi.LastWriteTimeUtc.Ticks != p.Modified) throw new IOException("文件已被外部修改或移动，请先重新扫描：" + p.Name);
    }
    public static string HashFile(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static string CheckedHash(Photo p)
    {
        CheckUnchanged(p); var hash = HashFile(p.Path); CheckUnchanged(p);
        if (p.Hash.Length > 0 && p.Hash != hash) throw new IOException("文件内容与上次校验不一致，请重新扫描：" + p.Name);
        return hash;
    }
    public static void Log(string operation, string path, Exception e)
    {
        lock (typeof(LibraryService)) File.AppendAllText(Path.Combine(AppPaths.Data, "errors.log"), $"{DateTime.Now:O}\t{operation}\t{path}\t{e.Message}{Environment.NewLine}");
    }
}
