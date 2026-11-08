using System.Text.Json;

namespace Shiguang;
public static class MigrationTest
{
    public static int Run()
    {
        string? failure = null; var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new IOException(name); checks.Add(name); }
        try
        {
            var file = Path.Combine(AppPaths.Data, "library.sqlite"); if (File.Exists(file)) throw new IOException("迁移测试需要空目录");
            var original = Path.Combine(AppPaths.Data, "legacy-original.jpg"); File.WriteAllText(original, "synthetic legacy original; migration must not touch it"); var hash = LibraryService.HashFile(original);
            using (var db = new NativeDb(file))
            {
                db.Run("CREATE TABLE photos(id TEXT PRIMARY KEY,path TEXT COLLATE NOCASE UNIQUE,root TEXT,size INTEGER,modified INTEGER,taken TEXT,date_source TEXT,width INTEGER,height INTEGER,hash TEXT,visual_hash TEXT,sharpness REAL,brightness REAL,error TEXT,status TEXT,original_path TEXT,trash_date TEXT,favorite INTEGER)");
                db.Run("INSERT INTO photos VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)", "old-id", original, AppPaths.Data, new FileInfo(original).Length, File.GetLastWriteTimeUtc(original).Ticks, "2015-01-02 03:04:05", "拍摄时间", 6000, 1000, "", "", 0, 0, "", "active", "", "", 1);
                db.Run("CREATE TABLE albums(id TEXT PRIMARY KEY,name TEXT)"); db.Run("INSERT INTO albums VALUES('album-old','旧相册')");
                db.Run("CREATE TABLE album_photos(album_id TEXT REFERENCES albums(id),photo_id TEXT REFERENCES photos(id),PRIMARY KEY(album_id,photo_id))"); db.Run("INSERT INTO album_photos VALUES('album-old','old-id')");
            }
            using (var catalog = new Catalog())
            {
                Check(catalog.ById("old-id") is { Favorite: true, ContentKind: "panorama" }, "legacy IDs, favorites and metadata survive schema upgrade");
                Check(catalog.ById("old-id")!.Path == original, "migration leaves original source path unchanged");
                Check(catalog.Browse(new("album", "album-old")).Total == 1, "legacy album references remain attached");
                Check(catalog.Browse(new(Year: "2015", Month: "01", Day: "02")).Total == 1, "legacy capture date supports the new timeline");
            }
            Check(Directory.GetFiles(Path.Combine(AppPaths.Data, "backups"), "*.sqlite").Length == 1, "upgrade creates a standalone pre-migration backup");
            Check(LibraryService.HashFile(original) == hash, "migration does not modify original bytes");
        }
        catch (Exception e) { failure = e.ToString(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "migration-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
}
