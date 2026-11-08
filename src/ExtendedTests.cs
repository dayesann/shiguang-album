using System.Diagnostics;
using System.Text.Json;

namespace Shiguang;

public static class ExtendedTests
{
    public static int CrossVolume(string targetRoot)
    {
        var checks = new List<string>(); string? failure = null;
        void Check(bool result, string name) { if (!result) throw new IOException(name); checks.Add(name); }
        try
        {
            if (Directory.Exists(targetRoot)) throw new IOException("跨盘测试目标必须尚不存在");
            Directory.CreateDirectory(targetRoot);
            var sourceRoot = Path.Combine(Path.GetDirectoryName(AppPaths.Data)!, "source"); Directory.CreateDirectory(sourceRoot);
            var source = Path.Combine(sourceRoot, "cross.jpg"); SelfTest.CreateImage(source, 1, true);
            Check(!Path.GetPathRoot(source)!.Equals(Path.GetPathRoot(targetRoot), StringComparison.OrdinalIgnoreCase), "test uses two distinct disk volumes");
            using var catalog = new Catalog(); var fs = new FileOrganizer(catalog);
            var p = Imaging.Read(source, sourceRoot); catalog.Upsert(p); var hash = LibraryService.HashFile(source);
            var target = Path.Combine(targetRoot, "2024", "06", "cross.jpg");
            fs.Transfer(p, target, "archive", "cross", targetRoot, CancellationToken.None);
            Check(!File.Exists(source) && File.Exists(target) && LibraryService.HashFile(target) == hash, "cross-volume move validates copy before deleting source");
            Check(catalog.ById(p.Id)!.Path == target, "cross-volume catalog path committed");
            fs.UndoLastArchive(new Progress<JobProgress>(), CancellationToken.None);
            Check(File.Exists(source) && !File.Exists(target) && LibraryService.HashFile(source) == hash, "cross-volume archive can be undone without changing bytes");
            p = catalog.ById(p.Id)!;
            var pendingTarget = Path.Combine(targetRoot, "resumed.jpg");
            var op = new FileOperation(Guid.NewGuid().ToString("N"), "partial", "archive", p.Id, source, pendingTarget, hash, "NeedsReview", "simulated interrupted copy", DateTime.UtcNow.ToString("O"), sourceRoot, targetRoot);
            catalog.Prepare(op); var partial = pendingTarget + ".shiguang-" + op.Id + ".partial"; File.WriteAllBytes(partial, File.ReadAllBytes(source).Take(2000).ToArray());
            Check(fs.Reconcile() == 0 && File.Exists(source), "startup leaves an interrupted copy's original intact");
            fs.ResumePending(new Progress<JobProgress>(), CancellationToken.None);
            Check(!File.Exists(partial) && File.Exists(pendingTarget) && LibraryService.HashFile(pendingTarget) == hash && !File.Exists(source), "explicit recovery restarts partial copy and verifies complete bytes");
            fs.UndoLastArchive(new Progress<JobProgress>(), CancellationToken.None);
            p = catalog.ById(p.Id)!;
            var conflict = op with { Id = Guid.NewGuid().ToString("N"), Batch = "conflict", Target = Path.Combine(targetRoot, "conflict.jpg") };
            catalog.Prepare(conflict); File.WriteAllText(conflict.Target, "different target contents");
            bool rejected = false; try { fs.ResumePending(new Progress<JobProgress>(), CancellationToken.None); } catch (IOException) { rejected = true; }
            Check(rejected && File.Exists(source) && File.ReadAllText(conflict.Target) == "different target contents", "recovery refuses to delete either file when contents differ");
            File.Delete(conflict.Target); fs.CancelUntouched();
            Check(catalog.Operations().Count == 0, "cross-volume tests finish with a resolved journal");
        }
        catch (Exception e) { failure = e.ToString(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "cross-volume-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
    public static int Scale()
    {
        string? failure = null; var timing = new Dictionary<string, double>();
        try
        {
            using var catalog = new Catalog();
            if (catalog.Count() > 0) throw new IOException("索引规模测试需要空数据库");
            var watch = Stopwatch.StartNew();
            catalog.UpsertMany(Enumerable.Range(0, 100000).Select(i => new Photo
            {
                Id = i.ToString("D32"), Path = Path.Combine(AppPaths.Data, "virtual-fixtures", $"IMG_{i:D6}.jpg"), Root = AppPaths.Data,
                Taken = new DateTime(2015, 1, 1).AddHours(i).ToString("yyyy-MM-dd HH:mm:ss"), DateSource = "拍摄时间", Width = 4000, Height = 3000, Size = 4500000,
                VisualHash = ((ulong)i * 0x9E3779B97F4A7C15UL).ToString("X16"), Sharpness = 100 + i % 100, Brightness = 110 + i % 30
            }));
            timing["Insert100000Seconds"] = watch.Elapsed.TotalSeconds;
            watch.Restart(); var first = catalog.Page("all", null, "全部年份", "全部月份", "", 0, 300); timing["FirstPageMs"] = watch.Elapsed.TotalMilliseconds;
            if (first.Total != 100000 || first.Items.Count != 300) throw new Exception("首页分页数量错误");
            watch.Restart(); var last = catalog.Page("all", null, "全部年份", "全部月份", "", 99700, 300); timing["LastPageMs"] = watch.Elapsed.TotalMilliseconds;
            if (last.Items.Count != 300 || last.Items.Any(p => first.Items.Any(x => x.Id == p.Id))) throw new Exception("末页分页结果错误");
            watch.Restart(); var filtered = catalog.Page("all", null, "2024", "06", "", 0, 300); timing["FilteredPageMs"] = watch.Elapsed.TotalMilliseconds;
            if (filtered.Total < 600 || filtered.Items.Count != 300 || filtered.Items.Any(p => !p.Taken.StartsWith("2024-06"))) throw new Exception("日期筛选错误");
            watch.Restart(); var duplicateGroups = new LibraryService(catalog).FindDuplicates(new Progress<JobProgress>(), CancellationToken.None);
            timing["CachedDuplicateIndexSeconds"] = watch.Elapsed.TotalSeconds;
            if (duplicateGroups.Count > 50) throw new Exception("合成随机指纹产生了过多近似候选");
            if (timing["CachedDuplicateIndexSeconds"] > 15) throw new Exception("缓存查重超过 15 秒预算");
            watch.Restart(); var cachedGroups = catalog.CachedDuplicateGroups(); timing["CachedResultLoadMs"] = watch.Elapsed.TotalMilliseconds;
            if (!catalog.HasDuplicateIndex() || cachedGroups.Count != duplicateGroups.Count || timing["CachedResultLoadMs"] > 1000) throw new Exception("重复索引回读未达到即时加载预算");
        }
        catch (Exception e) { failure = e.ToString(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "scale-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Rows = 100000, SyntheticMetadataOnly = true, Timing = timing, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
}
