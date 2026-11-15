using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shiguang;

internal sealed record UpdateRelease(Version Version, Uri Download, long Bytes, string Sha256);

internal sealed class PortableUpdates(HttpClient http, string home, Version current)
{
    // The main repository hosts both source and stable releases.
    internal const string DownloadsRepo = "dayesann/shiguang-album";
    const string Executable = "拾光照片.exe";
    const string Pointer = ".shiguang-current-version";
    const string Receipt = ".shiguang-package-sha256";
    const string AutoDownload = ".shiguang-auto-download";
    const long MaxZipBytes = 256 * 1024 * 1024;
    const long MaxProgramBytes = 768 * 1024 * 1024;
    internal static string FolderName(Version version) => $"运行程序-{version.ToString(3)}";
    static string PackageName(Version version) => $"ShiguangAlbum-{version.ToString(3)}-win-x64";
    static readonly Regex StableVersion = new(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant);

    internal static Version? ParseVersion(string? value) =>
        value is { Length: > 0 and <= 32 } && StableVersion.IsMatch(value) && Version.TryParse(value, out var version) ? version : null;

    internal bool ReadAutoDownload() => File.Exists(Path.Combine(home, AutoDownload));

    internal void SetAutoDownload(bool enabled)
    {
        EnsurePlainDirectory(home);
        string path = Path.Combine(home, AutoDownload);
        if (enabled && !File.Exists(path))
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        else if (!enabled) File.Delete(path);
    }

    internal static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ShiguangAlbum/" + AppInfo.Version);
        return client;
    }

    internal async Task<UpdateRelease?> CheckAsync(CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{DownloadsRepo}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new IOException("更新渠道暂不可用，请稍后再试。");
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBoundedAsync(response.Content, 1024 * 1024, limit.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString();
        var version = tag?.StartsWith('v') == true ? ParseVersion(tag[1..]) : null;
        if (version == null) throw new IOException("更新渠道返回的版本号无效。");
        if (version <= current) return null;
        string name = PackageName(version) + ".zip";
        string expectedUrl = $"https://github.com/{DownloadsRepo}/releases/download/{tag}/{name}";
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name) continue;
            if (asset.GetProperty("state").GetString() == "uploaded" && asset.GetProperty("browser_download_url").GetString() == expectedUrl &&
                asset.GetProperty("size").TryGetInt64(out long size) && size is > 0 and <= MaxZipBytes &&
                asset.TryGetProperty("digest", out var digestValue) && digestValue.GetString() is { Length: 71 } digest &&
                digest.StartsWith("sha256:", StringComparison.Ordinal) && digest.AsSpan(7).ToString().All(Uri.IsHexDigit))
                return new(version, new Uri(expectedUrl), size, digest[7..]);
            break;
        }
        throw new IOException("新版暂没有可校验的 Windows 更新包，请稍后再试。");
    }

    static async Task<byte[]> ReadBoundedAsync(HttpContent content, int max, CancellationToken token)
    {
        using var input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384];
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > max) throw new IOException("更新信息超出允许大小。");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    internal async Task DownloadAndStageAsync(UpdateRelease release, IProgress<int>? progress, CancellationToken token)
    {
        home = Path.GetFullPath(home);
        EnsurePlainDirectory(home);
        if (release.Version <= current) throw new IOException("更新版本必须比当前版本新。");
        string destination = Path.Combine(home, FolderName(release.Version));
        string stage = Path.Combine(home, ".shiguang-update-" + Guid.NewGuid().ToString("N"));
        // A single installer can prepare this portable copy at a time.
        using var installLock = OpenInstallLock(home);
        if (IsPrepared(release)) return;
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("新版程序目录已存在。请手动打开新版，或将现有目录移到别处后重试。");
        Directory.CreateDirectory(stage);
        try
        {
            var zip = Path.Combine(stage, "package.zip");
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(TimeSpan.FromMinutes(15));
            await DownloadAsync(release, zip, progress, limit.Token).ConfigureAwait(false);
            await Task.Run(() => ExtractProgram(zip, stage, release.Version, limit.Token), limit.Token).ConfigureAwait(false);
            limit.Token.ThrowIfCancellationRequested();
            string program = Path.Combine(stage, "program");
            File.WriteAllText(Path.Combine(program, Receipt), release.Sha256);
            Directory.Move(program, destination);
        }
        finally
        {
            // 'stage' is a new GUID directory constructed directly under the checked home.
            try
            {
                if (Path.GetDirectoryName(Path.GetFullPath(stage)) == Path.TrimEndingDirectorySeparator(home))
                {
                    EnsurePlainDirectory(stage);
                    Directory.Delete(stage, recursive: true);
                }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal bool IsPrepared(UpdateRelease release)
    {
        try
        {
            string program = Path.Combine(home, FolderName(release.Version));
            ValidateProgram(program, release.Version);
            var receipt = new FileInfo(Path.Combine(program, Receipt));
            return receipt.Exists && receipt.Length == 64 && !receipt.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
                File.ReadAllText(receipt.FullName).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException) { return false; }
    }

    async Task DownloadAsync(UpdateRelease release, string zip, IProgress<int>? progress, CancellationToken token)
    {
        Uri url = release.Download;
        for (int hop = 0; hop < 4; hop++)
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if (response.Headers.Location is not { IsAbsoluteUri: true } next || next.Scheme != "https" ||
                    !next.IsDefaultPort || next.UserInfo.Length != 0 ||
                    next.IdnHost is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new IOException("更新下载跳转到了未知地址。");
                url = next;
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long expected && expected != release.Bytes)
                throw new IOException("更新包大小不匹配。");
            using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var output = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long total = 0;
            int lastPercent = -1;
            int read;
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > release.Bytes) throw new IOException("更新包大小不匹配。");
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                int percent = (int)(total * 100 / release.Bytes);
                if (percent != lastPercent) { progress?.Report(percent); lastPercent = percent; }
            }
            if (total != release.Bytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("更新包校验失败，未安装新版。");
            return;
        }
        throw new IOException("更新下载跳转次数过多。");
    }

    static void ExtractProgram(string file, string stage, Version version, CancellationToken token)
    {
        string prefix = $"{PackageName(version)}/{FolderName(version)}/";
        string program = Path.Combine(stage, "program");
        Directory.CreateDirectory(program);
        using var archive = ZipFile.OpenRead(file);
        if (archive.Entries.Count > 10000) throw new IOException("更新包包含过多文件。");
        long expanded = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name == prefix) continue;
            var parts = name[prefix.Length..].TrimEnd('/').Split('/');
            if (parts.Any(UnsafeSegment) || (parts[0] is not (".runtime" or "Assets") &&
                (parts.Length != 1 || parts[0] is not (Executable or "拾光照片.dll" or "拾光照片.deps.json" or "拾光照片.runtimeconfig.json"))))
                throw new IOException("更新包包含未知或无效的程序路径。");
            string target = Path.Combine(program, Path.Combine(parts));
            if (!paths.Add(target) || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new IOException("更新包包含重复路径或链接。");
            if (name.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            expanded = checked(expanded + entry.Length);
            if (expanded > MaxProgramBytes) throw new IOException("更新包解压后超出允许大小。");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew);
            byte[] buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                token.ThrowIfCancellationRequested();
                written += read;
                if (written > entry.Length) throw new IOException("更新包文件长度不匹配。");
                output.Write(buffer, 0, read);
            }
            if (written != entry.Length) throw new IOException("更新包文件长度不匹配。");
        }
        ValidateProgram(program, version);
    }

    static bool UnsafeSegment(string part)
    {
        if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
            part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return true;
        string stem = part.Split('.')[0].TrimEnd().ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "0123456789¹²³".Contains(stem[3]);
    }

    static string ValidateProgram(string program, Version version)
    {
        EnsurePlainDirectory(program);
        foreach (var file in new[] { Executable, "拾光照片.dll", "拾光照片.deps.json", "拾光照片.runtimeconfig.json" })
            if (!File.Exists(Path.Combine(program, file)) || File.GetAttributes(Path.Combine(program, file)).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("更新包缺少完整的程序文件。");
        EnsurePlainDirectory(Path.Combine(program, "Assets"));
        EnsurePlainDirectory(Path.Combine(program, ".runtime"));
        if (!File.Exists(Path.Combine(program, ".runtime", "dotnet.exe")) ||
            AssemblyName.GetAssemblyName(Path.Combine(program, "拾光照片.dll")).Version?.ToString(3) != version.ToString(3))
            throw new IOException("更新包的程序版本或运行时不匹配。");
        return Path.Combine(program, Executable);
    }

    static void EnsurePlainDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("更新需要普通的本地程序目录。");
    }

    static FileStream OpenInstallLock(string home) => new(Path.Combine(home, ".shiguang-update.lock"),
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    internal static string? ActiveExecutable(string home, Version current)
    {
        try
        {
            var marker = new FileInfo(Path.Combine(home, Pointer));
            if (!marker.Exists || marker.Length > 40 || marker.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            var version = ParseVersion(File.ReadAllText(marker.FullName).Trim());
            return version > current ? ValidateProgram(Path.Combine(home, FolderName(version)), version) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException) { return null; }
    }

    internal string Activate(UpdateRelease release)
    {
        EnsurePlainDirectory(home);
        using var installLock = OpenInstallLock(home);
        var version = release.Version;
        if (version <= current || !IsPrepared(release)) throw new IOException("新版程序尚未下载完整，请重新下载。");
        if (ActiveExecutable(home, version) is string newer) return newer;
        string executable = Path.Combine(home, FolderName(version), Executable);
        string marker = Path.Combine(home, Pointer);
        string temporary = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, version.ToString(3)); File.Move(temporary, marker, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return executable;
    }

    internal static void Launch(string exe, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var child = Process.Start(start) ?? throw new IOException("无法启动新版程序。");
    }
}
