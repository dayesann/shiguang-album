using System.Text.Json;

namespace Shiguang;

public static class SafetyRegression
{
    public static int Run()
    {
        var checks = new List<string>(); string? failure = null;
        var data = AppPaths.Data; var excluded = AppPaths.Excluded;
        void Check(bool value, string name) { if (!value) throw new IOException(name); checks.Add(name); }
        void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (IOException) { rejected = true; } Check(rejected, name); }
        var progress = new Progress<JobProgress>(); var ct = CancellationToken.None;
        try
        {
            var root = Path.Combine(data, "fixtures"); Directory.CreateDirectory(root);
            string victim = Path.Combine(root, "victim.jpg"); File.WriteAllText(victim, "sentinel");
            foreach (string id in new[] { "../../victim", @"..\victim", @"C:\victim", "bad/name", "bad:stream", "", "a|b" })
            {
                Reject(() => _ = AppPaths.Thumb(id), "thumbnail rejects unsafe ID: " + id);
                Reject(() => _ = new Photo { Id = id }.LargePath, "large preview rejects unsafe ID: " + id);
            }
            Check(File.ReadAllText(victim) == "sentinel", "unsafe cache keys leave outside sentinel intact");
            Check(AppPaths.Within(AppPaths.Thumb("old-id_1"), Path.Combine(data, "thumbnails")), "legacy safe identifiers stay supported");
            using (var catalog = new Catalog())
            {
                Reject(() => catalog.Upsert(new Photo { Id = "../victim" }), "catalog rejects unsafe IDs before writes");
                using var db = new NativeDb(Path.Combine(data, "library.sqlite"));
                var bad = new Photo(); catalog.Upsert(bad);
                db.Run("UPDATE photos SET id=? WHERE id=?", "../../victim", bad.Id);
                Reject(() => LibraryPackage.Validate(data), "opening untrusted library rejects unsafe stored IDs");
                Reject(() => { using var invalid = new Catalog(); }, "direct catalog open also rejects unsafe stored IDs");
                db.Run("DELETE FROM photos");
            }
            AppPaths.Init(Path.Combine(root, "relink-data"), excluded);
            var oldRoot = Path.Combine(root, "old"); var newRoot = Path.Combine(root, "new");
            Directory.CreateDirectory(oldRoot); Directory.CreateDirectory(newRoot);
            string source = Path.Combine(oldRoot, "sample.jpg"), target = Path.Combine(newRoot, "sample.jpg");
            SelfTest.CreateImage(source, 1, true); File.Copy(source, target);
            using (var catalog = new Catalog())
            {
                var photo = Imaging.Read(source, oldRoot); catalog.Upsert(photo); catalog.AddRoot(oldRoot);
                var hash = LibraryService.HashFile(source); var bytes = File.ReadAllBytes(target); bytes[^8] ^= 1; File.WriteAllBytes(target, bytes);
                Reject(() => catalog.RelinkRoot(oldRoot, newRoot), "same-size different contents rejected without stored hash");
                Check(catalog.ById(photo.Id)!.Path == source, "failed relink leaves references unchanged");
                File.Copy(source, target, true);
                File.Move(source, source + ".offline");
                Reject(() => catalog.RelinkRoot(oldRoot, newRoot), "offline source without identity proof fails closed");
                File.Move(source + ".offline", source);
                PreviewWorker.Save(Imaging.Full(photo), photo.LargePath);
                Check(catalog.RelinkRoot(oldRoot, newRoot) == 1, "matching online source can establish hash and relink");
                var live = catalog.ById(photo.Id)!;
                Check(live.Hash == hash && live.PreviewState == "pending" && live.VisualHash == "", "relink persists identity and invalidates preview metadata");
                Check(!File.Exists(photo.ThumbPath) && !File.Exists(photo.LargePath), "old disk previews removed after verified relink");
                new MediaIndexer(catalog).Scan(newRoot, progress, ct);
                Check(catalog.PendingPreviews().Any(p => p.Id == photo.Id), "rescan retains relink preview regeneration request");
                PreviewPipeline.Enrich(catalog, progress, ct).GetAwaiter().GetResult();
                Check(catalog.ById(photo.Id)!.PreviewState == "ready" && File.Exists(photo.ThumbPath), "relinked preview is regenerated from verified file");
                Check(LibraryService.HashFile(source) == hash && LibraryService.HashFile(target) == hash, "relink preserves both original file contents");
            }
            AppPaths.Init(Path.Combine(root, "recovery-data"), excluded);
            using (var catalog = new Catalog())
            {
                var photo = Imaging.Read(source, oldRoot); catalog.Upsert(photo);
                var organizer = new FileOrganizer(catalog);
                string trash = Path.Combine(oldRoot, ".shiguang-trash", photo.Id + ".jpg"); Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
                var op = new FileOperation(Guid.NewGuid().ToString("N"), "test", "trash", photo.Id, source, trash, LibraryService.HashFile(source), "Prepared", "", DateTime.UtcNow.ToString("O"), oldRoot, oldRoot);
                catalog.Prepare(op); File.Move(source, trash);
                Reject(() => new MediaIndexer(catalog).Scan(oldRoot, progress, ct), "scan blocked before mutation with pending trash");
                Check(organizer.Reconcile() == 1 && catalog.Trashed().Count == 1, "finished interrupted trash reconciles into visible recycle bin");
                var restore = op with { Id = Guid.NewGuid().ToString("N"), Kind = "restore", Source = trash, Target = source };
                catalog.Prepare(restore); File.Move(trash, source);
                Reject(() => new MediaIndexer(catalog).Scan(oldRoot, progress, ct), "scan blocked during interrupted restore");
                Check(catalog.ByPath(source) == null, "blocked scan creates no conflicting new ID");
                Check(organizer.Reconcile() == 1 && catalog.ById(photo.Id)!.Path == source, "restore reconciliation preserves original ID");
                catalog.Prepare(op with { Id = Guid.NewGuid().ToString("N") });
                Check(organizer.Reconcile() == 0 && File.Exists(source), "startup reconciliation never moves a remaining source");
                Check(organizer.CancelUntouched() == 1 && catalog.Operations().Count == 0, "untouched operation cancels safely");
                Check(new MediaIndexer(catalog).Scan(oldRoot, progress, ct).Failed == 0, "scan resumes when recovery has finished");
            }
        }
        catch (Exception e) { failure = e.ToString(); }
        finally { AppPaths.Init(data, excluded); }
        File.WriteAllText(Path.Combine(data, "safety-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
}
