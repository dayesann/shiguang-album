namespace Shiguang;

public sealed partial class Catalog
{
    void MigrateMedia()
    {
        var existing = db.Query("PRAGMA table_info(photos)").Select(r => r[1]).ToHashSet();
        var columns = new[] { ("media_kind", "TEXT NOT NULL DEFAULT 'image'"), ("content_kind", "TEXT NOT NULL DEFAULT 'photo'"), ("duration", "REAL NOT NULL DEFAULT 0"), ("added", "TEXT NOT NULL DEFAULT ''"), ("preview_state", "TEXT NOT NULL DEFAULT 'ready'"), ("manual_kind", "INTEGER NOT NULL DEFAULT 0"), ("tags", "TEXT NOT NULL DEFAULT ''"), ("revision", "INTEGER NOT NULL DEFAULT 0") };
        if (!existing.Contains("media_kind") && int.Parse(db.Query("SELECT COUNT(*) FROM photos")[0][0]) > 0)
        {
            var backups = System.IO.Path.Combine(AppPaths.Data, "backups"); Directory.CreateDirectory(backups);
            db.Run("VACUUM INTO ?", System.IO.Path.Combine(backups, "升级前-v0.1-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".sqlite"));
        }
        db.Transaction(() => { foreach (var c in columns) if (!existing.Contains(c.Item1)) db.Run("ALTER TABLE photos ADD COLUMN " + c.Item1 + " " + c.Item2); });
        if (!existing.Contains("content_kind"))
        {
            db.Run("UPDATE photos SET content_kind=CASE WHEN lower(path) LIKE '%screenshot%' OR path LIKE '%截图%' OR path LIKE '%屏幕快照%' THEN 'screenshot' WHEN height>0 AND width>=height*2.8 THEN 'panorama' ELSE 'photo' END,preview_state=CASE WHEN error<>'' THEN 'error' ELSE 'ready' END");
        }
        db.Run("CREATE INDEX IF NOT EXISTS media_category ON photos(status,content_kind,taken DESC)");
        db.Run("CREATE INDEX IF NOT EXISTS media_kind ON photos(status,media_kind,taken DESC)");
        db.Run("CREATE INDEX IF NOT EXISTS albums_reverse ON album_photos(photo_id,album_id)");
        db.Run("PRAGMA user_version=3");
    }
    (string Sql, List<object?> Args) Where(MediaFilter f)
    {
        var args = new List<object?>(); string where = "p.status='active'";
        if (f.View == "missing") where = "p.status='missing'";
        if (f.View == "favorites") where += " AND p.favorite=1";
        if (f.View == "errors") where += " AND p.error<>''";
        if (f.View == "unfiled") where += " AND NOT EXISTS(SELECT 1 FROM album_photos a WHERE a.photo_id=p.id)";
        if (f.View == "album") { where += " AND EXISTS(SELECT 1 FROM album_photos a WHERE a.photo_id=p.id AND a.album_id=?)"; args.Add(f.Album); }
        if (f.Kind is "image" or "video") { where += " AND p.media_kind=?"; args.Add(f.Kind); }
        if (f.Kind is "panorama" or "screenshot") { where += " AND p.content_kind=?"; args.Add(f.Kind); }
        if (f.Year.Length == 4 && int.TryParse(f.Year, out int year) && year is >= 1 and < 9999)
        {
            DateTime start = new(year, 1, 1), end = start.AddYears(1);
            if (int.TryParse(f.Month, out int month) && month is >= 1 and <= 12)
            {
                start = new(year, month, 1); end = start.AddMonths(1);
                if (int.TryParse(f.Day, out int day) && day >= 1 && day <= DateTime.DaysInMonth(year, month)) { start = new(year, month, day); end = start.AddDays(1); }
            }
            where += " AND p.taken>=? AND p.taken<?"; args.Add(start.ToString("yyyy-MM-dd")); args.Add(end.ToString("yyyy-MM-dd"));
        }
        if (f.Search.Length > 0)
        {
            where += " AND (p.path LIKE ? ESCAPE '\\' OR p.tags LIKE ? ESCAPE '\\')";
            var q = "%" + f.Search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"; args.Add(q); args.Add(q);
        }
        return (where, args);
    }
    public (List<Photo> Items, int Total) Browse(MediaFilter f, int offset = 0, int limit = 240)
    {
        var (sql, args) = Where(f); int count = int.Parse(db.Query("SELECT COUNT(*) FROM photos p WHERE " + sql, args.ToArray())[0][0]);
        args.Add(limit); args.Add(offset);
        var order = f.View == "recent" ? "p.added DESC,p.id" : "p.taken DESC,p.id";
        return (db.Query("SELECT p.* FROM photos p WHERE " + sql + " ORDER BY " + order + " LIMIT ? OFFSET ?", args.ToArray()).Select(Map).ToList(), count);
    }
    public int BrowseIndex(MediaFilter f, string id)
    {
        var (sql, args) = Where(f); args.Add(id);
        var order = f.View == "recent" ? "p.added DESC,p.id" : "p.taken DESC,p.id";
        var rows = db.Query("SELECT ordinal FROM (SELECT p.id,ROW_NUMBER() OVER (ORDER BY " + order + ")-1 ordinal FROM photos p WHERE " + sql + ") WHERE id=?", args.ToArray());
        return rows.Count == 0 ? -1 : int.Parse(rows[0][0]);
    }
    public List<TimeBucket> Buckets(MediaFilter filter, bool months)
    {
        var (sql, args) = Where(filter); int length = months ? 7 : 4;
        var rows = db.Query($"SELECT substr(p.taken,1,{length}),COUNT(*),MAX(p.taken) FROM photos p WHERE " + sql + $" AND p.taken<>'' GROUP BY substr(p.taken,1,{length}) ORDER BY 1 DESC", args.ToArray());
        var result = new List<TimeBucket>();
        foreach (var row in rows)
        {
            var a = new List<object?>(args) { row[0] + "%" };
            var covers = db.Query("SELECT id FROM photos p WHERE " + sql + " AND p.taken LIKE ? ORDER BY CASE WHEN p.content_kind='photo' THEN 0 ELSE 1 END,p.taken DESC LIMIT 2", a.ToArray());
            result.Add(new(row[0], months ? int.Parse(row[0][5..]).ToString() + "月" : row[0] + "年", int.Parse(row[1]), covers.Count > 0 ? AppPaths.Thumb(covers[0][0]) : "", covers.Count > 1 ? AppPaths.Thumb(covers[1][0]) : ""));
        }
        return result;
    }
    public List<string> Months(string year) => db.Query("SELECT DISTINCT substr(taken,6,2) FROM photos WHERE status='active' AND taken LIKE ? ORDER BY 1 DESC", year + "-%").Select(r => r[0]).ToList();
    public List<string> Days(string year, string month) => db.Query("SELECT DISTINCT substr(taken,9,2) FROM photos WHERE status='active' AND taken LIKE ? ORDER BY 1 DESC", year + "-" + month + "-%").Select(r => r[0]).ToList();
    public List<Photo> PendingPreviews() => db.Query("SELECT * FROM photos WHERE status='active' AND preview_state='pending' ORDER BY taken DESC").Select(Map).ToList();
    public List<Photo> PendingVisualFingerprints() => db.Query("SELECT * FROM photos WHERE status='active' AND error='' AND visual_hash='' AND preview_state='ready' ORDER BY taken DESC").Select(Map).ToList();
    public void UpdatePreview(Photo decoded)
    {
        db.Transaction(() =>
        {
        var p = ById(decoded.Id); if (p == null || p.Modified != decoded.Modified || p.Size != decoded.Size || p.Path != decoded.Path) return;
        if (p.DateSource != "手动确认") { p.Taken = decoded.Taken; p.DateSource = decoded.DateSource; }
        p.Width = decoded.Width; p.Height = decoded.Height; p.Duration = decoded.Duration; p.MediaKind = decoded.MediaKind;
        if (!p.ManualKind) p.ContentKind = decoded.ContentKind;
        p.VisualHash = decoded.VisualHash; p.Sharpness = decoded.Sharpness; p.Brightness = decoded.Brightness;
        p.Error = decoded.Error; p.PreviewState = decoded.Error.Length > 0 ? "error" : "ready"; p.Revision = DateTime.UtcNow.Ticks; Upsert(p);
        });
    }
    public void UpdateVisualFingerprint(Photo decoded)
    {
        if (decoded.VisualHash.Length != 16) return;
        db.Run("UPDATE photos SET visual_hash=?,sharpness=?,brightness=?,revision=? WHERE id=? AND modified=? AND size=?",
            decoded.VisualHash, decoded.Sharpness, decoded.Brightness, DateTime.UtcNow.Ticks, decoded.Id, decoded.Modified, decoded.Size);
    }
    public void SaveIndexBatch(IEnumerable<Photo> photos) => db.Transaction(() =>
    {
        foreach (var p in photos)
        {
            var live = ById(p.Id);
            if (live != null)
            {
                p.Favorite = live.Favorite; p.Tags = live.Tags;
                if (live.ManualKind) { p.ManualKind = true; p.ContentKind = live.ContentKind; }
                if (live.DateSource == "手动确认") { p.Taken = live.Taken; p.DateSource = live.DateSource; }
            }
            Upsert(p);
        }
    });
    public void SetKind(IEnumerable<Photo> photos, string kind)
    {
        if (kind is not ("photo" or "screenshot" or "panorama")) return;
        db.Transaction(() => { foreach (var p in photos.Where(p => !p.IsVideo)) db.Run("UPDATE photos SET content_kind=?,manual_kind=1 WHERE id=?", kind, p.Id); });
    }
    public void SetTags(IEnumerable<Photo> photos, string tags) => db.Transaction(() => { foreach (var p in photos) db.Run("UPDATE photos SET tags=? WHERE id=?", tags, p.Id); });
    public string Memberships(string id) => string.Join("、", db.Query("SELECT a.name FROM albums a JOIN album_photos x ON x.album_id=a.id WHERE x.photo_id=? ORDER BY a.name", id).Select(r => r[0]));
}
