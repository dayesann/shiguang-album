using System.Globalization;
using System.IO;

namespace Shiguang;

public sealed partial class Catalog : IDisposable
{
    readonly NativeDb db;
    readonly bool hasAlbumCovers;
    public Catalog(bool readOnly = false)
    {
        db = new NativeDb(Path.Combine(AppPaths.Data, "library.sqlite"), readOnly);
        try
        {
            if (db.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='photos'").Count > 0)
                foreach (var row in db.Query("SELECT id FROM photos")) AppPaths.ValidateId(row[0]);
        }
        catch { db.Dispose(); throw; }
        if (readOnly)
        {
            hasAlbumCovers = db.Query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='album_covers'").Count > 0;
            return;
        }
        db.Run("CREATE TABLE IF NOT EXISTS photos(id TEXT PRIMARY KEY,path TEXT NOT NULL COLLATE NOCASE UNIQUE,root TEXT NOT NULL,size INTEGER,modified INTEGER,taken TEXT,date_source TEXT,width INTEGER,height INTEGER,hash TEXT,visual_hash TEXT,sharpness REAL,brightness REAL,error TEXT,status TEXT NOT NULL DEFAULT 'active',original_path TEXT NOT NULL DEFAULT '',trash_date TEXT NOT NULL DEFAULT '',favorite INTEGER DEFAULT 0)");
        db.Run("CREATE INDEX IF NOT EXISTS photos_time ON photos(status,taken DESC,id)");
        db.Run("CREATE INDEX IF NOT EXISTS photos_hash ON photos(hash)");
        db.Run("CREATE INDEX IF NOT EXISTS photos_size ON photos(size)");
        db.Run("CREATE TABLE IF NOT EXISTS roots(path TEXT PRIMARY KEY COLLATE NOCASE)");
        db.Run("CREATE TABLE IF NOT EXISTS albums(id TEXT PRIMARY KEY,name TEXT NOT NULL)");
        db.Run("CREATE TABLE IF NOT EXISTS album_photos(album_id TEXT REFERENCES albums(id) ON DELETE CASCADE,photo_id TEXT REFERENCES photos(id),PRIMARY KEY(album_id,photo_id))");
        db.Run("CREATE TABLE IF NOT EXISTS album_covers(album_id TEXT PRIMARY KEY REFERENCES albums(id) ON DELETE CASCADE,photo_id TEXT NOT NULL,FOREIGN KEY(album_id,photo_id) REFERENCES album_photos(album_id,photo_id) ON DELETE CASCADE)");
        hasAlbumCovers = true;
        db.Run("CREATE TABLE IF NOT EXISTS duplicate_members(group_id TEXT,kind TEXT,photo_id TEXT REFERENCES photos(id),keeper TEXT,PRIMARY KEY(group_id,photo_id))");
        db.Run("CREATE TABLE IF NOT EXISTS duplicate_index_state(id INTEGER PRIMARY KEY CHECK(id=1),scanned_at TEXT NOT NULL,item_count INTEGER NOT NULL)");
        db.Run("CREATE TABLE IF NOT EXISTS operations(id TEXT PRIMARY KEY,batch TEXT,kind TEXT,photo_id TEXT REFERENCES photos(id),source TEXT,target TEXT,hash TEXT,state TEXT,error TEXT,created TEXT,source_root TEXT,target_root TEXT,keeper_id TEXT)");
        MigrateMedia();
    }
    static Photo Map(string[] r) => new()
    {
        Id = r[0], Path = r[1], Root = r[2], Size = long.Parse(r[3]), Modified = long.Parse(r[4]), Taken = r[5], DateSource = r[6], Width = int.Parse(r[7]), Height = int.Parse(r[8]), Hash = r[9], VisualHash = r[10], Sharpness = double.Parse(r[11], CultureInfo.InvariantCulture), Brightness = double.Parse(r[12], CultureInfo.InvariantCulture), Error = r[13], Status = r[14], OriginalPath = r[15], TrashDate = r[16], Favorite = r[17] == "1", MediaKind = r[18], ContentKind = r[19], Duration = double.Parse(r[20], CultureInfo.InvariantCulture), Added = r[21], PreviewState = r[22], ManualKind = r[23] == "1", Tags = r[24], Revision = long.Parse(r[25])
    };
    public Photo? ByPath(string path) => db.Query("SELECT * FROM photos WHERE path=?", path).Select(Map).FirstOrDefault();
    public Photo? ById(string id) => db.Query("SELECT * FROM photos WHERE id=?", id).Select(Map).FirstOrDefault();
    public void Upsert(Photo p)
    {
        AppPaths.ValidateId(p.Id);
        db.Run("INSERT INTO photos VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET path=excluded.path,root=excluded.root,size=excluded.size,modified=excluded.modified,taken=excluded.taken,date_source=excluded.date_source,width=excluded.width,height=excluded.height,hash=excluded.hash,visual_hash=excluded.visual_hash,sharpness=excluded.sharpness,brightness=excluded.brightness,error=excluded.error,status=excluded.status,original_path=excluded.original_path,trash_date=excluded.trash_date,favorite=excluded.favorite,media_kind=excluded.media_kind,content_kind=excluded.content_kind,duration=excluded.duration,added=excluded.added,preview_state=excluded.preview_state,manual_kind=excluded.manual_kind,tags=excluded.tags,revision=excluded.revision", p.Id, p.Path, p.Root, p.Size, p.Modified, p.Taken, p.DateSource, p.Width, p.Height, p.Hash, p.VisualHash, p.Sharpness, p.Brightness, p.Error, p.Status, p.OriginalPath, p.TrashDate, p.Favorite, p.MediaKind, p.ContentKind, p.Duration, p.Added, p.PreviewState, p.ManualKind, p.Tags, p.Revision);
    }
    public void UpsertMany(IEnumerable<Photo> photos) => db.Transaction(() => { foreach (var p in photos) Upsert(p); });
    public List<Photo> AllActive() => db.Query("SELECT * FROM photos WHERE status='active' ORDER BY taken DESC,id").Select(Map).ToList();
    public List<Photo> Trashed() => db.Query("SELECT * FROM photos WHERE status='trash' ORDER BY trash_date DESC,id").Select(Map).ToList();
    public List<Photo> All() => db.Query("SELECT * FROM photos ORDER BY taken DESC,id").Select(Map).ToList();
    public List<Photo> AlbumPhotos(string album) => db.Query("SELECT p.* FROM photos p JOIN album_photos a ON a.photo_id=p.id WHERE a.album_id=?", album).Select(Map).ToList();
    public List<Photo> RootPhotos(string root) => db.Query("SELECT * FROM photos WHERE root=? AND status IN ('active','missing')", root).Select(Map).ToList();
    public IReadOnlyList<string> Roots => db.Query("SELECT path FROM roots ORDER BY path").Select(r => r[0]).ToList();
    public void AddRoot(string root) => db.Run("INSERT OR IGNORE INTO roots VALUES(?)", root);
    internal void ImportAlbums(IReadOnlyList<(string Name, List<Photo> Photos, string? CoverPhotoId)> albums) => db.Transaction(() =>
    {
        foreach (var album in albums)
        {
            string id = CreateAlbum(album.Name);
            foreach (var photo in album.Photos) db.Run("INSERT OR IGNORE INTO album_photos VALUES(?,?)", id, photo.Id);
            if (album.CoverPhotoId is { } cover && album.Photos.Any(p => p.Id == cover && !p.IsVideo))
                db.Run("INSERT INTO album_covers VALUES(?,?)", id, cover);
        }
    });
    public void MarkMissing(string id) => db.Run("UPDATE photos SET status='missing' WHERE id=?", id);
    public void ClearDuplicates() { db.Run("DELETE FROM duplicate_members"); db.Run("DELETE FROM duplicate_index_state"); }
    public int Count(string status = "active") => int.Parse(db.Query("SELECT COUNT(*) FROM photos WHERE status=?", status)[0][0]);
    public List<string> Years() => db.Query("SELECT DISTINCT substr(taken,1,4) FROM photos WHERE taken<>'' AND status='active' ORDER BY 1 DESC").Select(r => r[0]).ToList();
    public (List<Photo> Items, int Total) Page(string view, string? album, string year, string month, string search, int offset, int limit)
    {
        var args = new List<object?>();
        string where = "p.status=?"; args.Add(view == "trash" ? "trash" : view == "missing" ? "missing" : "active");
        if (view == "favorites") where += " AND p.favorite=1";
        if (view == "dates") where += " AND p.date_source NOT IN ('拍摄时间','手动确认')";
        if (view == "errors") where += " AND p.error<>''";
        if (view == "album") { where += " AND EXISTS(SELECT 1 FROM album_photos a WHERE a.photo_id=p.id AND a.album_id=?)"; args.Add(album); }
        if (year != "全部年份") { where += " AND p.taken LIKE ?"; args.Add(year + (month == "全部月份" ? "%" : "-" + month + "%")); }
        else if (month != "全部月份") { where += " AND substr(p.taken,6,2)=?"; args.Add(month); }
        if (search.Length > 0) { where += " AND p.path LIKE ? ESCAPE '\\'"; args.Add("%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"); }
        var count = int.Parse(db.Query("SELECT COUNT(*) FROM photos p WHERE " + where, args.ToArray())[0][0]);
        args.Add(limit); args.Add(offset);
        return (db.Query("SELECT p.* FROM photos p WHERE " + where + " ORDER BY p.taken DESC,p.id LIMIT ? OFFSET ?", args.ToArray()).Select(Map).ToList(), count);
    }
    public List<Album> Albums() => db.Query(
        "SELECT a.id,a.name,(SELECT COUNT(*) FROM album_photos x JOIN photos p ON p.id=x.photo_id WHERE x.album_id=a.id AND p.status='active')," +
        (hasAlbumCovers ? "c.photo_id FROM albums a LEFT JOIN album_covers c ON c.album_id=a.id" : "'' FROM albums a") +
        " ORDER BY a.name").Select(r => new Album(r[0], r[1], int.Parse(r[2]), r[3])).ToList();
    public string AlbumCover(Album album)
    {
        if (album.CoverPhotoId.Length > 0 && ById(album.CoverPhotoId) is { Status: "active", IsVideo: false, PreviewState: "ready", Error.Length: 0 } cover &&
            File.Exists(cover.ThumbPath)) return cover.ThumbKey;
        return Browse(new("album", album.Id), 0, 1).Items.FirstOrDefault()?.ThumbKey ?? "";
    }
    public bool TrySetAlbumCover(string albumId, string photoId)
    {
        bool saved = false;
        db.Transaction(() =>
        {
            var eligible = db.Query("SELECT p.id FROM album_photos x JOIN photos p ON p.id=x.photo_id WHERE x.album_id=? AND p.id=? AND p.status='active' AND p.media_kind='image' AND p.error='' AND p.preview_state='ready'", albumId, photoId);
            if (eligible.Count == 0 || !File.Exists(AppPaths.Thumb(photoId))) return;
            db.Run("INSERT OR REPLACE INTO album_covers(album_id,photo_id) VALUES(?,?)", albumId, photoId);
            saved = true;
        });
        return saved;
    }
    public void ResetAlbumCover(string albumId) => db.Run("DELETE FROM album_covers WHERE album_id=?", albumId);
    public string CreateAlbum(string name) { var id = Guid.NewGuid().ToString("N"); db.Run("INSERT INTO albums VALUES(?,?)", id, name); return id; }
    public void RenameAlbum(string id, string name) => db.Run("UPDATE albums SET name=? WHERE id=?", name, id);
    public void DeleteAlbum(string id) => db.Run("DELETE FROM albums WHERE id=?", id);
    public void AddToAlbum(string album, IEnumerable<Photo> photos) => db.Transaction(() => { foreach (var p in photos) db.Run("INSERT OR IGNORE INTO album_photos VALUES(?,?)", album, p.Id); });
    public void RemoveFromAlbum(string album, IEnumerable<Photo> photos) => db.Transaction(() => { foreach (var p in photos) db.Run("DELETE FROM album_photos WHERE album_id=? AND photo_id=?", album, p.Id); });
    public void CopyAlbumReferences(string from, string to) => db.Run("INSERT OR IGNORE INTO album_photos SELECT album_id,? FROM album_photos WHERE photo_id=?", to, from);
    public void Favorite(IEnumerable<Photo> photos, bool favorite) => db.Transaction(() => { foreach (var p in photos) db.Run("UPDATE photos SET favorite=? WHERE id=?", favorite, p.Id); });
    public void SetDate(IEnumerable<Photo> photos, DateTime date) => db.Transaction(() => { foreach (var p in photos) db.Run("UPDATE photos SET taken=?,date_source='手动确认' WHERE id=?", date.ToString("yyyy-MM-dd HH:mm:ss"), p.Id); });
    public void SaveGroups(List<DuplicateGroup> groups) => db.Transaction(() =>
    {
        ClearDuplicates();
        foreach (var g in groups) foreach (var p in g.Photos) db.Run("INSERT INTO duplicate_members VALUES(?,?,?,?)", g.Id, g.Kind, p.Id, g.Keeper);
        db.Run("INSERT OR REPLACE INTO duplicate_index_state VALUES(1,?,?)", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Count());
    });
    public bool HasDuplicateIndex() => int.Parse(db.Query("SELECT COUNT(*) FROM duplicate_index_state WHERE id=1")[0][0]) == 1;
    public List<DuplicateGroup> CachedDuplicateGroups() => Groups("exact").Concat(Groups("similar")).ToList();
    public List<DuplicateGroup> Groups(string kind)
    {
        var rows = db.Query("SELECT d.group_id,d.keeper,p.* FROM duplicate_members d JOIN photos p ON p.id=d.photo_id WHERE d.kind=? AND p.status='active' ORDER BY d.group_id", kind);
        return rows.GroupBy(r => r[0]).Where(g => g.Count() > 1).Select(g => new DuplicateGroup(g.Key, kind, g.Select(r => Map(r.Skip(2).ToArray())).OrderByDescending(p => p.Quality).ToList(), g.First()[1])).ToList();
    }
    public void Prepare(FileOperation o) => db.Run("INSERT INTO operations VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)", o.Id, o.Batch, o.Kind, o.PhotoId, o.Source, o.Target, o.Hash, o.State, o.Error, o.Created, o.SourceRoot, o.TargetRoot, o.KeeperId);
    public void OpState(string id, string state, string error = "") => db.Run("UPDATE operations SET state=?,error=? WHERE id=?", state, error, id);
    public List<FileOperation> Operations(bool pendingOnly = true) => db.Query("SELECT * FROM operations" + (pendingOnly ? " WHERE state NOT IN ('Completed','Undone')" : "") + " ORDER BY created DESC").Select(r => new FileOperation(r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7], r[8], r[9], r[10], r[11], r[12])).ToList();
    public void Complete(FileOperation o)
    {
        db.Transaction(() =>
        {
            var p = ById(o.PhotoId) ?? throw new IOException("照片索引不存在");
            p.Path = o.Target;
            if (o.TargetRoot.Length > 0) { p.Root = o.TargetRoot; AddRoot(p.Root); }
            if (o.Kind == "trash") { p.OriginalPath = o.Source; p.TrashDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); p.Status = "trash"; }
            else if (o.Kind == "restore") { p.Status = "active"; p.OriginalPath = ""; p.TrashDate = ""; }
            else { p.Status = "active"; }
            // A restored/copied file may have a different filesystem timestamp.
            p.Modified = File.GetLastWriteTimeUtc(o.Target).Ticks;
            p.Hash = o.Hash;
            if (o.KeeperId.Length > 0) CopyAlbumReferences(o.PhotoId, o.KeeperId);
            Upsert(p); OpState(o.Id, "Completed");
        });
    }
    public void ForgetTrashed(string id) => db.Transaction(() => { db.Run("DELETE FROM duplicate_members WHERE photo_id=?", id); db.Run("DELETE FROM album_photos WHERE photo_id=?", id); db.Run("DELETE FROM operations WHERE photo_id=?", id); db.Run("DELETE FROM photos WHERE id=? AND status='trash'", id); });
    public void Backup(string destination)
    {
        // VACUUM INTO produces a consistent standalone snapshot, including WAL contents.
        db.Run("VACUUM INTO ?", destination);
    }
    public void Dispose() => db.Dispose();
}
