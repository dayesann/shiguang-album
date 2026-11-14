namespace Shiguang;

public sealed partial class Catalog
{
    public int RelinkRoot(string oldRoot, string newRoot)
    {
        newRoot = Path.GetFullPath(newRoot); LibraryService.NoLinks(newRoot);
        if (!Directory.Exists(newRoot)) throw new IOException("新目录不存在。");
        if (Operations().Count > 0) throw new IOException("请先处理未完成的文件操作。");
        var items = All().Where(p => p.Root.Equals(oldRoot, StringComparison.OrdinalIgnoreCase)).ToList();
        string Remap(string path) => path.Length == 0 ? "" : AppPaths.Within(path, oldRoot) ? Path.GetFullPath(Path.Combine(newRoot, Path.GetRelativePath(oldRoot, path))) : throw new IOException("部分文件不在原来源目录内，未更改连接。");
        var mapped = items.Select(p => (Photo: p, Path: Remap(p.Path), Original: Remap(p.OriginalPath))).ToList();
        var verified = new Dictionary<string, (string Hash, long Modified)>();
        foreach (var item in mapped)
        {
            if (ByPath(item.Path) is Photo other && other.Id != item.Photo.Id) throw new IOException("新位置与已有项目冲突。");
            if (!File.Exists(item.Path)) throw new IOException("新目录缺少文件：" + item.Photo.Name);
            LibraryService.NoLinks(item.Path);
            var info = new FileInfo(item.Path);
            string expected = item.Photo.Hash;
            if (expected.Length == 0)
            {
                if (!File.Exists(item.Photo.Path)) throw new IOException("原位置离线且没有内容校验记录，无法确认文件身份，未更改连接：" + item.Photo.Name);
                expected = LibraryService.CheckedHash(item.Photo);
            }
            long modified = info.LastWriteTimeUtc.Ticks;
            string actual = LibraryService.HashFile(item.Path);
            info.Refresh();
            if (info.Length != item.Photo.Size || info.LastWriteTimeUtc.Ticks != modified || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("新目录中的文件与原记录不符：" + item.Photo.Name);
            // Validate every cache destination before committing any changed references.
            _ = item.Photo.ThumbPath; _ = item.Photo.LargePath;
            verified[item.Photo.Id] = (actual, modified);
        }
        db.Transaction(() =>
        {
            foreach (var item in mapped) db.Run("UPDATE photos SET path=?,root=?,original_path=?,modified=?,hash=?,preview_state='pending',visual_hash='',revision=?,status=CASE WHEN status='missing' THEN 'active' ELSE status END WHERE id=?", item.Path, newRoot, item.Original, verified[item.Photo.Id].Modified, verified[item.Photo.Id].Hash, DateTime.UtcNow.Ticks, item.Photo.Id);
            db.Run("DELETE FROM roots WHERE path=?", oldRoot); AddRoot(newRoot);
            db.Run("DELETE FROM operations"); ClearDuplicates();
        });
        foreach (var item in mapped)
        {
            if (File.Exists(item.Photo.ThumbPath)) File.Delete(item.Photo.ThumbPath);
            if (File.Exists(item.Photo.LargePath)) File.Delete(item.Photo.LargePath);
        }
        return mapped.Count;
    }
}
