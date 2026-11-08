using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shiguang;

public static class ComponentAcceptance
{
    public static async Task Run(MainWindow main, Catalog catalog)
    {
        var checks = new List<string>(); string? failure = null; var opened = new List<Window>();
        void Check(bool pass, string name) { if (!pass) throw new IOException(name); checks.Add(name); }
        async Task Settle() { await Task.Delay(350); main.UpdateLayout(); foreach (var w in opened.Where(w => w.IsVisible)) w.UpdateLayout(); }
        Button Named(DependencyObject root, string name) => UiSmoke.Descendants<Button>(root).First(b => AutomationProperties.GetName(b) == name);
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            while (main.IsBusy) await Task.Delay(50);
            var originals = catalog.AllActive().ToDictionary(p => p.Path, p => LibraryService.HashFile(p.Path));
            ThemeManager.SetPreference("light");
            var sample = new ComponentGalleryWindow { Owner = main }; opened.Add(sample); sample.Show(); await Settle();
            Check(UiSmoke.Descendants<Button>(sample).Where(b => b.IsVisible).All(b => b.ActualHeight >= 32 && b.FontSize == UiMetrics.Body), "all sample actions share the 32 DIP target and 14 DIP type");
            var primaryText = UiSmoke.Descendants<System.Windows.Controls.ContentPresenter>(sample.SamplePrimary).First();
            var before = primaryText.TranslatePoint(new Point(), sample); sample.SamplePrimary.Focus(); await Settle(); var after = primaryText.TranslatePoint(new Point(), sample);
            Check(before == after, "keyboard focus ring does not move button content");
            Check(!Named(sample, "等待选择").IsEnabled && !Named(sample, "等待选择").IsKeyboardFocused, "disabled sample remains unavailable and unfocused");
            UiSmoke.Save(sample, "01-控件样板-浅色.png");
            sample.SampleToolbar.Width = 80; await Settle();
            Check(sample.SampleToolbar.HasOverflowItems, "native toolbar moves excess actions to overflow at narrow width");
            sample.SampleToolbar.IsOverflowOpen = true; await Settle();
            var overflowed = sample.SampleToolbar.Items.OfType<Button>().Where(ToolBar.GetIsOverflowItem).ToList();
            Check(overflowed.Count > 0 && overflowed.All(b => b.ActualWidth >= 190), "overflow actions expand into readable named menu rows");
            sample.SampleToolbar.IsOverflowOpen = false; sample.SampleToolbar.Width = 420;
            sample.Notice.Show("已移入回收站 3 个项目", "撤销", () => sample.Notice.Show("已撤销")); await Settle();
            Click(Named(sample.Notice, "撤销")); await Settle(); Check(sample.Notice.Message == "已撤销", "notice action runs once and displays its result");
            sample.Notice.Show("目录当前不可访问", error: true); await Task.Delay(7600);
            Check(sample.Notice.IsVisible && sample.Notice.Message == "目录当前不可访问", "error notice remains until explicitly dismissed");
            Click(Named(sample.Notice, "关闭提示")); Check(!sample.Notice.IsVisible, "notice can be explicitly closed");
            ThemeManager.SetPreference("dark"); await Settle(); UiSmoke.Save(sample, "02-控件样板-深色.png");
            ThemeManager.SetPreference("system"); await Settle(); Check(ThemeManager.Preference == "system", "component gallery follows the shared system-theme preference");
            sample.Close(); opened.Remove(sample);
            ThemeManager.SetPreference("light");
            var nav = UiSmoke.Descendants<Button>(main).First(b => b.Tag as string == "image"); Click(nav); await Settle();
            main.Width = 1150; main.Height = 750; await Settle();
            var header = (Grid)main.FindName("HeaderGrid"); var mode = (Border)main.FindName("ModeSwitch"); var search = (TextBox)main.FindName("SearchBox");
            Check(Math.Abs(mode.TranslatePoint(new Point(mode.ActualWidth / 2, 0), header).X - header.ActualWidth / 2) < 1, "year-month-day group remains absolutely centered at minimum width");
            Check(mode.TranslatePoint(new Point(mode.ActualWidth, 0), header).X + 8 < search.TranslatePoint(new Point(), header).X, "centered mode and search do not overlap at minimum width");
            Check(new[] { "YearBox", "MonthBox", "DayBox", "KindBox" }.All(n => !((FrameworkElement)main.FindName(n)).IsVisible), "duplicate filter controls stay out of the visible header");
            var day = (Button)main.FindName("DayMode"); day.Focus();
            var keyboard = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(day)!, 0, Key.Left) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            day.RaiseEvent(keyboard); await Settle();
            Check(((Button)main.FindName("MonthMode")).IsKeyboardFocused && !main.ShowGallery, "left arrow changes focused day segment to month view");
            main.SetMode("days"); await Settle();
            search.Text = "测试搜索";
            search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); await Settle();
            Check(search.Text.Length == 0, "Escape clears search without leaving a hidden filter");
            UiSmoke.Save(main, "03-主窗口-最小尺寸.png");
            main.MinWidth = 800; main.MinHeight = 500; main.Width = 960; main.Height = 540; await Settle();
            Check(mode.TranslatePoint(new Point(mode.ActualWidth, 0), header).X + 8 < search.TranslatePoint(new Point(), header).X, "header remains separated in a 960 by 540 logical workspace");
            Check(Named(main, "设置").IsVisible && ((ScrollViewer)main.FindName("SideNavigation")).ViewportHeight > 200, "small logical workspace retains scrollable navigation and settings");
            UiSmoke.Save(main, "03b-960x540逻辑工作区.png");
            main.MinWidth = 1150; main.MinHeight = 750;
            main.Width = 1536; main.Height = 1024; await Settle(); UiSmoke.Save(main, "04-主窗口-浅色.png");
            foreach (string theme in new[] { "light", "dark" })
            {
                ThemeManager.SetPreference(theme);
                var settings = new SettingsWindow(main, catalog) { Owner = main }; opened.Add(settings); settings.Show(); settings.SelectPage("library"); await Settle();
                Check(UiSmoke.Descendants<SettingRow>(settings).Count() == 4, theme + " library settings uses consistent compact rows");
                UiSmoke.Save(settings, "05-设置-媒体库-" + theme + ".png");
                settings.SelectPage("backup"); await Settle(); UiSmoke.Save(settings, "06-设置-备份迁移-" + theme + ".png");
                settings.SelectPage("appearance"); await Settle(); UiSmoke.Save(settings, "07-设置-外观-" + theme + ".png");
                settings.Close(); opened.Remove(settings);
            }
            var confirm = new ConfirmationWindow("永久删除 3 个项目？", "这些文件和相册引用将永久删除，无法恢复。", "永久删除", true, catalog.AllActive().Take(3).ToList()) { Owner = main }; opened.Add(confirm); confirm.Show(); await Settle();
            Check(confirm.CancelButton.IsKeyboardFocused && !confirm.ConfirmButton.IsDefault, "permanent deletion defaults to safe cancellation");
            Check(confirm.ActualHeight < 400 && confirm.ConfirmButton.ActualHeight == 32, "confirmation sizes to content with compact actions");
            UiSmoke.Save(confirm, "08-永久删除确认.png"); confirm.Close(); opened.Remove(confirm);
            ThemeManager.SetPreference("light"); main.OpenAlbum(catalog.Albums().First().Id); await Settle();
            var selected = main.Rows.SelectMany(r => r.Items).First().Photo; selected.Selected = true; await Settle();
            int membership = catalog.AlbumPhotos(catalog.Albums().First().Id).Count;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) => { var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().FirstOrDefault(w => w.IsVisible); if (dialog == null) return; timer.Stop(); dialog.DialogResult = true; }; timer.Start();
            Click(Named(main, "移入回收站"));
            for (int i = 0; i < 100 && (main.IsBusy || catalog.ById(selected.Id)?.Status != "trash"); i++) await Task.Delay(100);
            timer.Stop(); await Settle();
            Check(catalog.ById(selected.Id)?.Status == "trash" && main.Notification.IsVisible, "confirmed deletion offers recovery through the inline notice");
            Click(Named(main.Notification, "撤销"));
            for (int i = 0; i < 100 && (main.IsBusy || catalog.ById(selected.Id)?.Status != "active"); i++) await Task.Delay(100);
            await Settle();
            Check(catalog.ById(selected.Id)?.Status == "active" && catalog.AlbumPhotos(catalog.Albums().First().Id).Count == membership, "undo restores the original file and album memberships");
            Check(originals.All(p => File.Exists(p.Key) && LibraryService.HashFile(p.Key) == p.Value), "UI review and recoverable-delete cycle preserve every original file");
        }
        catch (Exception e) { failure = e.ToString(); }
        foreach (var w in opened.Where(w => w.IsVisible).ToList()) w.Close();
        File.WriteAllText(Path.Combine(AppPaths.Data, "component-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        main.ExitCode = failure == null ? 0 : 1; Application.Current.Shutdown(main.ExitCode);
    }
}
