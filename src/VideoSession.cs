using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Shiguang;

public sealed record VideoRequest(string Path, MediaFilter? Filter = null, string Id = "", string Scope = "视频");

// Keep only the query context, not a copy of every path in a large media library.
internal sealed class VideoQueue : IDisposable
{
    readonly Catalog? catalog;
    readonly MediaFilter? filter;
    readonly VideoRequest request;
    public int Count { get; }
    public int Index { get; set; }
    public string Scope => request.Scope;
    public VideoQueue(VideoRequest request)
    {
        this.request = request;
        if (request.Filter != null)
        {
            catalog = new Catalog(readOnly: true); filter = request.Filter with { Kind = "video" };
            Count = catalog.Browse(filter, 0, 1).Total; Index = catalog.BrowseIndex(filter, request.Id);
            if (Index < 0 || Count == 0) { catalog.Dispose(); throw new IOException("这个视频已不在当前浏览范围，请刷新媒体库后重试。"); }
        }
        else { Count = 1; Index = 0; }
    }
    public string CurrentPath => catalog == null ? request.Path : catalog.Browse(filter!, Index, 1).Items.FirstOrDefault()?.Path ?? throw new IOException("当前视频已不在媒体库中。");
    public void Dispose() => catalog?.Dispose();
}

// One isolated player per library window; further opens replace its current media.
internal sealed class VideoLauncher
{
    readonly SemaphoreSlim gate = new(1);
    readonly string pipe = "shiguang-video-" + Guid.NewGuid().ToString("N");
    Process? process;
    public event Action? Returned;
    public async Task Open(VideoRequest request)
    {
        await gate.WaitAsync();
        try
        {
            if (process is { HasExited: false })
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                // Never launch another player if the existing one has not acknowledged an open.
                using var client = await VideoPipeClient.ConnectAsync(pipe, timeout.Token);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, leaveOpen: true);
                await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
                if (await reader.ReadLineAsync(timeout.Token) != "ok") throw new IOException("播放窗口未能接收视频，请关闭播放窗口后重试。");
                return;
            }
            string folder = System.IO.Path.Combine(AppPaths.Data, "playback"); Directory.CreateDirectory(folder);
            string initial = System.IO.Path.Combine(folder, "request-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(initial, JsonSerializer.Serialize(request));
            try
            {
                var start = ChildProcess.StartInfo("--video-request", initial, "--player-pipe", pipe, "--data", AppPaths.Data); start.CreateNoWindow = false;
                process = Process.Start(start) ?? throw new IOException("无法打开视频播放窗口。");
                process.EnableRaisingEvents = true; process.Exited += (_, _) => Returned?.Invoke();
            }
            catch { File.Delete(initial); throw; }
        }
        finally { gate.Release(); }
    }
}

internal static class VideoPipeClient
{
    public static async Task<NamedPipeClientStream> ConnectAsync(string pipeName, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await client.ConnectAsync(300, cancellationToken);
                return client;
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or TimeoutException)
            {
                lastError = error;
                client.Dispose();
            }
            await Task.Delay(60, cancellationToken);
        }
        throw new IOException("播放窗口通信通道未就绪，请稍后重试。", lastError);
    }
}

internal sealed record PlaybackPreferences(double Volume = .6, bool Muted = false)
{
    static string FileName => System.IO.Path.Combine(AppPaths.Data, "playback-preferences.json");
    public static PlaybackPreferences Read()
    {
        try { var p = JsonSerializer.Deserialize<PlaybackPreferences>(File.ReadAllText(FileName)); return p != null && double.IsFinite(p.Volume) ? p with { Volume = Math.Clamp(p.Volume, 0, 1) } : new(); }
        catch { return new(); }
    }
    public void Save()
    {
        string temporary = FileName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, FileName, true);
    }
}
