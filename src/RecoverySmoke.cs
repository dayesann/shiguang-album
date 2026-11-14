using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Shiguang;

public static class RecoverySmoke
{
    static string original = "", hash = "", movedId = "";
    public static void Prepare(Catalog catalog)
    {
        if (catalog.All().Count > 0) throw new IOException("恢复验收需要空的隔离媒体库。");
        var root = Path.Combine(AppPaths.Data, "generated-source"); Directory.CreateDirectory(root);
        foreach (bool moved in new[] { true, false })
        {
            var path = Path.Combine(root, moved ? "moved.jpg" : "untouched.jpg"); SelfTest.CreateImage(path, moved ? 1 : 2, true);
            var photo = Imaging.Read(path, root); catalog.Upsert(photo); catalog.AddRoot(root);
            var target = Path.Combine(root, ".shiguang-trash", photo.Id + ".jpg"); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            catalog.Prepare(new(Guid.NewGuid().ToString("N"), "recovery-smoke", "trash", photo.Id, path, target, LibraryService.HashFile(path), "Prepared", "", DateTime.UtcNow.ToString("O"), root, root));
            if (moved) { movedId = photo.Id; File.Move(path, target); }
            else { original = path; hash = LibraryService.HashFile(path); }
        }
    }
    public static async Task Run(MainWindow main, Catalog catalog)
    {
        var checks = new List<string>(); string? failure = null;
        void Check(bool value, string name) { if (!value) throw new IOException(name); checks.Add(name); }
        try
        {
            await Task.Delay(400);
            Check(catalog.ById(movedId)?.Status == "trash", "real window startup reconciles already finished move");
            Check(catalog.Operations().Count == 1 && !main.CanScan && !main.IsBusy, "pending task blocks scan and automatic enrichment");
            Check(main.Notification.IsVisible, "startup exposes recovery notice");
            foreach (var theme in new[] { "light", "dark", "system" })
            {
                ThemeManager.SetPreference(theme);
                main.Width = 1150; main.Height = 750;
                await Task.Delay(300); UiSmoke.Save(main, "recovery-main-" + theme + ".png");
                var settings = new SettingsWindow(main, catalog) { Owner = main }; settings.Show(); settings.SelectPage("library");
                await Task.Delay(300);
                Check(UiSmoke.Descendants<Button>(settings).Any(b => AutomationProperties.GetName(b) == "任务恢复"), theme + " settings exposes recovery action");
                UiSmoke.Save(settings, "recovery-settings-" + theme + ".png"); settings.Close();
                var review = new RecoveryWindow(catalog.Operations()) { Owner = main }; review.Show();
                await Task.Delay(250); UiSmoke.Save(review, "recovery-review-" + theme + ".png"); review.Close();
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            bool captured = false, safeFocus = false;
            timer.Tick += (_, _) =>
            {
                var review = Application.Current.Windows.OfType<RecoveryWindow>().FirstOrDefault(w => w.IsVisible);
                if (review == null) return;
                timer.Stop(); captured = true;
                safeFocus = UiSmoke.Descendants<Button>(review).Any(b => AutomationProperties.GetName(b) == "关闭" && b.IsKeyboardFocused);
                UiSmoke.Save(review, "recovery-review.png");
                UiSmoke.Descendants<Button>(review).Single(b => AutomationProperties.GetName(b) == "取消未开始任务").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try { await main.RecoverTasks(); } finally { timer.Stop(); }
            Check(captured && safeFocus, "review shows operation scope and defaults focus to safe close");
            Check(catalog.Operations().Count == 0 && main.CanScan, "cancel untouched through product flow unblocks scanning");
            Check(File.Exists(original) && LibraryService.HashFile(original) == hash, "recovery cancel preserves original bytes");
            Check(File.Exists(catalog.ById(movedId)!.Path), "reconciled trash remains recoverable");
        }
        catch (Exception e) { failure = e.ToString(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "recovery-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Failure = failure, InputLimit = "Programmatic WPF events; not physical keyboard or DPI certification" }, new JsonSerializerOptions { WriteIndented = true }));
        main.ExitCode = failure == null ? 0 : 1;
        Application.Current.Shutdown(main.ExitCode);
    }
}
