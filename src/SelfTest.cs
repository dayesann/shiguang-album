using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shiguang;

public static class SelfTest
{
    public static int Run(string root)
    {
        var checks = new List<string>(); string? failure = null;
        string report = Path.Combine(AppPaths.Data, "self-test-results.json");
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); checks.Add(name); }
        void Throws(Action action, string name) { bool rejected = false; try { action(); } catch (IOException) { rejected = true; } Check(rejected, name); }
        try
        {
            root = Path.GetFullPath(root);
            if (Directory.Exists(root)) throw new IOException("测试目录必须尚不存在，避免覆盖已有文件");
            Directory.CreateDirectory(root);
            string source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
            var shot = Path.Combine(source, "IMG_20240615_100000.jpg");
            CreateImage(shot, 1, true);
            string copy = Path.Combine(source, "副本.jpg"); File.Copy(shot, copy);
            string compressed = Path.Combine(source, "压缩版.jpg");
            SaveJpeg(Imaging.Decode(shot, 320), compressed, 45, "2024:06:15 10:00:00");
            string filename = Path.Combine(source, "IMG_20231220_090000.png"); CreateImage(filename, 2, false);
            string other = Path.Combine(source, "IMG_20240615_110000.jpg"); CreateImage(other, 3, true);
            string rotated = Path.Combine(source, "旋转照片.jpg"); CreateImage(rotated, 4, true, 6);
            string broken = Path.Combine(source, "broken.heic"); File.WriteAllText(broken, "synthetic undecodable HEIC fixture");
            var before = Directory.GetFiles(source).ToDictionary(p => p, LibraryService.HashFile);
            using var catalog = new Catalog(); var service = new LibraryService(catalog); var fs = new FileOrganizer(catalog);
            var progress = new Progress<JobProgress>(); var ct = CancellationToken.None;
            var scan = service.Scan(source, progress, ct);
            Check(scan.Added == 7 && catalog.Count() == 7, "scan indexes 7 files including unsupported HEIC");
            Check(scan.Failed == 1, "unsupported HEIC is recorded, never treated as low quality");
            Check(before.All(k => File.Exists(k.Key) && LibraryService.HashFile(k.Key) == k.Value), "scanning leaves every original byte unchanged");
            Check(catalog.ByPath(shot)!.DateSource == "拍摄时间", "EXIF capture date parsed");
            Check(catalog.ByPath(filename)!.DateSource == "文件名推断", "filename dates explicitly marked inferred");
            Check(catalog.ByPath(rotated) is { Width: 480, Height: 640 }, "EXIF orientation swaps dimensions");
            var again = service.Scan(source, progress, ct); Check(again.Skipped == 6 && again.Added == 0 && again.Updated == 1, "incremental rescan reuses valid thumbnails and retries failed codecs");
            string album = catalog.CreateAlbum("测试旅行");
            catalog.AddToAlbum(album, [catalog.ByPath(copy)!, catalog.ByPath(filename)!]);
            Check(Directory.GetFiles(source).Length == 7, "creating albums adds no photo copies");
            Check(catalog.Page("album", album, "全部年份", "全部月份", "", 0, 300).Total == 2, "virtual album links resolve");
            Check(catalog.Page("all", null, "2024", "06", "", 0, 2) is { Total: 5, Items.Count: 2 }, "date filtering and database paging");
            Check(catalog.Page("all", null, "全部年份", "全部月份", "IMG_", 0, 300).Total == 3, "search escapes wildcard underscore");
            var groups = service.FindDuplicates(progress, ct);
            var exact = groups.Single(g => g.Kind == "exact");
            Check(exact.Photos.Count == 2, "exact duplicates require full content equality");
            Check(groups.Any(g => g.Kind == "similar" && g.Photos.Any(p => p.Path == compressed)), "recompressed reduced-resolution version found as similar");
            Check(groups.All(g => g.Photos.All(p => p.Path != broken)), "decode errors excluded from visual quality grouping");
            Check(catalog.HasDuplicateIndex() && catalog.CachedDuplicateGroups().Count == groups.Count, "duplicate results persist in a reusable index");
            var copyPhoto = catalog.ByPath(copy)!; var original = catalog.ByPath(shot)!;
            fs.Trash([copyPhoto], new() { [copyPhoto.Id] = original.Id }, progress, ct);
            Check(!File.Exists(copy) && catalog.ById(copyPhoto.Id)!.Status == "trash", "trash moves original into recoverable quarantine");
            Check(catalog.Page("album", album, "全部年份", "全部月份", "", 0, 300).Items.Any(p => p.Id == original.Id), "duplicate cleanup transfers album references to retained copy");
            fs.Restore([catalog.ById(copyPhoto.Id)!], progress, ct);
            Check(File.Exists(copy) && catalog.ById(copyPhoto.Id)!.Status == "active", "restore returns original and its album membership");
            string destination = Path.Combine(root, "managed"); Directory.CreateDirectory(Path.Combine(destination, "2024", "06"));
            string collision = Path.Combine(destination, "2024", "06", Path.GetFileName(shot)); File.WriteAllText(collision, "existing target must survive");
            var plan = fs.Plan(catalog.AllActive(), destination);
            Check(plan.Single(i => i.Photo.Path == shot).Destination != collision, "archive preview avoids filename collision");
            Check(plan.Single(i => i.Photo.Path == filename).Selected == false, "inferred date not selected for move by default");
            Check(plan.Single(i => i.Photo.Path == broken).Selected == false, "fallback modification date not selected by default");
            var albumBefore = catalog.Page("album", album, "全部年份", "全部月份", "", 0, 300).Items.Select(p => p.Id).Order().ToArray();
            var chosen = plan.Where(i => i.Selected).ToList(); int moved = fs.Archive(plan, destination, progress, ct);
            Check(moved == chosen.Count && chosen.All(i => File.Exists(i.Destination) && !File.Exists(i.Source)), "archive moves selected files into year/month folders");
            Check(File.ReadAllText(collision) == "existing target must survive", "existing destination never overwritten");
            Check(catalog.Page("album", album, "全部年份", "全部月份", "", 0, 300).Items.Select(p => p.Id).Order().SequenceEqual(albumBefore), "album identities survive archive moves");
            Check(fs.UndoLastArchive(progress, ct) == moved && File.Exists(shot), "last archive batch can be undone");
            original = catalog.ByPath(shot)!;
            var planned = fs.Plan([original], destination); planned[0].Selected = true;
            Directory.CreateDirectory(Path.GetDirectoryName(planned[0].Destination)!); File.WriteAllText(planned[0].Destination, "late collision");
            Throws(() => fs.Archive(planned, destination, progress, ct), "late target collision stops before any overwrite");
            Check(File.Exists(shot) && catalog.Operations().Count == 0, "late collision leaves original and no pending mutation");
            var bytes = File.ReadAllBytes(shot); var timestamp = File.GetLastWriteTimeUtc(shot);
            var changed = (byte[])bytes.Clone(); changed[^8] ^= 1; File.WriteAllBytes(shot, changed); File.SetLastWriteTimeUtc(shot, timestamp);
            Throws(() => fs.Trash([original], null, progress, ct), "content change detected even when size and timestamp are preserved");
            Check(File.Exists(shot), "externally modified photo is not deleted");
            File.WriteAllBytes(shot, bytes); File.SetLastWriteTimeUtc(shot, timestamp);
            var target = Path.Combine(root, "recovery", "2024", "06", "recovered.jpg"); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var op = new FileOperation(Guid.NewGuid().ToString("N"), "recovery-batch", "archive", original.Id, shot, target, LibraryService.CheckedHash(original), "Prepared", "", DateTime.UtcNow.ToString("O"), source, Path.Combine(root, "recovery"));
            catalog.Prepare(op); File.Move(shot, target);
            Check(fs.Reconcile() == 1 && catalog.ById(original.Id)!.Path == target, "crash after file move reconciles stale catalog");
            Check(fs.UndoLastArchive(progress, ct) == 1 && File.Exists(shot), "recovered archive can be undone");
            original = catalog.ByPath(shot)!;
            var cancelTarget = Path.Combine(root, "not-started.jpg");
            var untouched = new FileOperation(Guid.NewGuid().ToString("N"), "cancel", "archive", original.Id, shot, cancelTarget, LibraryService.CheckedHash(original), "Prepared", "", DateTime.UtcNow.ToString("O"), source, root);
            catalog.Prepare(untouched); Check(fs.CancelUntouched() == 1 && File.Exists(shot), "untouched pending operation can be safely canceled");
            var bothTarget = Path.Combine(root, "both.jpg");
            var both = untouched with { Id = Guid.NewGuid().ToString("N"), Batch = "both", Target = bothTarget };
            catalog.Prepare(both); File.Copy(shot, bothTarget);
            Check(fs.Reconcile() == 0 && File.Exists(shot), "startup recovery never deletes a remaining original");
            Check(fs.ResumePending(progress, ct) == 1 && !File.Exists(shot) && File.Exists(bothTarget), "explicit recovery verifies two copies before completing move");
            fs.UndoLastArchive(progress, ct);
            original = catalog.ByPath(shot)!;
            string occupied = copy; fs.Trash([catalog.ByPath(copy)!], null, progress, ct); File.WriteAllText(occupied, "new occupant");
            fs.Restore([catalog.ById(copyPhoto.Id)!], progress, ct);
            Check(File.ReadAllText(occupied) == "new occupant" && catalog.ById(copyPhoto.Id)!.Path != occupied, "restore conflict creates alternate filename, never overwrites");
            var backup = Path.Combine(root, "backup.sqlite"); catalog.Backup(backup);
            using (var db = new NativeDb(backup)) Check(db.Query("PRAGMA integrity_check")[0][0] == "ok", "SQLite backup includes a consistent standalone snapshot");
            string json = Path.Combine(root, "backup.albums.json"); AlbumBackup.Export(catalog, json); var imported = AlbumBackup.Import(catalog, json);
            Check(catalog.Albums().Any(a => a.Name.EndsWith("（导入）") && a.Count > 0), "album export/import restores photo references");
            var bad = catalog.ByPath(broken)!; fs.Trash([bad], null, progress, ct); var trashed = catalog.ById(bad.Id)!;
            Check(fs.Purge([trashed], progress, ct) == 1 && !File.Exists(trashed.Path) && catalog.ById(bad.Id) == null, "explicit permanent purge removes only quarantined photo and index");
            catalog.Favorite([original], true); Check(catalog.Page("favorites", null, "全部年份", "全部月份", "", 0, 300).Total == 1, "favorite filter persists");
            catalog.SetDate([catalog.ByPath(filename)!], new DateTime(2023, 12, 20)); Check(catalog.ByPath(filename)!.DateSource == "手动确认", "manual date confirmation enables explicit archival");
            Check(catalog.Page("dates", null, "全部年份", "全部月份", "", 0, 300).Items.All(p => p.Path != filename), "confirmed dates leave the date-review queue");
            var live = catalog.ById(original.Id)!; File.Move(live.Path, live.Path + ".offline"); service.Scan(source, progress, ct);
            Check(catalog.ById(original.Id)!.Status == "missing", "confirmed externally missing files marked offline");
            Check(!catalog.HasDuplicateIndex(), "source changes invalidate stale duplicate results");
            File.Move(live.Path + ".offline", live.Path); service.Scan(source, progress, ct);
            Check(catalog.ById(original.Id)!.Status == "active", "reappearing originals reconnect to stable IDs");
            Check(catalog.Operations().Count == 0, "all completed test operations leave no unresolved journal");
        }
        catch (Exception e) { failure = e.ToString(); }
        File.WriteAllText(report, JsonSerializer.Serialize(new { Passed = failure == null, Count = checks.Count, Checks = checks, Failure = failure, FixtureRoot = root }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
    public static void CreateImage(string path, int variant, bool exif, ushort orientation = 1)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var palettes = new[] { ("#77B7E5", "#FBE9C5", "#5D8968"), ("#EEB7A4", "#F4D3BB", "#846E92"), ("#5C799B", "#D6E5ED", "#B36D62"), ("#CFBFAE", "#F1E8D3", "#60838C") };
            var colors = palettes[(variant - 1) % palettes.Length];
            Color C(string s) => (Color)ColorConverter.ConvertFromString(s);
            dc.DrawRectangle(new LinearGradientBrush(C(colors.Item1), C(colors.Item2), 90), null, new Rect(0, 0, 640, 480));
            dc.DrawEllipse(new SolidColorBrush(C("#FFF0CA")), null, new Point(variant == 2 ? 470 : 130, 110), 45, 45);
            dc.DrawEllipse(new SolidColorBrush(C(colors.Item3)), null, new Point(variant == 3 ? 180 : 490, 510), 410, 260);
            var random = new Random(variant * 73);
            for (int i = 0; i < 75; i++) { var x = random.Next(0, 640); var y = random.Next(230, 470); dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)random.Next(30, 170), (byte)random.Next(65, 150), (byte)random.Next(40, 120))), null, new Rect(x, y, random.Next(2, 9), random.Next(8, 45))); }
        }
        var bitmap = new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file); }
        else SaveJpeg(bitmap, path, 94, exif ? "2024:06:15 10:00:00" : null, orientation);
    }
    static void SaveJpeg(BitmapSource source, string path, int quality, string? taken = null, ushort orientation = 1)
    {
        var metadata = new BitmapMetadata("jpg");
        if (taken != null) metadata.SetQuery("/app1/ifd/exif/{ushort=36867}", taken);
        metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);
        var encoder = new JpegBitmapEncoder { QualityLevel = quality }; encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        using var output = File.Create(path); encoder.Save(output);
    }
}
