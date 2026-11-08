using System.IO;

namespace Shiguang;

public sealed class FileOrganizer(Catalog catalog)
{
    public List<ArchiveItem> Plan(IEnumerable<Photo> photos, string destination)
    {
        destination = Path.GetFullPath(destination); LibraryService.NoLinks(destination);
        if (AppPaths.Within(destination, AppPaths.Excluded) || AppPaths.Within(destination, AppPaths.Data) || destination.Contains(".shiguang-trash", StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择程序目录和最近删除目录之外的归档位置");
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var result = new List<ArchiveItem>();
        foreach (var p in photos)
        {
            var known = DateTime.TryParseExact(p.Taken, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date);
            var bucket = known ? Path.Combine(date.ToString("yyyy"), date.ToString("MM")) : "待确认日期";
            var target = Path.Combine(destination, bucket, p.Name);
            if (target.Equals(p.Path, StringComparison.OrdinalIgnoreCase)) continue;
            var unique = UniquePath(target, reserved);
            while (catalog.ByPath(unique) is Photo existing && existing.Id != p.Id) { reserved.Add(unique); unique = UniquePath(target, reserved); }
            reserved.Add(unique);
            bool certain = p.DateSource is "拍摄时间" or "手动确认";
            result.Add(new ArchiveItem { Photo = p, Destination = unique, Selected = certain, Note = certain ? (unique == target ? "" : "同名文件已自动避让") : "日期需确认；勾选表示接受此日期" });
        }
        return result;
    }
    static string UniquePath(string path, HashSet<string>? reserved = null)
    {
        string candidate = path; int i = 1;
        while (File.Exists(candidate) || Directory.Exists(candidate) || reserved?.Contains(candidate) == true)
            candidate = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + $"_{i++:D3}" + Path.GetExtension(path));
        return candidate;
    }
    public int Archive(List<ArchiveItem> plan, string targetRoot, IProgress<JobProgress> progress, CancellationToken ct)
    {
        EnsureNoPending(); var selected = plan.Where(i => i.Selected).ToList(); int count = 0; string batch = Guid.NewGuid().ToString("N");
        foreach (var item in selected)
        {
            ct.ThrowIfCancellationRequested();
            if (!AppPaths.Within(item.Destination, targetRoot)) throw new IOException("目标路径超出归档目录");
            Transfer(item.Photo, item.Destination, "archive", batch, targetRoot, ct);
            progress.Report(new($"归档 {++count:N0} / {selected.Count:N0}", count, selected.Count));
        }
        return count;
    }
    public int Trash(List<Photo> photos, Dictionary<string, string>? keepers, IProgress<JobProgress> progress, CancellationToken ct)
    {
        EnsureNoPending(); int count = 0; string batch = Guid.NewGuid().ToString("N");
        var deleting = photos.Select(p => p.Id).ToHashSet();
        if (keepers != null)
            foreach (var item in keepers)
                if (deleting.Contains(item.Value)) throw new IOException("每组必须至少保留一张照片");
        foreach (var item in photos)
        {
            ct.ThrowIfCancellationRequested();
            var p = catalog.ById(item.Id) ?? throw new IOException("照片索引不存在");
            if (p.Status != "active") continue;
            string keeper = keepers?.GetValueOrDefault(p.Id) ?? "";
            if (keeper.Length > 0)
            {
                var k = catalog.ById(keeper);
                if (k is not { Status: "active" } || LibraryService.CheckedHash(p) != LibraryService.CheckedHash(k)) throw new IOException("保留项已变化，已停止清理，请重新检测重复照片");
            }
            var trashRoot = AppPaths.Within(p.Path, p.Root) ? p.Root : Path.GetDirectoryName(p.Path)!;
            var target = Path.Combine(trashRoot, ".shiguang-trash", p.Id + Path.GetExtension(p.Path));
            Transfer(p, target, "trash", batch, p.Root, ct, keeper);
            progress.Report(new($"移入最近删除 {++count:N0} / {photos.Count:N0}", count, photos.Count));
        }
        return count;
    }
    public int Restore(List<Photo> photos, IProgress<JobProgress> progress, CancellationToken ct)
    {
        EnsureNoPending(); int count = 0; string batch = Guid.NewGuid().ToString("N");
        foreach (var item in photos)
        {
            ct.ThrowIfCancellationRequested(); var p = catalog.ById(item.Id);
            if (p?.Status != "trash" || p.OriginalPath.Length == 0) continue;
            var target = UniquePath(p.OriginalPath);
            Transfer(p, target, "restore", batch, p.Root, ct);
            progress.Report(new($"恢复 {++count:N0} / {photos.Count:N0}", count, photos.Count));
        }
        return count;
    }
    public int Purge(List<Photo> photos, IProgress<JobProgress> progress, CancellationToken ct)
    {
        EnsureNoPending(); int count = 0; string batch = Guid.NewGuid().ToString("N");
        foreach (var item in photos)
        {
            ct.ThrowIfCancellationRequested(); var p = catalog.ById(item.Id);
            if (p?.Status != "trash") continue;
            if (!p.Path.Contains("\\.shiguang-trash\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("拒绝清除最近删除目录之外的文件");
            var hash = LibraryService.CheckedHash(p);
            var op = new FileOperation(Guid.NewGuid().ToString("N"), batch, "purge", p.Id, p.Path, "", hash, "Prepared", "", DateTime.UtcNow.ToString("O"));
            catalog.Prepare(op);
            File.Delete(p.Path); catalog.ForgetTrashed(p.Id);
            if (File.Exists(p.ThumbPath)) File.Delete(p.ThumbPath);
            progress.Report(new($"永久清除 {++count:N0} / {photos.Count:N0}", count, photos.Count));
        }
        return count;
    }
    public int UndoLastArchive(IProgress<JobProgress> progress, CancellationToken ct)
    {
        EnsureNoPending();
        var operations = catalog.Operations(false).Where(o => o.Kind == "archive" && o.State == "Completed").ToList();
        var batch = operations.FirstOrDefault()?.Batch;
        var work = operations.Where(o => o.Batch == batch).ToList(); int count = 0;
        foreach (var o in work)
        {
            ct.ThrowIfCancellationRequested(); var p = catalog.ById(o.PhotoId);
            if (p?.Status != "active" || !p.Path.Equals(o.Target, StringComparison.OrdinalIgnoreCase)) throw new IOException("照片已再次移动或删除，无法直接撤销：" + Path.GetFileName(o.Target));
            if (LibraryService.CheckedHash(p) != o.Hash) throw new IOException("归档后文件内容已发生变化，无法撤销");
            if (File.Exists(o.Source) || Directory.Exists(o.Source)) throw new IOException("原位置已有同名文件，请先处理：" + o.Source);
            Transfer(p, o.Source, "undo", o.Batch, o.SourceRoot, ct);
            catalog.OpState(o.Id, "Undone");
            progress.Report(new($"撤销归档 {++count:N0} / {work.Count:N0}", count, work.Count));
        }
        return count;
    }
    void EnsureNoPending() { if (catalog.Operations().Count > 0) throw new IOException("存在未完成操作，请先在“任务恢复”中处理。"); }
    static bool Present(string path)
    {
        if (path.Length == 0) return false;
        try { File.GetAttributes(path); return true; }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
    public void Transfer(Photo p, string target, string kind, string batch, string targetRoot, CancellationToken ct, string keeper = "")
    {
        ct.ThrowIfCancellationRequested(); LibraryService.NoLinks(p.Path); LibraryService.NoLinks(target);
        if (File.Exists(target) || Directory.Exists(target) || catalog.ByPath(target) is Photo existing && existing.Id != p.Id) throw new IOException("目标已存在，未覆盖任何文件：" + target);
        var hash = LibraryService.CheckedHash(p);
        var op = new FileOperation(Guid.NewGuid().ToString("N"), batch, kind, p.Id, p.Path, target, hash, "Prepared", "", DateTime.UtcNow.ToString("O"), p.Root, targetRoot, keeper);
        catalog.Prepare(op);
        try { TransferFiles(op, ct); catalog.Complete(op); }
        catch (Exception e) { catalog.OpState(op.Id, "NeedsReview", e.Message); throw; }
    }
    void TransferFiles(FileOperation o, CancellationToken ct)
    {
        LibraryService.NoLinks(o.Source); LibraryService.NoLinks(o.Target);
        if (o.Source.Equals(o.Target, StringComparison.OrdinalIgnoreCase)) throw new IOException("源路径和目标路径相同");
        if (LibraryService.HashFile(o.Source) != o.Hash) throw new IOException("源文件已变化，操作中止");
        Directory.CreateDirectory(Path.GetDirectoryName(o.Target)!);
        if (Path.GetPathRoot(o.Source)!.Equals(Path.GetPathRoot(o.Target), StringComparison.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested(); File.Move(o.Source, o.Target, false);
        }
        else
        {
            var temp = o.Target + ".shiguang-" + o.Id + ".partial";
            using (var input = File.Open(o.Source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1024 * 1024]; int read;
                while ((read = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
                output.Flush(true);
            }
            if (LibraryService.HashFile(temp) != o.Hash) throw new IOException("复制校验失败，源文件已保留");
            File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(o.Source));
            File.Move(temp, o.Target, false);
            catalog.OpState(o.Id, "TargetReady");
            ct.ThrowIfCancellationRequested();
            if (LibraryService.HashFile(o.Source) != o.Hash || LibraryService.HashFile(o.Target) != o.Hash) throw new IOException("复制后内容变化，已保留两份文件等待处理");
            File.Delete(o.Source);
        }
        if (LibraryService.HashFile(o.Target) != o.Hash) throw new IOException("移动后校验失败，请在任务恢复中检查");
    }
    // Startup recovery only reconciles an already completed move; it never deletes a remaining original.
    public int Reconcile()
    {
        int count = 0;
        foreach (var o in catalog.Operations())
        {
            try
            {
                if (o.Kind == "purge") { if (!Present(o.Source)) { catalog.ForgetTrashed(o.PhotoId); count++; } continue; }
                if (!Present(o.Source) && Present(o.Target))
                {
                    LibraryService.NoLinks(o.Target);
                    if (LibraryService.HashFile(o.Target) != o.Hash) throw new IOException("目标内容不匹配，需要手动检查");
                    catalog.Complete(o); count++;
                    if (o.Kind == "undo") MarkOriginalUndone(o);
                }
            }
            catch (Exception e) { catalog.OpState(o.Id, "NeedsReview", e.Message); }
        }
        return count;
    }
    void MarkOriginalUndone(FileOperation undo)
    {
        foreach (var original in catalog.Operations(false).Where(x => x.Kind == "archive" && x.Batch == undo.Batch && x.PhotoId == undo.PhotoId && x.State == "Completed")) catalog.OpState(original.Id, "Undone");
    }
    public int ResumePending(IProgress<JobProgress> progress, CancellationToken ct)
    {
        int count = Reconcile();
        foreach (var o in catalog.Operations().OrderBy(o => o.Created))
        {
            ct.ThrowIfCancellationRequested(); LibraryService.NoLinks(o.Source);
            if (o.Kind == "purge") throw new IOException("未完成的永久清除不会自动继续；请使用“取消未开始任务”后重新确认清除。");
            LibraryService.NoLinks(o.Target);
            if (o.KeeperId.Length > 0)
            {
                var keeper = catalog.ById(o.KeeperId);
                if (keeper?.Status != "active" || LibraryService.CheckedHash(keeper) != o.Hash) throw new IOException("保留副本不可用，不能继续重复清理");
            }
            if (Present(o.Source) && Present(o.Target))
            {
                if (LibraryService.HashFile(o.Source) != o.Hash || LibraryService.HashFile(o.Target) != o.Hash) throw new IOException("两处文件内容不同，未删除任何文件：" + o.Source);
                File.Delete(o.Source);
            }
            else if (Present(o.Source))
            {
                string partial = o.Target + ".shiguang-" + o.Id + ".partial";
                if (File.Exists(partial))
                {
                    // The remaining original must still match before discarding a partial copy.
                    if (LibraryService.HashFile(o.Source) != o.Hash) throw new IOException("源文件发生变化，保留临时文件等待人工检查");
                    File.Delete(partial);
                }
                TransferFiles(o, ct);
            }
            else throw new IOException("源文件和目标文件均不存在，需要人工检查：" + o.Source);
            catalog.Complete(o); if (o.Kind == "undo") MarkOriginalUndone(o);
            progress.Report(new($"已恢复 {++count} 项操作"));
        }
        return count;
    }
    public int CancelUntouched()
    {
        int count = 0;
        foreach (var o in catalog.Operations())
        {
            LibraryService.NoLinks(o.Source);
            if (Present(o.Source) && !Present(o.Target) && LibraryService.HashFile(o.Source) == o.Hash)
            {
                var partial = o.Target + ".shiguang-" + o.Id + ".partial";
                if (o.Target.Length > 0 && File.Exists(partial)) { LibraryService.NoLinks(partial); File.Delete(partial); }
                catalog.OpState(o.Id, "Undone", "已取消，源文件保留"); count++;
            }
        }
        return count;
    }
}
