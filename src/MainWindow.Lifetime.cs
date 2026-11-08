using System.Windows;

namespace Shiguang;

public partial class MainWindow
{
    TrayController? tray;
    bool exitRequested, sessionEnding, lifetimeEnabled;
    WindowState previousState = WindowState.Normal;
    public bool HasTray => tray?.IsAvailable == true;
    public bool IsInBackground => HasTray && !IsVisible;
    public void EnableTray()
    {
        if (tray != null) return;
        lifetimeEnabled = true;
        try { tray = new TrayController(this); }
        catch (Exception error) { LibraryService.Log("系统托盘", "", error); Notification.Show("托盘图标创建失败，关闭窗口前会保留退出确认。", error: true); }
    }
    public void RestoreFromTray()
    {
        if (exitRequested || Dispatcher.HasShutdownStarted) return;
        ShowInTaskbar = true;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = previousState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        Activate();
    }
    public void HideToTray()
    {
        if (!HasTray || exitRequested) return;
        if (WindowState != WindowState.Minimized) previousState = WindowState;
        ShowInTaskbar = false; Hide(); tray!.NotifyHidden();
    }
    public void OpenSettings(string page = "appearance")
    {
        var existing = OwnedWindows.OfType<SettingsWindow>().FirstOrDefault(w => w.IsVisible);
        if (existing != null) { existing.SelectPage(page); existing.Activate(); return; }
        var settings = new SettingsWindow(this, catalog) { Owner = this }; settings.SelectPage(page); settings.ShowDialog();
    }
    public void ToggleTaskPause() { if (CanPause) Pause_Click(this, new RoutedEventArgs()); }
    public void RequestExit(bool confirm = true)
    {
        if (exitRequested) return;
        if (busy && confirm)
        {
            RestoreFromTray();
            if (!Dialogs.Confirm(this, "停止当前任务并退出？", StatusText + "\n\n已保存的索引和相册会保留。任务将安全停止，之后可增量重新扫描。", "停止并退出")) return;
        }
        exitRequested = true; Close();
    }
    internal void PrepareSessionEnd()
    {
        sessionEnding = exitRequested = true;
        cancellation?.Cancel(); activePause?.Set(); indexer.Pause.Set();
        StopTray();
    }
    internal void StopTray() { tray?.Dispose(); tray = null; }
    void Exit_Click(object sender, RoutedEventArgs e) => RequestExit();
}
