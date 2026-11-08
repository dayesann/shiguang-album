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
            foreach (var (key, file) in new[] { ("sources", "05-设置-来源目录.png"), ("library", "06-设置-媒体库与备份.png"), ("about", "07-设置-关于.png") })
            {
                settings.SelectPage(key); await Settle(); Check(settings.CurrentPage == key, key + " settings navigation works"); UiSmoke.Save(settings, file);
            }
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
