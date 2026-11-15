using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shiguang;

public static class SettingsSmoke
{
    public static async Task Run(MainWindow main, Catalog catalog)
    {
        var checks = new List<string>(); string? failure = null; var system = ThemeManager.SystemLight; var highContrast = ThemeManager.HighContrastMode;
        void Check(bool value, string name) { if (!value) throw new IOException(name); checks.Add(name); }
        async Task Settle() { await Task.Delay(350); main.UpdateLayout(); }
        string ColorOf(Brush brush) => ((SolidColorBrush)brush).Color.ToString();
        double WhiteContrast(Brush brush)
        {
            var color = ((SolidColorBrush)brush).Color;
            double Linear(byte part) { double value = part / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return 1.05 / (.2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B) + .05);
        }
        SettingsWindow? settings = null;
        try
        {
            while (main.IsBusy) await Task.Delay(50);
            var originals = catalog.AllActive().ToDictionary(x => x.Path, x => LibraryService.HashFile(x.Path));
            Check(ThemeManager.Preference == ThemeManager.ReadPreference() && (ThemeManager.Preference != "light" || ThemeManager.IsLight && ColorOf(main.Background) == "#FFF5F7FA"), "startup restores the saved preference and applies it to the library");
            settings = new(main, catalog) { Owner = main }; settings.Show(); await Settle();
            settings.ChooseTheme("light"); await Settle();
            Check(WhiteContrast((Brush)Application.Current.FindResource("Primary")) >= 4.5, "light primary buttons retain readable white-text contrast");
            Check(ThemeManager.IsLight && ColorOf(main.Background) == "#FFF5F7FA", "light theme immediately updates the open library window");
            Check(ColorOf(settings.Background) == "#FFF5F7FA", "settings window follows the same light palette");
            Check(ThemeManager.ReadPreference() == "light", "light preference persists to disk");
            UiSmoke.Save(settings, "01-设置-浅色.png"); UiSmoke.Save(main, "02-媒体库-浅色.png");
            var still = catalog.AllActive().First(x => !x.IsVideo && x.Error.Length == 0); var viewer = new PhotoViewer([still], 0) { Owner = main }; viewer.Show(); await Settle();
            Check(ColorOf(viewer.Background) == "#FFF5F7FA", "photo preview inherits the selected theme"); viewer.Close();
            settings.ChooseTheme("dark"); await Settle();
            Check(WhiteContrast((Brush)Application.Current.FindResource("Primary")) >= 4.5, "dark primary buttons retain readable white-text contrast");
            Check(!ThemeManager.IsLight && ColorOf(main.Background) == "#FF20242C", "dark theme immediately updates the open library window");
            Check(ThemeManager.ReadPreference() == "dark", "dark preference persists to disk"); UiSmoke.Save(settings, "03-设置-深色.png");
            var selected = ((ComboBox)main.FindName("YearBox")).Foreground; Check(ColorOf(selected) == "#FFF0F3F9", "filter controls update foreground with theme");
            ThemeManager.SystemLight = () => true; settings.ChooseTheme("system"); await Settle();
            Check(ThemeManager.IsLight && ThemeManager.ReadPreference() == "system", "follow-system mode persists while resolving light system theme"); UiSmoke.Save(settings, "04-设置-跟随系统.png");
            ThemeManager.SystemLight = () => false; await Task.Delay(2300); await Settle();
            Check(!ThemeManager.IsLight && ColorOf(main.Background) == "#FF20242C", "follow-system mode reacts to a changed system theme reading");
            settings.ChooseTheme("light"); ThemeManager.RefreshSystem(); Check(ThemeManager.IsLight, "explicit theme ignores subsequent system color changes");
            ThemeManager.HighContrastMode = () => true; ThemeManager.Reapply(); await Settle();
            Check(ThemeManager.IsHighContrast && ColorOf(main.Background) == System.Windows.SystemColors.WindowColor.ToString(), "Windows high-contrast mode overrides decorative palettes with system colors");
            ThemeManager.HighContrastMode = () => false; ThemeManager.Reapply(); await Settle();
            var shortcuts = UiSmoke.Descendants<Button>(settings).Single(b => b.Tag is "shortcuts");
            Check(shortcuts.Focus() && shortcuts.IsKeyboardFocused, "shortcut navigation can take keyboard focus");
            shortcuts.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            Check(settings.CurrentPage == "shortcuts", "shortcut reference has a dedicated settings page");
            Check(UiSmoke.Descendants<TextBlock>(settings).Count(t => t.Text == "Ctrl + A") == 2 &&
                UiSmoke.Descendants<TextBlock>(settings).Any(t => t.Text == "全选已加载的项目") &&
                UiSmoke.Descendants<TextBlock>(settings).Any(t => t.Text == "勾选回收站中的所有项目"),
                "the two Ctrl+A scopes are documented separately");
            UiSmoke.Save(settings, "shortcuts-light.png");
            settings.Width = settings.MinWidth; settings.Height = settings.MinHeight;
            foreach (string theme in new[] { "dark", "system" })
            {
                settings.ChooseTheme(theme); await Settle();
                var scroll = UiSmoke.Descendants<ScrollViewer>(settings).Single(s => s.Content is StackPanel);
                Check(scroll.ScrollableHeight > 0 && scroll.ViewportWidth > 0, theme + " shortcut contents fit the minimum window with vertical scrolling");
                UiSmoke.Save(settings, "shortcuts-" + theme + "-minimum.png");
                scroll.ScrollToEnd(); await Settle();
                Check(scroll.VerticalOffset > 0, theme + " shortcut content can be scrolled to the last group");
                if (theme == "dark") UiSmoke.Save(settings, "shortcuts-dark-last-group.png");
                settings.SelectPage("about"); settings.SelectPage("shortcuts"); await Settle();
                Check(scroll.VerticalOffset == 0, theme + " returning to shortcuts opens the first group");
            }
            settings.Width = 1060; settings.Height = 790;
            settings.ChooseTheme("light"); await Settle();
            foreach (var (key, file) in new[] { ("sources", "05-设置-来源目录.png"), ("library", "06-设置-媒体库与备份.png"), ("about", "07-设置-关于.png") })
            {
                settings.SelectPage(key); await Settle(); Check(settings.CurrentPage == key, key + " settings navigation works"); UiSmoke.Save(settings, file);
            }
            var pending = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>();
            using var http = new System.Net.Http.HttpClient(new UpdateTests.Stub((request, token) => pending.Task.WaitAsync(token)));
            var updateHome = Directory.CreateDirectory(Path.Combine(AppPaths.Data, "update-sandbox")).FullName;
            main.ConfigureUpdates(updateHome, Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), true, http);
            settings.SelectPage("about"); await Settle();
            var autoDownload = UiSmoke.Descendants<CheckBox>(settings).Single(x => x.Content is string text && text == "自动下载更新");
            Check(autoDownload.IsEnabled && autoDownload.IsChecked == false, "background downloads require opting in");
            await main.SetAutoDownloadAsync(true);
            Check(autoDownload.IsChecked == true && new PortableUpdates(http, updateHome, new Version(0, 0, 0)).ReadAutoDownload(), "automatic download choice persists per installation");
            await main.SetAutoDownloadAsync(false);
            Button Action(string name) => UiSmoke.Descendants<Button>(settings).Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == name);
            var update = Action("检查更新");
            Check(update.IsEnabled && update.Focusable, "about exposes a keyboard-focusable Check for updates action");
            Check(update.Focus() && update.IsKeyboardFocused, "update button takes keyboard focus and shows the shared focus outline");
            await Settle();
            UiSmoke.Save(settings, "09-软件更新-浅色.png");
            update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            Check(!update.IsEnabled && main.UpdateStatus.Contains("正在检查"), "active update check disables duplicate actions and shows its status");
            UiSmoke.Save(settings, "10-软件更新-检查中.png");
            pending.SetException(new System.Net.Http.HttpRequestException("offline")); await Settle();
            Check(update.IsEnabled && main.UpdateStatus.Contains("无法检查更新"), "an offline check displays a retryable error");
            settings.ChooseTheme("dark"); settings.Width = settings.MinWidth; settings.Height = settings.MinHeight; await Settle();
            UiSmoke.Save(settings, "11-软件更新-深色最小窗口-离线.png");
            pending = new();
            update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pending.SetResult(UpdateTests.Metadata(new Version(99, 0, 0), 1024, new string('a', 64))); await Settle();
            Check(Action("下载更新").IsEnabled && main.UpdateStatus.Contains("99.0.0"), "discovery exposes a download action and the new version");
            ThemeManager.SystemLight = () => true; settings.ChooseTheme("system"); await Settle();
            UiSmoke.Save(settings, "12-软件更新-跟随系统-发现新版.png");
            settings.ChooseTheme("light"); await Settle();
            settings.SelectPage("library"); await Settle();
            Check(UiSmoke.Descendants<Button>(settings).Any(button => System.Windows.Automation.AutomationProperties.GetName(button) == "检查重复"), "media-library settings exposes duplicate cleanup");
            Check(UiSmoke.Descendants<Button>(settings).Any(button => System.Windows.Automation.AutomationProperties.GetName(button).StartsWith("暂无内容")), "media-library settings exposes the recoverable-deletion entry state");
            settings.Width = 940; settings.Height = 650; settings.SelectPage("appearance"); await Settle(); UiSmoke.Save(settings, "08-设置-紧凑窗口.png");
            Check(UiSmoke.Descendants<Button>(settings).Where(x => x.ToolTip is string s && s.StartsWith("使用")).All(x => x.ActualWidth > 150), "theme cards remain usable at minimum window width");
            settings.Close(); settings = new(main, catalog) { Owner = main }; settings.Show(); await Settle(); Check(ThemeManager.Preference == "light" && ColorOf(settings.Background) == "#FFF5F7FA", "reopened settings retains the selected appearance");
            ThemeManager.SystemLight = system; settings.ChooseTheme("system"); settings.Close(); settings = null;
            Check(ThemeManager.IsLight == system(), "real Windows setting is restored after simulated system readings");
            var backup = main.BackupLibrary(); Check(File.Exists(backup) && File.Exists(backup + ".albums.json"), "settings backup action creates both database and album reference snapshots");
            Check(originals.All(x => File.Exists(x.Key) && LibraryService.HashFile(x.Key) == x.Value), "appearance and settings operations preserve all original paths and bytes");
            await Settle(); var log = Path.Combine(AppPaths.Data, "binding-errors.log"); Check(!File.Exists(log) || new FileInfo(log).Length == 0, "no WPF binding warnings during settings and theme changes");
        }
        catch (Exception e) { failure = e.ToString(); }
        finally { ThemeManager.SystemLight = system; ThemeManager.HighContrastMode = highContrast; ThemeManager.Reapply(); settings?.Close(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "settings-test-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Count = checks.Count, Checks = checks, Failure = failure, SystemChangeTest = "Injected system-reading function; Windows preferences were not changed" }, new JsonSerializerOptions { WriteIndented = true }));
        File.AppendAllText(Path.Combine(AppPaths.Data, "lifecycle.log"), $"{DateTime.Now:O} pid={Environment.ProcessId} Settings tests completed; requesting shutdown\n");
        main.ExitCode = failure == null ? 0 : 1; Application.Current.Shutdown(main.ExitCode);
    }
}
