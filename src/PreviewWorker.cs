using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Shiguang;

public sealed record PreviewJob(string Mode, Photo Photo);
public sealed record PreviewResult(bool Success, Photo Photo, string CachePath, string Error);

public static class ChildProcess
{
    public static ProcessStartInfo StartInfo(params string[] arguments)
    {
        string exe = Environment.ProcessPath ?? throw new IOException("无法确定程序路径");
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        if (System.IO.Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        return info;
    }
}

public static class PreviewWorker
{
    public static int Serve()
    {
        string? line; int count = 0;
        while ((line = Console.ReadLine()) != null)
        {
            PreviewResult result;
            try
            {
                var job = JsonSerializer.Deserialize<PreviewJob>(line) ?? throw new IOException("无效预览任务");
                if (job.Mode == "test-exit") Environment.Exit(71);
                LibraryService.CheckUnchanged(job.Photo);
                if (job.Mode == "large")
                {
                    if (!File.Exists(job.Photo.LargePath))
                    {
                        var source = Imaging.Full(job.Photo); double scale = Math.Min(1d, 6144d / Math.Max(source.PixelWidth, source.PixelHeight));
                        if (scale < 1) source = new System.Windows.Media.Imaging.TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));
                        Save(source, job.Photo.LargePath, 94);
                    }
                    result = new(true, job.Photo, job.Photo.LargePath, "");
                }
                else
                {
                    Photo p;
                    if (job.Photo.IsVideo) p = ShellMedia.Read(job.Photo);
                    else
                    {
                        p = Imaging.Read(job.Photo.Path, job.Photo.Root, job.Photo); p.Added = job.Photo.Added;
                        p.ManualKind = job.Photo.ManualKind; p.ContentKind = p.ManualKind ? job.Photo.ContentKind : MediaIndexer.GuessKind(p.Path, p.Width, p.Height);
                        if (p.Error.Length == 0 && p.IsPanorama)
                        {
                            var wide = Imaging.Decode(p.Path, Math.Min(p.Width, 1600)); Save(wide, p.ThumbPath, 85);
                        }
                    }
                    result = new(p.Error.Length == 0, p, p.ThumbPath, p.Error);
                }
            }
            catch (Exception e)
            {
                var input = JsonSerializer.Deserialize<PreviewJob>(line)?.Photo ?? new Photo(); input.Error = e.Message;
                result = new(false, input, "", e.Message);
            }
            try { Console.WriteLine(JsonSerializer.Serialize(result)); Console.Out.Flush(); } catch (IOException) { return 0; }
            if (++count >= 48) break; // Bound native codec lifetime and unmanaged memory growth.
        }
        return 0;
    }
    public static void Save(BitmapSource bitmap, string path, int quality = 85)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var encoder = new JpegBitmapEncoder { QualityLevel = quality }; encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(temporary)) encoder.Save(output);
        File.Move(temporary, path, true);
    }
}

public sealed class PreviewSession : IDisposable
{
    Process? process;
    int jobs;
    public async Task<PreviewResult> Request(PreviewJob job, CancellationToken ct)
    {
        if (process == null || process.HasExited || jobs >= 48 || process.PrivateMemorySize64 > 600L * 1024 * 1024) Restart();
        try
        {
            await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(job)); await process.StandardInput.FlushAsync();
            var result = await process.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(35), ct); jobs++;
            if (result == null) throw new IOException("预览进程已退出，已隔离此文件的故障");
            return JsonSerializer.Deserialize<PreviewResult>(result) ?? throw new IOException("预览结果无效");
        }
        catch (OperationCanceledException) { Dispose(); throw; }
        catch (Exception e)
        {
            Dispose(); var p = job.Photo; p.Error = e is TimeoutException ? "生成预览超时；原文件未变，其他媒体仍可浏览" : e.Message;
            LibraryService.Log("预览隔离", p.Path, e); return new(false, p, "", p.Error);
        }
    }
    void Restart()
    {
        Dispose(); var info = ChildProcess.StartInfo("--worker-server", "--data", AppPaths.Data, "--exclude", AppPaths.Excluded);
        info.RedirectStandardInput = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        process = Process.Start(info) ?? throw new IOException("无法启动预览进程");
        _ = process.StandardError.ReadToEndAsync(); jobs = 0;
    }
    public void Dispose()
    {
        if (process != null) { try { if (!process.HasExited) process.Kill(true); } catch { } process.Dispose(); process = null; }
    }
    public static async Task<string> Large(Photo p, CancellationToken ct = default)
    {
        if (File.Exists(p.LargePath)) return p.LargePath;
        using var session = new PreviewSession(); var result = await session.Request(new("large", p), ct);
        if (!result.Success) throw new IOException(result.Error); return result.CachePath;
    }
}

public static class PreviewPipeline
{
    public static async Task<int> Enrich(Catalog catalog, IProgress<JobProgress> progress, CancellationToken ct)
    {
        var pending = catalog.PendingPreviews(); int completed = 0;
        var queue = new System.Collections.Concurrent.ConcurrentQueue<Photo>(pending);
        async Task Worker()
        {
            using var session = new PreviewSession();
            while (queue.TryDequeue(out var p))
            {
                ct.ThrowIfCancellationRequested(); var result = await session.Request(new("thumb", p), ct);
                catalog.UpdatePreview(result.Photo); int done = Interlocked.Increment(ref completed);
                progress.Report(new($"生成预览 {done:N0} / {pending.Count:N0} · 可继续浏览", done, pending.Count, p.Id));
            }
        }
        await Task.WhenAll(Worker(), Worker());

        var missingFingerprints = catalog.PendingVisualFingerprints();
        var fingerprintQueue = new System.Collections.Concurrent.ConcurrentQueue<Photo>(missingFingerprints);
        int fingerprinted = 0;
        async Task FingerprintWorker()
        {
            while (fingerprintQueue.TryDequeue(out var photo))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    LibraryService.CheckUnchanged(photo);
                    if (!File.Exists(photo.ThumbPath)) continue;
                    var source = await Task.Run(() => Imaging.Decode(photo.ThumbPath, 128), ct);
                    var metrics = Imaging.Analyze(source);
                    photo.VisualHash = metrics.Hash.ToString("X16"); photo.Sharpness = metrics.Sharpness; photo.Brightness = metrics.Brightness;
                    catalog.UpdateVisualFingerprint(photo);
                    int done = Interlocked.Increment(ref fingerprinted);
                    progress.Report(new($"补充视觉指纹 {done:N0} / {missingFingerprints.Count:N0}", done, missingFingerprints.Count, photo.Id));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { LibraryService.Log("视觉指纹", photo.Path, error); }
            }
        }
        await Task.WhenAll(FingerprintWorker(), FingerprintWorker());
        return completed + fingerprinted;
    }
}
