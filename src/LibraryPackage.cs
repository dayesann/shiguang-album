using System.Security.Cryptography;
using System.Text.Json;

namespace Shiguang;

public static class LibraryPackage
{
    public sealed record Entry(string Id, string Path, string Root, string OriginalPath, string Hash);
    public sealed record Manifest(int Version, bool IncludesMedia, string Location, string Created, List<Entry> Files);
    const string Marker = "时光媒体库.json";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Export(Catalog catalog, string parent, bool includeMedia, IProgress<JobProgress> progress, CancellationToken ct)
    {
        parent = Path.GetFullPath(parent); LibraryService.NoLinks(parent);
        if (!Directory.Exists(parent)) throw new IOException("备份目标目录不存在。");
        if (AppPaths.Within(parent, AppPaths.Data)) throw new IOException("请将备份保存到当前媒体库之外的位置。");
        if (catalog.Operations().Count > 0) throw new IOException("请先处理未完成的文件操作，再备份。");
        var photos = catalog.All();
        if (includeMedia && photos.Any(p => !File.Exists(p.Path))) throw new IOException("部分原始媒体离线。请先重新连接来源目录，完整迁移不会跳过缺失文件。");
        long bytes = includeMedia ? photos.Sum(p => new FileInfo(p.Path).Length) : 0;
        var drive = new DriveInfo(Path.GetPathRoot(parent)!);
        if (drive.IsReady && drive.AvailableFreeSpace < bytes + 64L * 1024 * 1024) throw new IOException("目标磁盘空间不足。");
        var name = (includeMedia ? "完整媒体库-" : "相册资料-") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        var final = Path.Combine(parent, name); var staging = final + ".partial";
        Directory.CreateDirectory(staging);
        // An interrupted export remains clearly marked, never mistaken for a usable backup.
        File.WriteAllText(Path.Combine(staging, "未完成.txt"), "备份未完成，请勿作为恢复来源。可重新导出。");
        var entries = new List<Entry>();
        catalog.Backup(Path.Combine(staging, "library.sqlite"));
        var roots = photos.Select(p => p.Root).Distinct(StringComparer.OrdinalIgnoreCase).Select((r, i) => (r, target: Path.Combine("Media", $"Source-{i + 1:D3}"))).ToDictionary(x => x.r, x => x.target, StringComparer.OrdinalIgnoreCase);
        int done = 0;
        foreach (var photo in photos)
        {
            ct.ThrowIfCancellationRequested();
            if (includeMedia)
            {
                var relative = AppPaths.Within(photo.Path, photo.Root) ? Path.GetRelativePath(photo.Root, photo.Path) : Path.Combine("Files", photo.Id + Path.GetExtension(photo.Path));
                var destination = Path.Combine(roots[photo.Root], relative);
                var original = photo.OriginalPath.Length == 0 ? "" : Path.Combine(roots[photo.Root], AppPaths.Within(photo.OriginalPath, photo.Root) ? Path.GetRelativePath(photo.Root, photo.OriginalPath) : Path.Combine("Files", photo.Name));
                progress.Report(new($"复制并校验 {done + 1:N0} / {photos.Count:N0} · {photo.Name}", done, photos.Count));
                var hash = CopyVerified(photo.Path, Resolve(staging, destination), ct);
                entries.Add(new(photo.Id, destination, roots[photo.Root], original, hash));
            }
            var thumb = photo.ThumbPath;
            if (File.Exists(thumb))
            {
                Directory.CreateDirectory(Path.Combine(staging, "thumbnails"));
                File.Copy(thumb, Path.Combine(staging, "thumbnails", photo.Id + ".jpg"));
            }
            done++;
        }
        ct.ThrowIfCancellationRequested();
        var settings = Path.Combine(AppPaths.Data, "settings.json");
        if (File.Exists(settings)) File.Copy(settings, Path.Combine(staging, "settings.json"));
        var windowPreferences = Path.Combine(AppPaths.Data, "window-preferences.json");
        if (File.Exists(windowPreferences)) File.Copy(windowPreferences, Path.Combine(staging, "window-preferences.json"));
        var manifest = new Manifest(1, includeMedia, final, DateTime.UtcNow.ToString("O"), entries);
        using (var db = new NativeDb(Path.Combine(staging, "library.sqlite")))
        {
            db.Run("DELETE FROM operations");
            if (includeMedia) Rebase(db, final, entries);
            if (db.Query("PRAGMA integrity_check")[0][0] != "ok") throw new IOException("备份数据库校验未通过。");
        }
        File.WriteAllText(Path.Combine(staging, Marker), JsonSerializer.Serialize(manifest, Json));
        File.WriteAllText(Path.Combine(staging, "如何恢复.txt"), includeMedia ? "复制整个文件夹到新电脑。安装时光相册后，在设置 → 备份与迁移 → 打开媒体库中选择本文件夹内的 library.sqlite。无需重新导入。照片、视频、标签、收藏、相册和回收站已包含。" : "此备份只含相册资料、标签、索引与缩略图，不含原始照片和视频。请单独备份来源目录；打开本媒体库后，可在设置 → 来源目录中重新连接原图位置。");
        File.Delete(Path.Combine(staging, "未完成.txt"));
        Directory.Move(staging, final);
        return final;
    }
    static string CopyVerified(string source, string destination, CancellationToken ct)
    {
        LibraryService.NoLinks(source);
        var before = new FileInfo(source); long size = before.Length; var modified = before.LastWriteTimeUtc;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] buffer = new byte[1024 * 1024]; int read;
            while ((read = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); output.Write(buffer, 0, read); }
            output.Flush(true);
        }
        string copied = Convert.ToHexString(hash.GetHashAndReset());
        var after = new FileInfo(source);
        if (after.Length != size || after.LastWriteTimeUtc != modified || !copied.Equals(LibraryService.HashFile(destination), StringComparison.OrdinalIgnoreCase)) throw new IOException("备份校验失败：" + Path.GetFileName(source));
        File.SetLastWriteTimeUtc(destination, modified); return copied;
    }
    static string Resolve(string folder, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new IOException("媒体库含无效路径。");
        var target = Path.GetFullPath(Path.Combine(folder, relative));
        if (!AppPaths.Within(target, Path.Combine(folder, "Media"))) throw new IOException("媒体库路径超出媒体目录。");
        return target;
    }
    static Manifest? Read(string folder)
    {
        if (folder.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(folder, "未完成.txt"))) throw new IOException("这份备份尚未完成，请选择已完成的媒体库。");
        var marker = Path.Combine(folder, Marker);
        if (!File.Exists(marker)) return null;
        if (new FileInfo(marker).Length > 100 * 1024 * 1024) throw new IOException("媒体库清单过大。");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(marker)) ?? throw new IOException("媒体库清单损坏。");
        if (manifest.Version != 1 || manifest.Files == null) throw new IOException("不支持此媒体库版本。");
        if (manifest.IncludesMedia)
            foreach (var item in manifest.Files)
            {
                if (string.IsNullOrWhiteSpace(item.Id)) throw new IOException("媒体库清单缺少项目标识。");
                Resolve(folder, item.Path); Resolve(folder, item.Root); if (item.OriginalPath.Length > 0) Resolve(folder, item.OriginalPath);
            }
        return manifest;
    }
    public static void Validate(string folder)
    {
        if (!File.Exists(Path.Combine(folder, "library.sqlite"))) throw new IOException("没有找到媒体库数据库。");
        Read(folder);
        using var db = new NativeDb(Path.Combine(folder, "library.sqlite"), true);
        if (db.Query("PRAGMA quick_check")[0][0] != "ok" || !db.Query("PRAGMA table_info(photos)").Any(r => r[1] == "path")) throw new IOException("不是有效的时光媒体库。");
    }
    public static void RelocateIfNeeded(string folder)
    {
        var manifest = Read(folder);
        if (manifest is not { IncludesMedia: true } || folder.Equals(manifest.Location, StringComparison.OrdinalIgnoreCase)) return;
        using (var db = new NativeDb(Path.Combine(folder, "library.sqlite")))
        {
            if (db.Query("SELECT id FROM operations WHERE state NOT IN ('Completed','Cancelled','Undone')").Count > 0) throw new IOException("存在未完成文件操作，请先在原位置恢复任务。");
            string oldMedia = Path.Combine(manifest.Location, "Media");
            string Remap(string path) => path.Length > 0 && AppPaths.Within(path, oldMedia) ? Resolve(folder, Path.GetRelativePath(manifest.Location, path)) : path;
            // Read current paths: albums, deletions and additions made after export must survive relocation.
            var current = db.Query("SELECT id,path,root,original_path FROM photos");
            var roots = db.Query("SELECT path FROM roots");
            db.Transaction(() =>
            {
                foreach (var row in current) db.Run("UPDATE photos SET path=? WHERE id=?", "relocating:" + row[0], row[0]);
                foreach (var row in current) db.Run("UPDATE photos SET path=?,root=?,original_path=? WHERE id=?", Remap(row[1]), Remap(row[2]), Remap(row[3]), row[0]);
                db.Run("DELETE FROM roots"); foreach (var row in roots) db.Run("INSERT OR IGNORE INTO roots VALUES(?)", Remap(row[0]));
                db.Run("DELETE FROM operations");
            });
        }
        var temporary = Path.Combine(folder, Marker + ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest with { Location = folder }, Json));
        File.Move(temporary, Path.Combine(folder, Marker), true);
    }
    static void Rebase(NativeDb db, string folder, IReadOnlyList<Entry> entries) => db.Transaction(() =>
    {
        db.Run("DELETE FROM roots");
        // Use a unique intermediate namespace so changed drive roots cannot collide during updates.
        foreach (var item in entries) db.Run("UPDATE photos SET path=? WHERE id=?", "relocating:" + item.Id, item.Id);
        foreach (var item in entries)
        {
            string root = Resolve(folder, item.Root);
            db.Run("INSERT OR IGNORE INTO roots VALUES(?)", root);
            db.Run("UPDATE photos SET path=?,root=?,original_path=?,hash=? WHERE id=?", Resolve(folder, item.Path), root, item.OriginalPath.Length > 0 ? Resolve(folder, item.OriginalPath) : "", item.Hash, item.Id);
        }
        db.Run("DELETE FROM operations");
    });
}
