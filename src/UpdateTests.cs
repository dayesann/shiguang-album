using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Shiguang;

internal static class UpdateTests
{
    internal sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }

    internal static HttpResponseMessage Metadata(Version version, long size, string hash, bool pre = false)
    {
        string zip = $"ShiguangAlbum-{version}-win-x64.zip";
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
        {
            tag_name = $"v{version}", draft = false, prerelease = pre,
            assets = new[] { new { name = zip, state = "uploaded", size, digest = $"sha256:{hash}",
                browser_download_url = $"https://github.com/{PortableUpdates.DownloadsRepo}/releases/download/v{version}/{zip}" } }
        })) };
    }

    internal static async Task<int> Run(string? package)
    {
        var checks = new List<string>();
        string? failure = null;
        void Check(bool accepted, string name) { if (!accepted) throw new IOException(name); checks.Add(name); }
        async Task Reject(Func<Task> run, string name)
        {
            try { await run(); }
            catch (IOException) { checks.Add(name); return; }
            throw new IOException("Expected rejection: " + name);
        }
        try
        {
            var version = Version.Parse(AppInfo.Version);
            var previous = new Version(0, 0, 0);
            string home = Path.Combine(AppPaths.Data, "install");
            var old = Path.Combine(home, PortableUpdates.FolderName(previous));
            Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old, "old.txt"), "old");
            var photo = Path.Combine(home, "original-photo.jpg");
            File.WriteAllText(photo, "original");
            var library = Path.Combine(AppPaths.Data, "library-sentinel");
            File.WriteAllText(library, "library");

            byte[] Archive(bool traversal)
            {
                using var stream = new MemoryStream();
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var name = $"ShiguangAlbum-{version}-win-x64/{PortableUpdates.FolderName(version)}/";
                    foreach (string file in new[] { "拾光照片.exe", "拾光照片.dll", "拾光照片.deps.json", "拾光照片.runtimeconfig.json" })
                        zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, file), name + file);
                    zip.CreateEntry(name + "Assets/");
                    zip.CreateEntry(name + ".runtime/dotnet.exe");
                    if (traversal) zip.CreateEntry(name + "../../original-photo.jpg");
                    zip.CreateEntry($"ShiguangAlbum-{version}-win-x64/数据/not-installed.txt");
                }
                return stream.ToArray();
            }
            var body = package == null ? Archive(false) : File.ReadAllBytes(package);
            var digest = Convert.ToHexString(SHA256.HashData(body));
            bool askedMainRepository = false;
            string? digestOverride = null;
            bool prerelease = false, foreign = false, offline = false;
            TaskCompletionSource? downloadInProgress = null;
            int downloads = 0;
            using var client = new HttpClient(new Stub(async (request, token) =>
            {
                var uri = request.RequestUri!;
                if (offline) throw new HttpRequestException("offline");
                if (uri.Host == "api.github.com")
                {
                    askedMainRepository = uri.AbsolutePath == "/repos/dayesann/shiguang-album/releases/latest";
                    return Metadata(version, body.Length, digestOverride ?? digest, prerelease);
                }
                if (uri.Host == "github.com")
                {
                    var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                    response.Headers.Location = new Uri(foreign ? "https://unrelated.invalid/package.zip" : "https://release-assets.githubusercontent.com/package.zip");
                    return response;
                }
                downloads++;
                if (downloadInProgress is { } started)
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            }));
            var updater = new PortableUpdates(client, home, previous);
            using var token = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Check(await new PortableUpdates(client, home, version).CheckAsync(token.Token) == null, "current version is not offered again");
            prerelease = true;
            Check(await updater.CheckAsync(token.Token) == null, "prereleases are ignored");
            prerelease = false;
            offline = true;
            try { await updater.CheckAsync(token.Token); throw new IOException("expected network error"); }
            catch (HttpRequestException) { checks.Add("offline check fails without writing to the installation"); }
            offline = false;
            digestOverride = "missing";
            await Reject(() => updater.CheckAsync(token.Token), "a release without a valid SHA-256 digest is rejected");
            digestOverride = null;
            var release = (await updater.CheckAsync(token.Token))!;
            Check(release.Version == version, "new stable version and official package are detected");
            Check(askedMainRepository && release.Download.AbsolutePath.StartsWith("/dayesann/shiguang-album/releases/download/", StringComparison.Ordinal),
                "version lookup and asset both use the main repository");
            foreign = true;
            await Reject(() => updater.DownloadAndStageAsync(release, null, token.Token), "download rejects an unrelated redirect");
            Check(downloads == 0, "foreign host never receives a request");
            foreign = false;
            downloadInProgress = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(token.Token))
            {
                var started = updater.DownloadAndStageAsync(release, null, cancel.Token);
                await downloadInProgress.Task.WaitAsync(token.Token);
                cancel.Cancel();
                try { await started; throw new IOException("expected cancellation"); }
                catch (OperationCanceledException) { checks.Add("a running download can be cancelled"); }
            }
            downloadInProgress = null;
            await Reject(() => updater.DownloadAndStageAsync(release with { Sha256 = new string('0', 64) }, null, token.Token), "corrupt package is rejected");
            var traversal = Archive(true);
            var validBody = body;
            body = traversal;
            await Reject(() => updater.DownloadAndStageAsync(release with { Bytes = traversal.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(traversal)) }, null, token.Token), "ZIP path traversal is rejected even with a matching hash");
            body = validBody;
            Check(!Directory.Exists(Path.Combine(home, PortableUpdates.FolderName(version))) &&
                !Directory.EnumerateDirectories(home, ".shiguang-update-*").Any(), "failed downloads leave neither an installed version nor a staging directory");
            await updater.DownloadAndStageAsync(release, null, token.Token);
            Check(updater.IsPrepared(release) && PortableUpdates.ActiveExecutable(home, previous) == null, "a verified download is reusable and waits for explicit activation");
            int before = downloads;
            await updater.DownloadAndStageAsync(release, null, token.Token);
            Check(downloads == before, "a prepared version is reused without overwriting the program or downloading again");
            var target = updater.Activate(release);
            Check(PortableUpdates.ActiveExecutable(home, previous) == target && PortableUpdates.ActiveExecutable(home, version) == null,
                "old shortcuts select the activated version without redirecting newest launch");
            Check(File.ReadAllText(photo) == "original" && File.ReadAllText(library) == "library" && File.Exists(Path.Combine(old, "old.txt")) &&
                !Directory.Exists(Path.Combine(home, "数据")), "install preserves source files, library and old program; ignores non-program ZIP entries");
        }
        catch (Exception error) { failure = error.ToString(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "update-results.json"),
            JsonSerializer.Serialize(new { Passed = failure == null, Count = checks.Count, Checks = checks, Failure = failure, UsedReleaseZip = package != null }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
}
