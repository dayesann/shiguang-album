using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Shiguang;

public static class GalleryTests
{
    public static async Task Run(MainWindow main, Catalog catalog)
    {
        var checks = new List<string>();
        string? failure = null;
        void Check(bool pass, string name)
        {
            if (!pass) throw new IOException(name);
            checks.Add(name);
        }
        async Task Settle(int delay = 180) { await Task.Delay(delay); main.UpdateLayout(); }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Button Action(string name) => UiElements.Descendants<Button>(main).First(button => AutomationProperties.GetName(button) == name);
        Button Tile(Photo photo)
        {
            var row = main.Rows.First(row => row.Items.Any(item => item.Photo.Id == photo.Id));
            var gallery = (ListBox)main.FindName("Gallery");
            gallery.ScrollIntoView(row); gallery.UpdateLayout();
            return UiElements.Descendants<Button>(gallery).First(button => button.DataContext is GalleryItem item && item.Photo.Id == photo.Id);
        }
        void Key(UIElement source, Key key, RoutedEvent? routedEvent = null)
        {
            source.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(main), Environment.TickCount, key)
            { RoutedEvent = routedEvent ?? Keyboard.PreviewKeyDownEvent });
        }
        void FocusKey(Key key) => Key((UIElement)Keyboard.FocusedElement, key);
        var modeButton = (Button)main.FindName("SelectionModeButton");
        try
        {
            while (main.IsBusy) await Task.Delay(100);
            await Settle();
            var originals = catalog.AllActive().ToDictionary(photo => photo.Path, photo => LibraryService.HashFile(photo.Path));
            var photos = main.Rows.SelectMany(row => row.Items).Select(item => item.Photo).ToList();
            var first = photos[0];
            var tile = Tile(first); tile.Focus(); Click(tile);
            Check(!main.IsSelecting && !main.HasSelection && !main.ShowFooter && !first.Selected, "single click focuses without starting bulk selection");
            Key(tile, System.Windows.Input.Key.Delete);
            Check(!Application.Current.Windows.OfType<ConfirmationWindow>().Any(), "Delete in browse mode has no destructive action");
            FocusKey(System.Windows.Input.Key.Right);
            Check(main.FocusedPhoto?.Id == photos[1].Id && !main.HasSelection, "right arrow moves focus without selecting");
            FocusKey(System.Windows.Input.Key.Down);
            Check(main.Rows[1].Items.Any(item => item.Photo.Id == main.FocusedPhoto?.Id), "down arrow moves to the adjacent visual row");
            var beforePage = main.FocusedPhoto;
            FocusKey(System.Windows.Input.Key.PageDown);
            Check(main.FocusedPhoto != beforePage, "Page Down advances the gallery viewport");
            FocusKey(System.Windows.Input.Key.PageUp);
            Check(!main.HasSelection, "page navigation never checks items");

            Click(modeButton);
            Check(main.IsSelecting && main.ShowFooter && !Action("移入回收站").IsEnabled && !Action("加入相册").IsEnabled,
                "explicit multi-select exposes disabled actions until items are checked");
            tile = Tile(first); tile.Focus(); Click(tile);
            Check(first.Selected && main.HasSelection && main.SelectionText == "已选择 1 个项目", "multi-select click checks exactly one item");
            var second = photos[1]; tile = Tile(second); tile.Focus(); Key(tile, System.Windows.Input.Key.Space);
            Check(second.Selected && main.SelectionText == "已选择 2 个项目", "space checks the focused item");
            Check(Action("加入相册").IsEnabled && Action("移入回收站").IsEnabled, "checked items enable album and trash actions");

            bool sawConfirmation = false, safeFocus = false;
            var cancelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            cancelTimer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().FirstOrDefault();
                if (dialog == null) return;
                sawConfirmation = true; safeFocus = dialog.CancelButton.IsKeyboardFocused;
                UiSmoke.Save(dialog, "delete-confirmation.png");
                cancelTimer.Stop(); dialog.DialogResult = false;
            };
            cancelTimer.Start(); Key(tile, System.Windows.Input.Key.Delete); cancelTimer.Stop();
            Check(sawConfirmation && safeFocus && main.SelectionText == "已选择 2 个项目", "Delete opens the scoped confirmation with cancel focused and cancel preserves selection");
            Check(originals.All(item => File.Exists(item.Key) && LibraryService.HashFile(item.Key) == item.Value), "cancelled deletion preserves source bytes");

            var search = (TextBox)main.FindName("SearchBox"); search.Focus(); Key(search, System.Windows.Input.Key.Delete);
            Check(main.SelectionText == "已选择 2 个项目" && !Application.Current.Windows.OfType<ConfirmationWindow>().Any(), "search Delete never invokes the media delete command");
            Tile(second).Focus(); FocusKey(System.Windows.Input.Key.Escape);
            Check(!main.IsSelecting && !first.Selected && !second.Selected && !main.ShowFooter, "Escape exits multi-select and clears checks");

            main.OpenAlbum(catalog.Albums().First().Id); await Settle();
            var coverAlbum = catalog.Albums().First();
            var previousCover = catalog.AlbumCover(coverAlbum);
            var custom = main.Rows.SelectMany(row => row.Items).Select(item => item.Photo)
                .First(photo => !photo.IsVideo && photo.PreviewState == "ready" && photo.ThumbKey != previousCover);
            var coverButton = (Button)main.FindName("AlbumCoverButton");
            Tile(custom).Focus();
            Check(coverButton.Focus() && main.FocusedPhoto?.Id == custom.Id, "keyboard focus can move from a photo to the album cover action");
            Click(coverButton); await Settle();
            var coverMenu = coverButton.ContextMenu;
            var setCover = (MenuItem)coverMenu.Items[0]; var resetCover = (MenuItem)coverMenu.Items[1];
            Check(setCover.IsEnabled && !resetCover.IsEnabled, "a usable photo can be set and an automatic cover has no reset action");
            setCover.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); coverMenu.IsOpen = false; await Settle();
            Check(catalog.Albums().Single(a => a.Id == coverAlbum.Id).CoverPhotoId == custom.Id && main.Notification.IsVisible,
                "choosing the cover saves the focused photo and reports success");
            UiSmoke.Save(main, "album-cover-set.png");
            Click(UiElements.Descendants<Button>(main).Single(button => button.Tag is "albums")); await Settle(600);
            Check(main.Buckets.Single(bucket => bucket.Key == coverAlbum.Id).Cover == custom.ThumbKey, "album overview displays the chosen cover");
            UiSmoke.Save(main, "album-cover-overview.png");
            main.OpenAlbum(coverAlbum.Id); await Settle();
            Click(coverButton); await Settle();
            coverMenu = coverButton.ContextMenu; resetCover = (MenuItem)coverMenu.Items[1];
            Check(resetCover.IsEnabled, "an album with a custom cover offers reset");
            resetCover.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); coverMenu.IsOpen = false; await Settle();
            Check(catalog.AlbumCover(catalog.Albums().Single(a => a.Id == coverAlbum.Id)) == previousCover, "reset restores the original cover in the app");
            Click(modeButton); Click(coverButton); await Settle();
            coverMenu = coverButton.ContextMenu;
            Check(!((MenuItem)coverMenu.Items[0]).IsEnabled, "multi-select cannot replace the cover with a focused item");
            coverMenu.IsOpen = false; Click(modeButton);
            foreach (string theme in new[] { "light", "dark", "system" })
            {
                ThemeManager.SetPreference(theme);
                main.Width = 1536; main.Height = 1024; await Settle(350);
                UiSmoke.Save(main, "browse-" + theme + ".png");
                Click(modeButton);
                var photo = main.Rows[0].Items[0].Photo; Tile(photo).Focus(); Click(Tile(photo)); await Settle();
                UiSmoke.Save(main, "select-" + theme + ".png");
                main.Width = main.MinWidth; main.Height = main.MinHeight; await Settle(350);
                Check(main.HasSelection && Action("移入回收站").IsVisible && Action("加入相册").IsVisible,
                    theme + " minimum window preserves selection and visible actions");
                UiSmoke.Save(main, "select-" + theme + "-minimum.png");
                Click(modeButton);
            }

            var selected = main.Rows[0].Items[0].Photo;
            Click(modeButton); Click(Tile(selected));
            var album = catalog.Albums().Last();
            bool sawAlbum = false;
            var albumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            albumTimer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.Title == "加入相册");
                if (dialog == null) return;
                sawAlbum = true;
                var list = UiElements.Descendants<ListBox>(dialog).First();
                list.SelectedItem = list.Items.Cast<Album>().First(item => item.Id == album.Id);
                albumTimer.Stop(); dialog.DialogResult = true;
            };
            albumTimer.Start(); Click(Action("加入相册")); albumTimer.Stop(); await Settle();
            Check(sawAlbum && catalog.AlbumPhotos(album.Id).Any(photo => photo.Id == selected.Id) && !main.IsSelecting,
                "adding selected items creates album membership and returns to browsing");

            Click(modeButton); selected = main.Rows[0].Items[0].Photo; Click(Tile(selected));
            main.RefreshPage(true); await Settle();
            Check(main.IsSelecting && main.HasSelection, "background refresh retains multi-select state");
            main.SetMode("years"); await Settle();
            Check(!main.IsSelecting && !main.HasSelection && !modeButton.IsVisible, "switching away from media clears selection mode");
            main.SetMode("days"); await Settle();

            var stills = catalog.AllActive().Where(photo => !photo.IsVideo).Take(3).ToList();
            var viewer = new PhotoViewer(stills, 0) { Owner = main }; viewer.Show(); await Settle();
            Key(viewer, System.Windows.Input.Key.Down, Keyboard.KeyDownEvent); await Settle(400);
            Check(UiElements.Descendants<TextBlock>(viewer).Any(text => text.Text.StartsWith("2 / 3")), "preview Down goes to next photo");
            Key(viewer, System.Windows.Input.Key.PageUp, Keyboard.KeyDownEvent); await Settle(400);
            Check(UiElements.Descendants<TextBlock>(viewer).Any(text => text.Text.StartsWith("1 / 3")), "preview Page Up goes to previous photo");
            viewer.Close();
            Check(originals.All(item => File.Exists(item.Key) && LibraryService.HashFile(item.Key) == item.Value), "browsing selection and album actions preserve every original file");
            Check(!File.Exists(Path.Combine(AppPaths.Data, "binding-errors.log")) || new FileInfo(Path.Combine(AppPaths.Data, "binding-errors.log")).Length == 0,
                "gallery bindings report no warnings or errors");
        }
        catch (Exception error) { failure = error.ToString(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "gallery-results.json"), JsonSerializer.Serialize(new
        { Passed = failure == null, Count = checks.Count, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        main.ExitCode = failure == null ? 0 : 1;
        Application.Current.Shutdown(main.ExitCode);
    }
}
