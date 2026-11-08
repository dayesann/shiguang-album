using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shiguang;

public static class FlowTests
{
    public static async Task Run(MainWindow main, Catalog catalog)
    {
        var checks = new List<string>(); string? failure = null;
        void Check(bool pass, string name) { if (!pass) throw new IOException(name); checks.Add(name); }
        async Task Settle() { await Task.Delay(400); main.UpdateLayout(); }
        Button Nav(string tag) => UiElements.Descendants<Button>(main).First(x => x.Tag as string == tag);
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            await Settle();
            var yearMode = (Button)main.FindName("YearMode"); var monthMode = (Button)main.FindName("MonthMode"); var dayMode = (Button)main.FindName("DayMode");
            var modeSwitch = (FrameworkElement)main.FindName("ModeSwitch");
            double ModeCenterInHeader() => modeSwitch.TranslatePoint(new Point(modeSwitch.ActualWidth / 2, 0), (UIElement)modeSwitch.Parent).X;
            double HeaderCenter() => ((FrameworkElement)modeSwitch.Parent).ActualWidth / 2;
            Check(new[] { yearMode, monthMode, dayMode }.All(x => x.ActualHeight >= 28 && x.ActualHeight <= 34), "segmented controls use the compact desktop density");
            Check(Math.Abs(ModeCenterInHeader() - HeaderCenter()) < 1, "year-month-day control is centered independently of side actions");
            var detailsButton = (Button)main.FindName("DetailsButton");
            Check(detailsButton.Background is SolidColorBrush ghost && ghost.Color.A == 0 && detailsButton.BorderBrush is SolidColorBrush border && border.Color.A == 0, "ordinary toolbar actions have no permanent filled or outlined box");
            var duplicateButton = (Button)main.FindName("DuplicateButton");
            Check(duplicateButton.IsVisible && !duplicateButton.IsEnabled && System.Windows.Automation.AutomationProperties.GetName(duplicateButton) == "重复照片", "movie-library empty state keeps the duplicate tool discoverable and correctly disabled");
            Check(!((ComboBox)main.FindName("YearBox")).IsVisible && ((TextBox)main.FindName("SearchBox")).ActualHeight == 32, "duplicate filters stay hidden and search has a compact 32 DIP height");
            Check(catalog.Count() == 0 && main.ShowEmptyImport, "fresh library offers a direct import action"); UiSmoke.Save(main, "01-首次使用.png");
            Click(Nav("albums")); await Settle();
            Check(main.IsAlbumOverview && !main.ShowFilters && main.RailWidth.Value == 0, "album overview hides unrelated media filters and year rail");
            Check(main.EmptyHeading == "还没有相册" && main.ShowEmptyAlbumAction, "empty album overview offers a direct create action"); UiSmoke.Save(main, "02-空相册.png");
            var editorWindow = Dialogs.Shell(main, "输入排版验收", 500, 220, out var body);
            var editor = new TextBox { Text = "验收测试·家庭回忆", Height = 48, VerticalAlignment = VerticalAlignment.Top }; body.Children.Add(editor); editorWindow.Show(); await Settle();
            var host = (ScrollViewer)editor.Template.FindName("PART_ContentHost", editor);
            Check(host.ViewportHeight >= editor.GetRectFromCharacterIndex(0).Height, "Chinese input has enough visible line height without clipping"); UiSmoke.Save(editorWindow, "05-中文输入框.png"); editorWindow.Close();
            string root = AppPaths.Data + "-分页素材"; Directory.CreateDirectory(root);
            string first = Path.Combine(root, "原始_20180101.jpg"); SelfTest.CreateImage(first, 1, false); var originals = new Dictionary<string, string>();
            var items = new List<Photo>();
            for (int i = 0; i < 270; i++)
            {
                string file = Path.Combine(root, $"照片_{i:D4}_20180101.jpg"); File.Copy(first, file, true); var p = MediaIndexer.Skeleton(file, root);
                p.Taken = new DateTime(2018, 1, 1).AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss"); p.DateSource = "手动确认"; p.PreviewState = "ready"; p.Width = 640; p.Height = 480;
                File.Copy(first, p.ThumbPath, true); items.Add(p); originals[file] = LibraryService.HashFile(file);
            }
            catalog.UpsertMany(items); catalog.AddRoot(root);
            string album = catalog.CreateAlbum("家庭旅行与朋友聚会的回忆" + new string('长', 40));
            await main.RescanSources(); await Settle();
            Check(duplicateButton.IsEnabled && duplicateButton.ActualHeight is >= 36 and <= 40, "duplicate cleanup becomes available at the compact desktop action size");
            main.OpenAlbum(album); await Settle();
            Check(main.IsAlbumEmpty && main.ShowEmptyTimelineAction && !main.ShowFilters, "new empty album guides users back to select indexed media");
            UiSmoke.Save(main, "06-空相册下一步.png"); catalog.AddToAlbum(album, items.Take(3)); await main.RescanSources(); Click(Nav("timeline")); await Settle();
            Check(main.Rows.SelectMany(x => x.Items).Count() == 240, "gallery initially loads a bounded page rather than the whole library");
            var search = (TextBox)main.FindName("SearchBox"); search.Text = "不可能匹配的词"; await Settle(); main.OpenAlbum(album); await Settle();
            Check(search.Text.Length == 0 && main.Rows.Count > 0, "opening an album clears the old search filter");
            Check(main.InAlbum && main.Heading.StartsWith("家庭旅行", StringComparison.Ordinal), "long album title is active before alignment comparison");
            Check(Math.Abs(ModeCenterInHeader() - HeaderCenter()) < 1, "year-month-day control does not move when the page title changes");
            search.Text = "不可能匹配的词"; await Settle(); Click(Nav("image")); await Settle();
            Check(search.Text.Length == 0 && main.CurrentFilter().View == "media", "switching media category resets unrelated search and album state");
            Check(((SolidColorBrush)Nav("image").Background).Color == ((SolidColorBrush)Application.Current.FindResource("Selected")).Color, "current media navigation has a persistent selected state");
            var selected = main.Rows.SelectMany(r => r.Items).First().Photo; selected.Selected = true; await main.RescanSources(); await Settle();
            Check(main.Rows.SelectMany(r => r.Items).Any(x => x.Photo.Id == selected.Id && x.Photo.Selected), "background rescan preserves current selection");
            main.OpenAlbum(album); main.Width = 1150; main.Height = 750; await Settle(); UiSmoke.Save(main, "03-长相册名与最小窗口.png");
            var gallery = (ListBox)main.FindName("Gallery"); Check(gallery.ActualWidth > 400, "long album names preserve gallery and toolbar space at minimum width");
            Check(main.InAlbum && main.Heading.StartsWith("家庭旅行", StringComparison.Ordinal), "minimum-width alignment check uses the long album title");
            Check(Math.Abs(ModeCenterInHeader() - HeaderCenter()) < 1, "year-month-day control stays centered after window and title changes");
            var sidebar = (ScrollViewer)main.FindName("SideNavigation");
            var settingsEntry = UiElements.Descendants<Button>(main).First(button => System.Windows.Automation.AutomationProperties.GetName(button) == "设置");
            Check(sidebar.ViewportHeight > 250 && settingsEntry.IsVisible && settingsEntry.ActualHeight is >= 36 and <= 40, "compact sidebar keeps navigation usable above the fixed settings entry");
            main.Width = 1536; main.Height = 1024; Click(Nav("timeline")); await Settle();
            var viewer = new PhotoViewer(i => catalog.Browse(new(Kind: "image"), i, 1).Items.FirstOrDefault(), catalog.Count(), 239) { Owner = main }; viewer.Show();
            await Settle(); Click(UiElements.Descendants<Button>(viewer).First(x => System.Windows.Automation.AutomationProperties.GetName(x) == "下一张"));
            for (int i = 0; i < 25 && !UiElements.Descendants<TextBlock>(viewer).Any(x => x.Text.StartsWith("241 / 271")); i++) await Settle();
            Check(UiElements.Descendants<TextBlock>(viewer).Any(x => x.Text.StartsWith("241 / 271")), "large preview crosses the 240-item gallery page boundary");
            viewer.Width = 700; viewer.Height = 500; await Settle(); UiSmoke.Save(viewer, "04-大图跨页与小窗口.png");
            Check(UiElements.Descendants<Button>(viewer).All(x => x.ActualWidth <= viewer.ActualWidth), "preview toolbar remains bounded at minimum size"); viewer.Close();

            var duplicatePhoto = catalog.ById(items[0].Id)!;
            var duplicateCopy = catalog.ById(items[1].Id)!;
            var duplicateGroup = new DuplicateGroup("ui-exact", "exact", [duplicatePhoto, duplicateCopy], duplicatePhoto.Id);
            var duplicateWindow = new DuplicateWindow([duplicateGroup]) { Owner = main };
            duplicateWindow.Show(); await Settle();
            Check(duplicateWindow.CurrentKind == "exact" && duplicateWindow.ApplyButton.IsEnabled, "duplicate review exposes a usable primary cleanup action");
            Check(UiElements.Descendants<TextBlock>(duplicateWindow).Where(x => x.IsVisible).All(x => x.FontSize >= 12), "duplicate review avoids unreadable operational text");
            UiSmoke.Save(duplicateWindow, "12-重复照片清理.png");
            duplicateWindow.Close();
            int before = catalog.Albums().Count; string invalid = Path.Combine(AppPaths.Data, "无效.albums.json");
            File.WriteAllText(invalid, "{\"Version\":1,\"Albums\":[{\"Name\":\"第一项有效\",\"Photos\":[]},{\"Name\":\"坏项\",\"Photos\":null}]}");
            bool rejected = false; try { AlbumBackup.Import(catalog, invalid); } catch (IOException) { rejected = true; }
            Check(rejected && catalog.Albums().Count == before, "malformed backup is rejected before any partial album writes");
            string duplicate = Path.Combine(AppPaths.Data, "重复引用.albums.json"); var reference = new AlbumBackup.Reference(items[0].Id, items[0].Path, "");
            File.WriteAllText(duplicate, JsonSerializer.Serialize(new AlbumBackup.Backup(1, [new("引用去重", [reference, reference])]))); AlbumBackup.Import(catalog, duplicate);
            Check(catalog.Albums().Single(x => x.Name == "引用去重（导入）").Count == 1, "backup import deduplicates repeated references");
            Check(originals.All(x => File.Exists(x.Key) && LibraryService.HashFile(x.Key) == x.Value), "full flow leaves every source file path and byte unchanged");
        }
        catch (Exception e) { failure = e.ToString(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "flow-test-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Count = checks.Count, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        main.ExitCode = failure == null ? 0 : 1; main.Close();
    }
}
