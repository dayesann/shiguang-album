using System.Windows;
using System.Windows.Threading;

namespace Shiguang;

public partial class MainWindow
{
    static readonly System.Net.Http.HttpClient UpdateHttp = PortableUpdates.CreateClient();
    PortableUpdates? updater;
    UpdateRelease? availableUpdate;
    DispatcherTimer? updateTimer;
    CancellationTokenSource? downloadCancellation;
    readonly CancellationTokenSource updateLifetime = new();
    bool checkingUpdates, updateReady, promptedAboutUpdate;
    string updateStatus = "启动后会自动检查，也可手动检查。";
    internal event Action? UpdateChanged;
    internal string UpdateStatus => updateStatus;
    internal string UpdateAction => downloadCancellation != null ? "取消下载" :
        updateReady ? "重启并更新" : availableUpdate != null ? "下载更新" : "检查更新";
    internal bool CanUseUpdateAction => updater != null && !checkingUpdates;
    internal bool CanSetAutoDownload => updater != null;
    internal bool AutoDownloadEnabled { get; private set; }
    internal string? RestartForUpdate { get; private set; }

    internal void ConfigureUpdates(string home, string baseDirectory, bool enabled, System.Net.Http.HttpClient? client = null)
    {
        if (!enabled || !string.Equals(Path.GetFileName(baseDirectory),
            PortableUpdates.FolderName(Version.Parse(AppInfo.Version)), StringComparison.OrdinalIgnoreCase))
        {
            updateStatus = "请使用完整解压的 Windows 便携包检查和安装更新。";
            return;
        }
        updater = new(client ?? UpdateHttp, home, Version.Parse(AppInfo.Version));
        AutoDownloadEnabled = updater.ReadAutoDownload();
        SetUpdateStatus("启动后会自动检查，也可手动检查。");
        if (client != null) return; // Isolated WPF checks drive the update action directly.
        updateTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = TimeSpan.FromHours(6);
            await CheckUpdatesAsync(manual: false);
            NotifyAvailableUpdate();
        };
        Loaded += (_, _) => updateTimer.Start();
        IsVisibleChanged += (_, _) => NotifyAvailableUpdate();
        Activated += (_, _) => NotifyAvailableUpdate();
    }

    internal void StopUpdates()
    {
        updateTimer?.Stop();
        updateLifetime.Cancel();
    }

    void SetUpdateStatus(string status)
    {
        updateStatus = status;
        UpdateChanged?.Invoke();
    }

    void NotifyAvailableUpdate()
    {
        if (availableUpdate == null || promptedAboutUpdate || !IsVisible || WindowState == WindowState.Minimized ||
            Notification.IsVisible || OwnedWindows.OfType<SettingsWindow>().Any(settings => settings.IsVisible)) return;
        promptedAboutUpdate = true;
        Notification.Show($"拾光照片 {availableUpdate.Version} 已发布。", "查看更新", () => OpenSettings("about"));
    }

    internal async Task CheckUpdatesAsync(bool manual)
    {
        if (updater == null || checkingUpdates || availableUpdate != null || updateLifetime.IsCancellationRequested) return;
        checkingUpdates = true; SetUpdateStatus("正在检查更新…");
        try
        {
            availableUpdate = await updater.CheckAsync(updateLifetime.Token);
            updateReady = availableUpdate != null && updater.IsPrepared(availableUpdate);
            SetUpdateStatus(updateReady ? "新版已准备就绪，重启后生效。" : availableUpdate is { } update
                ? $"发现新版 {update.Version} · {Photo.FormatSize(update.Bytes)}。下载完成后可选择重启。"
                : "当前已是最新版本。");
            if (!manual) NotifyAvailableUpdate();
        }
        catch (OperationCanceledException) when (updateLifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            SetUpdateStatus(UpdateError(e, "无法检查更新，请检查网络后重试。"));
        }
        finally { checkingUpdates = false; UpdateChanged?.Invoke(); }
        if (AutoDownloadEnabled && !updateReady && availableUpdate is { } release && !updateLifetime.IsCancellationRequested)
            await DownloadUpdateAsync(release);
    }

    internal async Task SetAutoDownloadAsync(bool enabled)
    {
        if (updater == null) return;
        try { updater.SetAutoDownload(enabled); AutoDownloadEnabled = enabled; }
        catch (Exception e) { SetUpdateStatus(UpdateError(e, "未能保存更新设置。")); return; }
        UpdateChanged?.Invoke();
        if (enabled && !checkingUpdates && !updateReady && availableUpdate is { } release)
            await DownloadUpdateAsync(release);
    }

    internal async Task UseUpdateActionAsync(Window owner)
    {
        if (updater == null || checkingUpdates || updateLifetime.IsCancellationRequested) return;
        if (downloadCancellation != null) { downloadCancellation.Cancel(); return; }
        if (availableUpdate is not { } release) { await CheckUpdatesAsync(manual: true); return; }
        if (updateReady)
        {
            if (IsBusy) { SetUpdateStatus("新版已下载。请等待当前媒体库任务结束，再重启更新。"); return; }
            try
            {
                RestartForUpdate = updater.Activate(release);
            }
            catch (Exception e)
            {
                updateReady = updater.IsPrepared(release);
                SetUpdateStatus(UpdateError(e, "未能启用新版，可继续使用当前版本。")); return;
            }
            owner.Close();
            RequestExit(confirm: false);
            return;
        }
        await DownloadUpdateAsync(release);
    }

    async Task DownloadUpdateAsync(UpdateRelease release)
    {
        if (updater == null || downloadCancellation != null || updateLifetime.IsCancellationRequested) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
        downloadCancellation = cancellation;
        SetUpdateStatus("正在下载更新…");
        var progress = new Progress<int>(value =>
        {
            if (ReferenceEquals(downloadCancellation, cancellation) && !cancellation.IsCancellationRequested)
                SetUpdateStatus(value == 100 ? "下载完成，正在校验并准备新版…" : $"正在下载更新… {value}%");
        });
        try
        {
            await updater.DownloadAndStageAsync(release, progress, cancellation.Token);
            updateReady = true;
            SetUpdateStatus(IsBusy ? "新版已下载。请等待当前媒体库任务结束，再重启更新。" : "新版已准备就绪，重启后生效。");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { SetUpdateStatus("已取消下载，可重新下载。"); }
        catch (Exception e) { SetUpdateStatus(UpdateError(e, "下载或安装失败，请重试。")); }
        finally { downloadCancellation = null; UpdateChanged?.Invoke(); }
    }

    static string UpdateError(Exception error, string fallback) =>
        error is IOException && error.Message.Any(c => c is >= '\u4e00' and <= '\u9fff') &&
        error is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException)
            ? error.Message : fallback;
}
