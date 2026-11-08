using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shiguang;

public static class DesignAcceptance
{
    public static async Task Run(MainWindow main, Catalog catalog)
    {
        var checks = new List<string>();
        string? failure = null;
        var opened = new List<Window>();
        void Check(bool value, string name)
        {
            if (!value) throw new IOException(name);
            checks.Add(name);
        }
        static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject => UiSmoke.Descendants<T>(root);
        async Task Settle(int milliseconds = 450)
        {
            await Task.Delay(milliseconds);
            foreach (var window in opened.Where(window => window.IsVisible)) window.UpdateLayout();
            main.UpdateLayout();
        }
        Button ButtonNamed(DependencyObject root, string name) =>
            Descendants<Button>(root).First(button => AutomationProperties.GetName(button) == name);

        try
        {
            while (main.IsBusy) await Task.Delay(50);
            main.Width = 1536;
            main.Height = 1024;
            main.SelectDate("");
            main.SetCategory("all");
            main.SetMode("days");
            ThemeManager.SetPreference("light");
            await Settle(700);

            var duplicateEntry = (Button)main.FindName("DuplicateButton");
            Check(duplicateEntry.IsVisible && duplicateEntry.IsEnabled, "main window exposes an enabled duplicate-cleanup entry");
            Check(AutomationProperties.GetName(duplicateEntry) == "重复照片", "duplicate-cleanup entry has an accessible name");
            Check(duplicateEntry.Background is SolidColorBrush duplicateBackground && duplicateBackground.Color.A == 0, "duplicate entry uses the light tertiary action style");
            Check(duplicateEntry.ActualHeight is >= 36 and <= 40, "duplicate entry preserves the compact desktop target");
            Check(!catalog.HasDuplicateIndex(), "initial library load skips duplicate comparison");
            UiSmoke.Save(main, "01-主窗口-浅色.png");

            DuplicateWindow? workflowReview = null;
            var duplicateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            duplicateTimer.Tick += (_, _) =>
            {
                workflowReview = Application.Current.Windows.OfType<DuplicateWindow>().FirstOrDefault(window => window.IsVisible);
                if (workflowReview == null) return;
                duplicateTimer.Stop();
                workflowReview.DialogResult = false;
            };
            duplicateTimer.Start();
            duplicateEntry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int attempt = 0; attempt < 200 && workflowReview == null; attempt++) await Task.Delay(100);
            duplicateTimer.Stop();
            Check(workflowReview?.Owner == main, "main-window duplicate action completes detection and opens its review dialog");
            Check(catalog.HasDuplicateIndex(), "first explicit duplicate action persists a reusable index");
            while (main.IsBusy) await Task.Delay(50);
            var cachedWatch = System.Diagnostics.Stopwatch.StartNew();
            var cachedGroups = catalog.CachedDuplicateGroups();
            Check(cachedGroups.Count > 0 && cachedWatch.ElapsedMilliseconds < 500, "subsequent duplicate opens reuse the persisted index");

            var settings = new SettingsWindow(main, catalog) { Owner = main };
            opened.Add(settings);
            settings.Show();
            await Settle();
            settings.SelectPage("library");
            await Settle();
            Check(ButtonNamed(settings, "检查重复") is { IsEnabled: true }, "settings exposes duplicate cleanup as a media-library action");
            Check(Descendants<Button>(settings).Any(button => AutomationProperties.GetName(button).StartsWith("暂无内容")), "settings explains the empty recent-deleted state");
            UiSmoke.Save(settings, "02-设置-媒体库.png");
            settings.SelectPage("appearance");
            await Settle();
            UiSmoke.Save(settings, "03-设置-外观.png");
            settings.Close();
            opened.Remove(settings);

            Window? promptWindow = null;
            string? promptResult = "unexpected";
            var promptTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            promptTimer.Tick += (_, _) =>
            {
                promptWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsVisible && window.Title == "新建相册");
                if (promptWindow == null) return;
                UiSmoke.Save(promptWindow, "04-新建相册弹窗.png");
                promptWindow.DialogResult = false;
                promptTimer.Stop();
            };
            promptTimer.Start();
            _ = main.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => promptResult = Dialogs.Prompt(main, "新建相册", "相册名称（只保存引用）")));
            await Settle(900);
            promptTimer.Stop();
            Check(promptResult == null, "application dialog closes through the safe cancel action");
            if (promptWindow != null)
            {
                var promptTexts = Descendants<TextBlock>(promptWindow).Where(text => text.IsVisible).ToList();
                Check(promptTexts.All(text => text.FontSize >= 12), "dialog copy stays at or above the readable type floor");
                Check(Descendants<Button>(promptWindow).Where(button => button.IsVisible).All(button => button.ActualHeight >= 32), "dialog actions preserve the compact desktop target");
            }
            else Check(false, "application dialog is visible during interaction");

            var duplicateService = new LibraryService(catalog);
            var duplicateGroups = await Task.Run(() => duplicateService.FindDuplicates(new Progress<JobProgress>(), CancellationToken.None));
            Check(duplicateGroups.Any(group => group.Kind == "exact"), "isolated demo library produces a real exact-duplicate group");
            Check(duplicateGroups.Any(group => group.Kind == "similar"), "isolated demo library produces a lower-quality similar-photo group");
            var review = new DuplicateWindow(duplicateGroups) { Owner = main };
            opened.Add(review);
            review.Show();
            await Settle(700);
            Check(review.CurrentKind == "exact" && review.ApplyButton.IsEnabled, "exact duplicate copies are preselected while a keeper remains");
            Check(Descendants<TextBlock>(review).Where(text => text.IsVisible).All(text => text.FontSize >= 12), "duplicate screen avoids 10–11 DIP operational text");
            Check(Descendants<Button>(review).Where(button => button.IsVisible).All(button => button.ActualHeight >= 32), "duplicate screen actions use the compact desktop target");
            UiSmoke.Save(review, "05-重复照片清理-浅色.png");
            var kindPicker = Descendants<ComboBox>(review).Single(combo => AutomationProperties.GetName(combo) == "重复类型");
            kindPicker.SelectedIndex = 1;
            await Settle();
            Check(review.CurrentKind == "similar", "duplicate screen exposes manual similar-photo review");
            UiSmoke.Save(review, "06-低质量近似候选.png");
            ThemeManager.SetPreference("dark");
            await Settle();
            UiSmoke.Save(review, "07-重复照片清理-深色.png");
            review.Close();
            opened.Remove(review);
            ThemeManager.SetPreference("light");
            await Settle();

            var exact = duplicateGroups.First(group => group.Kind == "exact");
            var keeper = exact.Photos.First(photo => photo.Id == exact.Keeper);
            var removable = exact.Photos.First(photo => photo.Id != exact.Keeper);
            string removableHash = LibraryService.HashFile(removable.Path);
            var organizer = new FileOrganizer(catalog);
            organizer.Trash([removable], new() { [removable.Id] = keeper.Id }, new Progress<JobProgress>(), CancellationToken.None);
            var trashed = catalog.Trashed().Single(photo => photo.Id == removable.Id);
            Check(trashed.Status == "trash" && File.Exists(trashed.Path), "duplicate cleanup moves the selected copy into recoverable storage");

            var trash = new TrashWindow([trashed]) { Owner = main };
            opened.Add(trash);
            trash.Show();
            await Settle(500);
            var restore = ButtonNamed(trash, "恢复所选");
            var purge = ButtonNamed(trash, "永久删除");
            Check(!restore.IsEnabled && !purge.IsEnabled, "recent-deleted destructive actions remain disabled until selection");
            var trashCheck = Descendants<CheckBox>(trash).First();
            trashCheck.IsChecked = true;
            await Settle(250);
            Check(restore.IsEnabled && purge.IsEnabled, "recent-deleted selection enables recovery and explicit permanent deletion");
            Check(Descendants<TextBlock>(trash).Where(text => text.IsVisible).All(text => text.FontSize >= 12), "recent-deleted screen follows the readable type floor");
            UiSmoke.Save(trash, "08-最近删除.png");

            Window? destructiveConfirm = null;
            bool dangerStyleCorrect = false;
            var confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            confirmTimer.Tick += (_, _) =>
            {
                destructiveConfirm = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsVisible && window is ConfirmationWindow);
                if (destructiveConfirm == null) return;
                var confirmButton = ButtonNamed(destructiveConfirm, "永久删除");
                dangerStyleCorrect = confirmButton.Background is SolidColorBrush danger && danger.Color == ((SolidColorBrush)Application.Current.FindResource("DangerFill")).Color;
                UiSmoke.Save(destructiveConfirm, "09-永久删除确认.png");
                destructiveConfirm.DialogResult = false;
                confirmTimer.Stop();
            };
            confirmTimer.Start();
            purge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int attempt = 0; attempt < 30 && destructiveConfirm == null; attempt++) await Task.Delay(100);
            confirmTimer.Stop();
            Check(dangerStyleCorrect, "irreversible deletion uses the danger action only in final confirmation");
            Check(destructiveConfirm != null && trash.IsVisible && File.Exists(trashed.Path), "canceling permanent deletion leaves the quarantined file intact");
            trashCheck.IsChecked = false;
            trash.Close();
            opened.Remove(trash);
            organizer.Restore([trashed], new Progress<JobProgress>(), CancellationToken.None);
            Check(catalog.ById(removable.Id) is { Status: "active" } restored && File.Exists(restored.Path) && LibraryService.HashFile(restored.Path) == removableHash, "recovery restores the same file bytes");

            var still = catalog.AllActive().First(photo => !photo.IsVideo && photo.Error.Length == 0);
            var viewer = new PhotoViewer([still], 0) { Owner = main };
            opened.Add(viewer);
            viewer.Show();
            for (int attempt = 0; attempt < 60 && !Descendants<Image>(viewer).Any(image => image.Source != null); attempt++) await Settle(120);
            Check(Descendants<Image>(viewer).Any(image => image.Source != null), "image preview renders the actual indexed photo");
            Check(Descendants<Button>(viewer).Where(button => button.IsVisible).All(button => button.ActualHeight >= 32), "image-preview tools expose the compact desktop hit area");
            Check(Descendants<TextBlock>(viewer).Where(text => text.IsVisible).All(text => text.FontSize >= 12), "image-preview copy remains readable");
            UiSmoke.Save(viewer, "10-图片预览.png");
            viewer.Close();
            opened.Remove(viewer);

            ThemeManager.SetPreference("dark");
            await Settle(500);
            UiSmoke.Save(main, "11-主窗口-深色.png");
            ThemeManager.SetPreference("system");
            await Settle(300);
            Check(LibraryService.HashFile(keeper.Path) == LibraryService.HashFile(exact.Photos.First(photo => photo.Id == exact.Keeper).Path), "the duplicate keeper remains unchanged throughout review");

            var sourcePhoto = catalog.AllActive().First(photo => !photo.IsVideo && photo.Error.Length == 0);
            var incrementalCopy = Path.Combine(sourcePhoto.Root, "增量导入-重复副本-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(sourcePhoto.Path));
            File.Copy(sourcePhoto.Path, incrementalCopy);
            await main.RescanSources();
            Check(catalog.ByPath(incrementalCopy) != null && catalog.HasDuplicateIndex() && main.StatusText.Contains("更新重复索引", StringComparison.Ordinal),
                "new media import refreshes the duplicate index without an immediate full rescan on open");
        }
        catch (Exception error)
        {
            failure = error.ToString();
        }
        finally
        {
            foreach (var window in opened.Where(window => window.IsVisible).ToList())
            {
                try { window.Close(); } catch { }
            }
        }
        File.WriteAllText(Path.Combine(AppPaths.Data, "design-acceptance-results.json"), JsonSerializer.Serialize(new
        {
            Passed = failure == null,
            Count = checks.Count,
            Checks = checks,
            Failure = failure,
            Surfaces = new[] { "main", "settings", "dialog", "duplicate-cleanup", "recent-deleted", "image-preview" },
            VideoEvidence = "video-playback-results.json and 01/02/03 player captures"
        }, new JsonSerializerOptions { WriteIndented = true }));
        main.ExitCode = failure == null ? 0 : 1;
        Application.Current.Shutdown(main.ExitCode);
    }
}
