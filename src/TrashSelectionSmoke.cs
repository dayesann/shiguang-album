using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Shiguang;

internal static class TrashSelectionSmoke
{
    internal static async Task Run(MainWindow main, Catalog catalog)
    {
        var checks = new List<string>();
        string? failure = null;
        TrashWindow? window = null;
        void Check(bool pass, string name)
        {
            if (!pass) throw new IOException(name);
            checks.Add(name);
        }
        static Button ButtonNamed(Window w, string name) =>
            UiSmoke.Descendants<Button>(w).Single(b => AutomationProperties.GetName(b) == name);
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static async Task Settle() => await Task.Delay(350);
        try
        {
            while (main.IsBusy) await Task.Delay(50);
            var originals = catalog.AllActive().ToDictionary(p => p.Path, p => LibraryService.HashFile(p.Path));
            var items = catalog.AllActive().Where(p => !p.IsVideo).Take(12).ToList();
            foreach (var photo in items)
            {
                // Display-only metadata for the isolated window; do not move, delete or update any file.
                photo.OriginalPath = Path.Combine(@"C:\演示照片", photo.Name);
                photo.TrashDate = "2026-11-15 10:30";
            }
            foreach (string theme in new[] { "light", "dark", "system" })
            {
                ThemeManager.SetPreference(theme);
                window = new TrashWindow(items) { Owner = main };
                if (theme != "light") { window.Width = window.MinWidth; window.Height = window.MinHeight; }
                window.Show(); await Settle();
                var dialog = window;
                var toggle = ButtonNamed(dialog, "全选");
                var rows = UiSmoke.Descendants<CheckBox>(dialog).ToList();
                var purge = ButtonNamed(dialog, "永久删除");
                var restore = ButtonNamed(dialog, "恢复所选");
                var scroll = UiSmoke.Descendants<ScrollViewer>(dialog).First();
                Check(scroll.ScrollableHeight > 0 && !purge.IsEnabled && !restore.IsEnabled,
                    theme + ": list has items below the viewport; actions initially disabled");
                if (theme == "light") UiSmoke.Save(dialog, "trash-none.png");
                Click(toggle); await Settle();
                Check(dialog.SelectedPhotos.Count == items.Count && rows.All(row => row.IsChecked == true) &&
                    restore.IsEnabled && purge.IsEnabled && AutomationProperties.GetName(toggle) == "取消全选" &&
                    dialog.RequestedAction == TrashRequestedAction.None,
                    theme + ": select all includes offscreen items and performs no action");
                UiSmoke.Save(dialog, "trash-selected-" + theme + ".png");
                rows[0].IsChecked = false;
                Check(dialog.SelectedPhotos.Count == items.Count - 1 && AutomationProperties.GetName(toggle) == "全选",
                    theme + ": individual checkbox updates the master action");
                var shortcut = dialog.InputBindings.OfType<KeyBinding>().Single(b => b.Key == Key.A && b.Modifiers == ModifierKeys.Control);
                ((RoutedCommand)shortcut.Command).Execute(null, dialog);
                Check(dialog.SelectedPhotos.Count == items.Count, theme + ": Ctrl+A command selects all");
                if (theme == "light")
                {
                    bool reviewedScope = false;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                    timer.Tick += (_, _) =>
                    {
                        var confirm = Application.Current.Windows.OfType<ConfirmationWindow>()
                            .SingleOrDefault(w => w.IsVisible && ReferenceEquals(w.Owner, dialog));
                        if (confirm == null) return;
                        timer.Stop();
                        try
                        {
                            reviewedScope = confirm.CancelButton.IsKeyboardFocused && !confirm.ConfirmButton.IsDefault &&
                                UiSmoke.Descendants<TextBlock>(confirm).Any(t => t.Text == $"永久删除 {items.Count:N0} 个项目？");
                            UiSmoke.Save(confirm, "trash-confirm.png");
                        }
                        finally { confirm.DialogResult = false; }
                    };
                    timer.Start(); Click(purge); timer.Stop();
                    Check(reviewedScope && dialog.RequestedAction == TrashRequestedAction.None && dialog.SelectedPhotos.Count == items.Count,
                        "confirmation shows the full scope, focuses cancel, and cancel keeps selections without deletion");
                }
                Click(toggle);
                Check(dialog.SelectedPhotos.Count == 0 && rows.All(row => row.IsChecked == false) && !purge.IsEnabled && !restore.IsEnabled,
                    theme + ": clear selection resets rows and actions");
                dialog.Close(); window = null;
            }
            window = new TrashWindow([]) { Owner = main };
            window.Show(); await Settle();
            Check(!ButtonNamed(window, "全选").IsEnabled && !ApplicationCommands.SelectAll.CanExecute(null, window),
                "empty recycle bin disables both the button and keyboard command");
            UiSmoke.Save(window, "trash-empty.png");
            Check(originals.All(entry => File.Exists(entry.Key) && LibraryService.HashFile(entry.Key) == entry.Value),
                "selecting, unselecting and cancelling leave all original media unchanged");
        }
        catch (Exception error) { failure = error.ToString(); }
        finally { window?.Close(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "trash-selection-results.json"),
            JsonSerializer.Serialize(new { Passed = failure == null, Count = checks.Count, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        main.ExitCode = failure == null ? 0 : 1;
        Application.Current.Shutdown(main.ExitCode);
    }
}
