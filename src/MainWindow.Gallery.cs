using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Shiguang;

public partial class MainWindow
{
    int selectedCount;
    bool updatingSelection;

    void Photo_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Photo.Selected) || sender is not Photo photo || updatingSelection) return;
        selectedCount += photo.Selected ? 1 : -1;
        NotifySelection();
    }

    void NotifySelection()
    {
        Notify(nameof(HasSelection));
        Notify(nameof(SelectionText));
        Notify(nameof(CanEditSelection));
        Notify(nameof(CanTrashSelection));
    }

    void SetAllSelected(bool value)
    {
        updatingSelection = true;
        foreach (var photo in loaded)
            if (photo.Selected != value) photo.Selected = value;
        updatingSelection = false;
        selectedCount = value ? loaded.Count : 0;
        NotifySelection();
    }

    List<Photo> Selection() => IsSelecting ? loaded.Where(photo => photo.Selected).ToList() : [];

    void SelectionMode_Click(object sender, RoutedEventArgs e)
    {
        if (!CanShowDetails) return;
        SetSelectionMode(!IsSelecting);
        if (IsSelecting && focused != null) FocusGalleryPhoto(focused);
    }

    void SetSelectionMode(bool value)
    {
        IsSelecting = value;
        if (!value) SetAllSelected(false);
        Notify(nameof(IsSelecting));
        Notify(nameof(SelectionModeText));
        Notify(nameof(SelectionModeHint));
        Notify(nameof(ShowFooter));
        NotifySelection();
    }

    void SetFocusedPhoto(Photo photo)
    {
        if (ReferenceEquals(focused, photo)) return;
        focused = photo;
        Notify(nameof(FocusedPhoto));
        Notify(nameof(MembershipText));
    }

    void Tile_Focused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (((Button)sender).DataContext is GalleryItem item) SetFocusedPhoto(item.Photo);
    }

    void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not GalleryItem item) return;
        SetFocusedPhoto(item.Photo);
        if (IsSelecting) item.Photo.Selected = !item.Photo.Selected;
    }

    void Tile_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || ((Button)sender).DataContext is not GalleryItem item) return;
        e.Handled = true;
        if (!IsSelecting) Open(item.Photo);
    }

    void Gallery_Focused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.NewFocus, Gallery) && focused != null) FocusGalleryPhoto(focused);
    }

    bool TryFindGalleryPhoto(Photo photo, out int row, out int column)
    {
        for (row = 0; row < Rows.Count; row++)
        {
            var items = Rows[row].Items;
            for (column = 0; column < items.Count; column++)
                if (ReferenceEquals(items[column].Photo, photo)) return true;
        }
        column = -1;
        return false;
    }

    void FocusGalleryPhoto(Photo photo)
    {
        if (!TryFindGalleryPhoto(photo, out int row, out _)) return;
        var targetRow = Rows[row];
        Gallery.ScrollIntoView(targetRow);
        Gallery.UpdateLayout();
        if (Gallery.ItemContainerGenerator.ContainerFromItem(targetRow) is not ListBoxItem container) return;
        var tile = UiElements.Descendants<Button>(container).FirstOrDefault(button =>
            button.DataContext is GalleryItem item && ReferenceEquals(item.Photo, photo));
        tile?.Focus();
    }

    void MoveGalleryFocus(Key key)
    {
        if (focused == null || !TryFindGalleryPhoto(focused, out int row, out int column)) return;
        if (key is Key.Left or Key.Right)
        {
            int next = loaded.IndexOf(focused) + (key == Key.Left ? -1 : 1);
            if (next == loaded.Count && next < total) LoadMore();
            if (next >= 0 && next < loaded.Count) FocusGalleryPhoto(loaded[next]);
            return;
        }

        var scroll = UiElements.Descendants<ScrollViewer>(Gallery).FirstOrDefault();
        int page = Math.Max(1, (int)Math.Floor(scroll?.ViewportHeight ?? 1));
        int offset = key switch { Key.Up => -1, Key.Down => 1, Key.PageUp => -page, _ => page };
        if (row + offset >= Rows.Count && loaded.Count < total)
        {
            LoadMore();
            if (!TryFindGalleryPhoto(focused, out row, out column)) return;
        }
        int nextRow = Math.Clamp(row + offset, 0, Rows.Count - 1);
        if (nextRow == row) return;

        // Rows use different tile widths. Follow the same horizontal position between rows.
        var items = Rows[row].Items;
        double center = items.Take(column).Sum(item => item.Width + GalleryLayout.Gap) + items[column].Width / 2;
        double left = 0;
        var targetItems = Rows[nextRow].Items;
        foreach (var item in targetItems)
        {
            if (center <= left + item.Width + GalleryLayout.Gap / 2)
            {
                FocusGalleryPhoto(item.Photo);
                return;
            }
            left += item.Width + GalleryLayout.Gap;
        }
        FocusGalleryPhoto(targetItems[^1].Photo);
    }

    async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.F && modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.OriginalSource is TextBoxBase or ComboBox or MenuItem || !CanShowDetails) return;

        if (e.Key == Key.Escape && modifiers == ModifierKeys.None && IsSelecting)
        {
            e.Handled = true;
            SetSelectionMode(false);
            if (focused != null) FocusGalleryPhoto(focused);
            return;
        }
        if (e.Key == Key.Delete && modifiers == ModifierKeys.None && IsSelecting
            && (Gallery.IsKeyboardFocusWithin || SelectionActions.IsKeyboardFocusWithin))
        {
            e.Handled = true;
            if (!e.IsRepeat) await DeleteSelection();
            return;
        }
        if (e.OriginalSource is not Button { DataContext: GalleryItem } && !ReferenceEquals(e.OriginalSource, Gallery)) return;

        if (e.Key == Key.A && modifiers == ModifierKeys.Control && IsSelecting)
        {
            e.Handled = true;
            if (!e.IsRepeat) SetAllSelected(true);
            return;
        }
        if (modifiers != ModifierKeys.None) return;

        switch (e.Key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown:
                e.Handled = true;
                MoveGalleryFocus(e.Key);
                break;
            case Key.Space or Key.Enter:
                e.Handled = true;
                if (e.IsRepeat || focused == null) break;
                if (IsSelecting) focused.Selected = !focused.Selected;
                else Open(focused);
                break;
        }
    }
}
