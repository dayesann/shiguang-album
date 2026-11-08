using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Shiguang;

public static class TrayTests
{
    public static async Task Run(MainWindow main, Catalog catalog)
    {
        var checks = new List<string>(); string? failure = null;
        var originalClose = main.SafeClose;
        Dictionary<string, string> originals = [];
        int expected = 0;
        void Check(bool pass, string name) { if (!pass) throw new IOException(name); checks.Add(name); }
        async Task Settle() { await Task.Delay(250); main.UpdateLayout(); }
        void SaveResult() => File.WriteAllText(Path.Combine(AppPaths.Data, "tray-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Failure = failure, ExpectedItems = expected }, new JsonSerializerOptions { WriteIndented = true }));
        async Task ActivateAgain()
        {
            using var child = new Process { StartInfo = new(Environment.ProcessPath!) { UseShellExecute = false } };
            foreach (var value in new[] { "--data", AppPaths.Data, "--activation-probe" }) child.StartInfo.ArgumentList.Add(value);
            child.Start(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { await child.WaitForExitAsync(timeout.Token); }
            catch { if (!child.HasExited) child.Kill(true); throw; }
            Check(child.ExitCode == 0, "second process activates the locked library and exits successfully"); await Settle();
        }
        void ConfirmNext(bool accept, string? screenshot = null)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().FirstOrDefault(w => w.IsVisible);
                if (dialog == null) return; timer.Stop();
                if (screenshot != null) UiSmoke.Save(dialog, screenshot);
                dialog.DialogResult = accept;
            }; timer.Start();
        }
        try
        {
            while (main.IsBusy) await Task.Delay(50); await Settle();
            Check(main.HasTray && WindowBehaviorSettings.CloseToTray, "tray exists and close-to-tray defaults on");
            ThemeManager.SetPreference("light");
            var settings = new SettingsWindow(main, catalog) { Owner = main }; settings.Show(); settings.SelectPage("general"); await Settle();
            var option = UiSmoke.Descendants<CheckBox>(settings).Single(c => AutomationProperties.GetName(c) == "关闭窗口时最小化到系统托盘");
            Check(option.IsChecked == true, "general settings exposes the enabled background option");
            UiSmoke.Save(settings, "01-常规设置-浅色.png");
            option.IsChecked = false; option.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(!WindowBehaviorSettings.CloseToTray && ThemeManager.ReadPreference() == "light", "window preference persists independently of the selected theme");
            option.IsChecked = true; option.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            ThemeManager.SetPreference("dark"); await Settle(); UiSmoke.Save(settings, "02-常规设置-深色.png");
            ThemeManager.SetPreference("system"); await Settle();
            Check(WindowBehaviorSettings.CloseToTray, "window preference survives light dark and system changes");
            settings.Close();
            var root = AppPaths.Data + "-source"; Directory.CreateDirectory(root);
            var first = Path.Combine(root, "后台扫描-20200101-000.jpg"); SelfTest.CreateImage(first, 1, false);
            for (int i = 1; i < 240; i++) File.Copy(first, Path.Combine(root, $"后台扫描-20200101-{i:D3}.jpg"));
            originals = Directory.GetFiles(root).ToDictionary(p => p, LibraryService.HashFile);
            expected = originals.Count;
            var scan = main.ScanDirectories([root]); main.ToggleTaskPause(); await Settle();
            Check(main.IsBusy && main.PauseText == "继续" && !scan.IsCompleted, "real indexing task can be paused before a close request");
            var handle = new WindowInteropHelper(main).Handle;
            main.Close(); await Settle();
            Check(main.IsInBackground && main.IsBusy && !scan.IsCompleted, "closing while indexing hides the window without canceling the task");
            Check(!main.ShowInTaskbar, "hidden window is removed from the taskbar");
            main.ToggleTaskPause();
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50))) await scan.WaitAsync(timeout.Token);
            Check(main.IsInBackground && !main.IsBusy && catalog.Count() == expected && catalog.PendingPreviews().Count == 0, "real indexing and previews complete while the window stays hidden");
            await ActivateAgain();
            Check(main.IsVisible && !main.IsInBackground && new WindowInteropHelper(main).Handle == handle, "relaunch restores the same window with the completed index");
            UiSmoke.Save(main, "03-后台任务完成后恢复.png");
            main.Close(); await Settle();
            Check(await Task.Run(() => LibraryInstance.TryRestoreThroughLibrary(AppPaths.Data)), "library fallback acknowledges activation without window enumeration");
            await Settle(); Check(main.IsVisible && !main.IsInBackground, "library fallback restores the hidden window");
            main.WindowState = WindowState.Maximized; await Settle(); main.Close(); await Settle();
            Check(main.IsInBackground && !main.IsBusy, "closing an idle window also keeps the tray process alive");
            await ActivateAgain();
            Check(main.IsVisible && main.WindowState == WindowState.Maximized, "tray restoration preserves maximized state");
            main.WindowState = WindowState.Normal;
            var again = main.RescanSources(); main.ToggleTaskPause(); await Settle();
            main.Close(); await Settle(); await ActivateAgain();
            Check(main.IsVisible && main.IsBusy && main.PauseText == "继续" && !again.IsCompleted, "relaunch also preserves a paused background task");
            WindowBehaviorSettings.SetCloseToTray(false);
            ConfirmNext(false, "04-任务中退出确认.png");
            main.Close(); await Settle();
            Check(main.IsVisible && main.IsBusy && !again.IsCompleted, "canceling exit with close-to-tray off leaves the task running");
            WindowBehaviorSettings.SetCloseToTray(true);
            main.SafeClose = code =>
            {
                try
                {
                    Check(!main.IsBusy && !main.HasTray, "explicit exit waits for task cancellation and disposes the tray");
                    Check(catalog.Count() == expected, "explicit exit preserves committed index rows");
                    Check(originals.All(p => File.Exists(p.Key) && LibraryService.HashFile(p.Key) == p.Value), "background close restore and exit never modify original media");
                    catalog.Backup(Path.Combine(AppPaths.Data, "persisted-index.sqlite"));
                }
                catch (Exception error) { failure = error.ToString(); }
                SaveResult(); originalClose?.Invoke(failure == null ? 0 : 1);
            };
            ConfirmNext(true); main.RequestExit();
            await again;
        }
        catch (Exception error)
        {
            failure = error.ToString(); SaveResult(); main.SafeClose = originalClose;
            main.ExitCode = 1; main.RequestExit(false);
        }
    }
    public static int Verify()
    {
        string? failure = null; int count = 0;
        try
        {
            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppPaths.Data, "tray-results.json")));
            if (!result.RootElement.GetProperty("Passed").GetBoolean()) throw new IOException("托盘流程未通过");
            int expected = result.RootElement.GetProperty("ExpectedItems").GetInt32();
            using var catalog = new Catalog(true); count = catalog.Count();
            if (count != expected || !WindowBehaviorSettings.CloseToTray) throw new IOException("重启后索引或窗口偏好未保留");
        }
        catch (Exception error) { failure = error.ToString(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "tray-restart-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, IndexedItems = count, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null ? 0 : 1;
    }
}
