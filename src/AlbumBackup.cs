using System.Text.Json;

namespace Shiguang;

public static class AlbumBackup
{
    public sealed record Reference(string Id, string Path, string Hash);
    public sealed record SavedAlbum(string Name, List<Reference> Photos, string? CoverPhotoId = null);
    public sealed record Backup(int Version, List<SavedAlbum> Albums);
    public static void Export(Catalog catalog, string path)
    {
        var albums = new List<SavedAlbum>();
        foreach (var album in catalog.Albums())
        {
            var photos = catalog.AlbumPhotos(album.Id);
            albums.Add(new(album.Name, photos.Select(p => new Reference(p.Id, p.Path, p.Hash)).ToList(),
                album.CoverPhotoId.Length > 0 ? album.CoverPhotoId : null));
        }
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(output, new Backup(1, albums), new JsonSerializerOptions { WriteIndented = true });
    }
    public static string Import(Catalog catalog, string path)
    {
        if (new FileInfo(path).Length > 100 * 1024 * 1024) throw new IOException("相册备份文件过大");
        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(path)) ?? throw new IOException("相册备份格式错误");
        if (backup.Version != 1 || backup.Albums == null) throw new IOException("不支持此备份版本");
        // Validate the entire document before making any writes, and commit all albums atomically.
        if (backup.Albums.Any(a => a == null || string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 160 || a.Photos == null || a.Photos.Any(p => p == null || p.Id == null || p.Path == null || p.Hash == null))) throw new IOException("相册备份结构不完整，未导入任何相册。");
        int linked = 0, missing = 0;
        var prepared = new List<(string Name, List<Photo> Photos, string? CoverPhotoId)>();
        var active = catalog.All(); var ids = active.ToDictionary(p => p.Id); var paths = active.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        var hashes = active.Where(p => p.Hash.Length > 0).GroupBy(p => p.Hash).ToDictionary(g => g.Key, g => g.First());
        foreach (var album in backup.Albums)
        {
            var photos = new List<Photo>();
            string? cover = null;
            foreach (var reference in album.Photos)
            {
                Photo? match = ids.GetValueOrDefault(reference.Id) ?? paths.GetValueOrDefault(reference.Path);
                if (match != null && reference.Hash.Length > 0 && match.Hash.Length > 0 && match.Hash != reference.Hash) match = null;
                match ??= reference.Hash.Length > 0 ? hashes.GetValueOrDefault(reference.Hash) : null;
                if (match != null)
                {
                    if (reference.Id == album.CoverPhotoId && !match.IsVideo) cover = match.Id;
                    if (!photos.Any(p => p.Id == match.Id)) { photos.Add(match); linked++; }
                }
                else missing++;
            }
            prepared.Add((album.Name + "（导入）", photos, cover));
        }
        catalog.ImportAlbums(prepared);
        return $"已导入 {backup.Albums.Count} 个相册 · 匹配 {linked} 张 · 未匹配 {missing} 张；未匹配的照片请先扫描来源目录";
    }
}
