using System.Text.Json;

namespace Shiguang;

public static class PackageTests
{
    public static int Run()
    {
        var data = AppPaths.Data; var excluded = AppPaths.Excluded;
        var checks = new List<string>(); string? failure = null;
        void Check(bool pass, string name) { if (!pass) throw new IOException(name); checks.Add(name); }
        var ct = CancellationToken.None; var progress = new Progress<JobProgress>();
        try
        {
            var source = data + "-source"; var exports = data + "-exports";
            Directory.CreateDirectory(source); Directory.CreateDirectory(exports);
            var originals = new Dictionary<string, string>(); string album; string firstId; string trashId; string package; string metadata;
            using (var catalog = new Catalog())
            {
                var photos = new List<Photo>();
                for (int i = 0; i < 6; i++)
                {
                    var path = Path.Combine(source, i % 2 == 0 ? "子目录" : "其他", $"照片 {i}.jpg"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); SelfTest.CreateImage(path, i + 1, true);
                    var p = Imaging.Read(path, source); catalog.Upsert(p); photos.Add(p); originals[path] = LibraryService.HashFile(path);
                }
                catalog.AddRoot(source); album = catalog.CreateAlbum("家庭回忆"); catalog.AddToAlbum(album, photos.Take(3));
                catalog.SetTags([photos[0]], "旅行 家人"); catalog.Favorite([photos[0]], true); firstId = photos[0].Id;
                new FileOrganizer(catalog).Trash([photos[5]], null, progress, ct); trashId = photos[5].Id;
                var trash = catalog.ById(trashId)!; originals.Remove(photos[5].Path); // Imaging.Read returned a separate snapshot; original path is in trash metadata.
                originals.Remove(trash.OriginalPath); originals[trash.Path] = LibraryService.HashFile(trash.Path);
                var before = catalog.All().Select(p => (p.Id, p.Path, p.Root, p.Status)).ToArray();
                WindowBehaviorSettings.SetCloseToTray(false);
                metadata = LibraryPackage.Export(catalog, exports, false, progress, ct);
                package = LibraryPackage.Export(catalog, exports, true, progress, ct);
                Check(catalog.All().Select(p => (p.Id, p.Path, p.Root, p.Status)).SequenceEqual(before), "export leaves every source database path and state unchanged");
                Check(originals.All(p => File.Exists(p.Key) && LibraryService.HashFile(p.Key) == p.Value), "export preserves every source file byte-for-byte");
                Check(!Directory.Exists(Path.Combine(metadata, "Media")), "metadata-only backup contains no original media");
                using (var copy = new NativeDb(Path.Combine(metadata, "library.sqlite"), true)) Check(copy.Query("SELECT path FROM photos WHERE id=?", firstId)[0][0] == photos[0].Path, "metadata backup retains original source connections");
                bool rejected = false;
                try { LibraryPackage.Export(catalog, data, true, progress, ct); } catch (IOException) { rejected = true; }
                Check(rejected, "backup inside live data folder is rejected");
                catalog.MarkMissing(firstId); string offline = photos[0].Path + ".offline"; File.Move(photos[0].Path, offline);
                rejected = false; try { LibraryPackage.Export(catalog, exports, true, progress, ct); } catch (IOException) { rejected = true; }
                File.Move(offline, photos[0].Path); var restored = catalog.ById(firstId)!; restored.Status = "active"; catalog.Upsert(restored);
                Check(rejected, "complete backup rejects missing originals rather than silently omitting them");
            }
            LibraryPackage.Validate(package);
            // A second computer uses a different absolute base directory.
            string moved = Path.Combine(exports, "换机后-不同路径"); Directory.Move(package, moved);
            AppPaths.Init(moved, excluded); LibraryPackage.RelocateIfNeeded(moved);
            Check(!WindowBehaviorSettings.CloseToTray, "window close preference survives a complete library migration");
            using (var restored = new Catalog())
            {
                Check(restored.All().Count == 6 && restored.All().All(p => File.Exists(p.Path) && AppPaths.Within(p.Path, moved)), "relocated full library resolves every original including trash");
                Check(restored.Albums().Single(a => a.Id == album).Count == 3, "cross-computer open retains album IDs and memberships");
                Check(restored.ById(firstId) is { Favorite: true, Tags: "旅行 家人" }, "favorites and tags survive complete migration");
                Check(restored.Trashed().Single().Id == trashId, "recycle-bin state survives migration");
                var trash = restored.ById(trashId)!; new FileOrganizer(restored).Restore([trash], progress, ct);
                Check(restored.ById(trashId) is { Status: "active" } p && File.Exists(p.Path) && AppPaths.Within(p.Path, moved), "recovery after migration writes only to the new library");
                restored.SetTags([restored.ById(firstId)!], "迁移后修改");
                // New external source after export is preserved when the portable folder moves again.
                var ext = restored.ById(firstId)!; var external = new Photo { Id = Guid.NewGuid().ToString("N"), Path = originals.Keys.First(), Root = source, Taken = ext.Taken, Size = ext.Size };
                restored.Upsert(external); restored.AddRoot(source);
            }
            var movedAgain = Path.Combine(exports, "再次移动"); Directory.Move(moved, movedAgain);
            AppPaths.Init(movedAgain, excluded); LibraryPackage.RelocateIfNeeded(movedAgain);
            using (var restored = new Catalog())
            {
                Check(restored.ById(firstId)?.Tags == "迁移后修改", "later edits survive a second relocation");
                Check(restored.ById(trashId) is { Status: "active", OriginalPath: "" } p && File.Exists(p.Path), "relocation uses current paths instead of stale export-time trash paths");
                Check(restored.Roots.Contains(source), "external sources added later remain connected");
                var internalRoot = restored.ById(firstId)!.Root; var reconnected = Path.Combine(exports, "来源换盘");
                Directory.CreateDirectory(reconnected);
                foreach (var item in restored.All().Where(p => p.Root == internalRoot)) { var dest = Path.Combine(reconnected, Path.GetRelativePath(internalRoot, item.Path)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(item.Path, dest); }
                int count = restored.RelinkRoot(internalRoot, reconnected);
                Check(count == 6 && restored.ById(firstId)!.Root == reconnected && restored.Albums().Single(a => a.Id == album).Count == 3, "source reconnection preserves IDs and albums");
            }
            Check(originals.All(p => File.Exists(p.Key) && LibraryService.HashFile(p.Key) == p.Value), "source files remain unchanged after all migration and recovery tests");
            // Reject path traversal in manifests before any database writes.
            string marker = Path.Combine(movedAgain, "时光媒体库.json"); var manifest = JsonSerializer.Deserialize<LibraryPackage.Manifest>(File.ReadAllText(marker))!;
            File.WriteAllText(marker, JsonSerializer.Serialize(manifest with { Files = [new("bad", "../outside.jpg", "Media", "", "")] }));
            bool unsafeRejected = false; try { LibraryPackage.Validate(movedAgain); } catch (IOException) { unsafeRejected = true; }
            Check(unsafeRejected, "untrusted manifest cannot reference paths outside its Media folder");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            AppPaths.Init(metadata, excluded);
            using (var catalog = new Catalog())
            {
                bool stopped = false; try { LibraryPackage.Export(catalog, exports, false, progress, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
                Check(stopped && Directory.GetDirectories(exports, "*.partial").Any(), "canceled export remains explicitly incomplete");
                var incomplete = Directory.GetDirectories(exports, "*.partial").First(); bool refused = false;
                try { LibraryPackage.Validate(incomplete); } catch (IOException) { refused = true; }
                Check(refused, "incomplete export cannot be opened as a usable backup");
            }
            var fixtures = new List<Photo>();
            for (int i = 0; i < 1000; i++) fixtures.Add(new Photo { Width = i % 4 == 0 ? 800 : i % 4 == 1 ? 3600 : 1200, Height = i % 4 == 0 ? 1600 : 900, Taken = "2020-01-01 12:00:00" });
            foreach (double width in new[] { 400d, 790d, 1200d })
            {
                var rows = GalleryLayout.Build(fixtures, width, false);
                Check(rows.SelectMany(r => r.Items).Count() == fixtures.Count && rows.All(r => r.Items.Sum(i => i.Width + GalleryLayout.Gap) <= width + .01), $"mixed-aspect layout stays within {width} DIP without losing files");
                Check(rows.SelectMany(r => r.Items).All(i => Math.Abs((i.Width - 4) / i.Height - (double)i.Photo.Width / i.Photo.Height) < .001), $"layout preserves image aspect ratios at {width} DIP");
                Check(rows.Take(rows.Count - 1).All(r => Math.Abs(r.Items.Sum(i => i.Width + GalleryLayout.Gap) - width) < .01), $"complete rows fill the available {width} DIP");
            }
        }
        catch (Exception e) { failure = e.ToString(); }
        finally { AppPaths.Init(data, excluded); }
        File.WriteAllText(Path.Combine(data, "package-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
}
