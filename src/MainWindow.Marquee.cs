using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Shiguang;

public partial class MainWindow
{
    ScrollContentPresenter? marqueeViewport;
    Point marqueeOrigin;
    bool marqueeAdditive;
    HashSet<Photo>? marqueeBefore;
    HashSet<Photo>? marqueeHits;
    bool MarqueeActive => marqueeBefore != null;

    static Rect Bounds(ScrollContentPresenter viewport) => new(viewport.RenderSize);

    void Gallery_MarqueeDown(object sender, MouseButtonEventArgs e)
    {
        EndMarquee(restore: true);
        if (!IsSelecting || !CanShowDetails || !CanMutate || e.StylusDevice != null ||
            UiElements.Descendants<ScrollContentPresenter>(Gallery).FirstOrDefault() is not { } viewport) return;
        var origin = e.GetPosition(viewport);
        if (!Bounds(viewport).Contains(origin)) return; // Scrollbar and padding keep their own gestures.
        ArmMarquee(viewport, origin, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
    }

    internal void ArmMarquee(ScrollContentPresenter viewport, Point origin, bool additive)
    {
        marqueeViewport = viewport;
        marqueeOrigin = origin;
        marqueeAdditive = additive;
    }

    void Gallery_MarqueeMove(object sender, MouseEventArgs e)
    {
        if (marqueeViewport is not { } viewport) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndMarquee(restore: true); return; }
        var current = e.GetPosition(viewport);
        if (!MarqueeActive)
        {
            if (!ExceedsDragThreshold(current)) return; // A click still belongs to the tile.
            if (!Gallery.CaptureMouse()) { EndMarquee(restore: true); return; }
            StartMarquee();
        }
        UpdateMarquee(current);
        e.Handled = true;
    }

    internal bool ExceedsDragThreshold(Point current) =>
        Math.Abs(current.X - marqueeOrigin.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(current.Y - marqueeOrigin.Y) >= SystemParameters.MinimumVerticalDragDistance;

    internal void StartMarquee() => marqueeBefore = loaded.Where(photo => photo.Selected).ToHashSet();

    internal void UpdateMarquee(Point current)
    {
        if (marqueeViewport is not { } viewport || marqueeBefore is not { } before) return;
        var visible = Bounds(viewport);
        var edge = new Point(Math.Clamp(current.X, 0, visible.Width), Math.Clamp(current.Y, 0, visible.Height));
        var rect = new Rect(marqueeOrigin, edge);
        var hits = new HashSet<Photo>();
        foreach (var tile in UiElements.Descendants<Button>(viewport))
        {
            if (!tile.IsVisible || tile.DataContext is not GalleryItem item) continue;
            var overlap = Rect.Intersect(rect, tile.TransformToAncestor(viewport).TransformBounds(new Rect(tile.RenderSize)));
            if (!overlap.IsEmpty && overlap.Width > 0 && overlap.Height > 0) hits.Add(item.Photo);
        }
        var topLeft = viewport.TranslatePoint(rect.TopLeft, MarqueeLayer);
        Canvas.SetLeft(MarqueeFrame, topLeft.X);
        Canvas.SetTop(MarqueeFrame, topLeft.Y);
        MarqueeFrame.Width = rect.Width;
        MarqueeFrame.Height = rect.Height;
        MarqueeFrame.Visibility = Visibility.Visible;

        var prior = marqueeHits;
        marqueeHits = hits;
        IEnumerable<Photo> removed = prior == null ? (marqueeAdditive ? [] : before.Except(hits)) : prior.Except(hits);
        ApplyMarqueeSelection(marqueeAdditive ? removed.Where(photo => !before.Contains(photo)) : removed, hits);
    }

    void ApplyMarqueeSelection(IEnumerable<Photo> removed, IEnumerable<Photo> added)
    {
        updatingSelection = true;
        foreach (var photo in removed)
            if (photo.Selected) { photo.Selected = false; selectedCount--; }
        foreach (var photo in added)
            if (!photo.Selected) { photo.Selected = true; selectedCount++; }
        updatingSelection = false;
        NotifySelection();
    }

    void Gallery_MarqueeUp(object sender, MouseButtonEventArgs e)
    {
        if (MarqueeActive && marqueeViewport is { } viewport)
        {
            UpdateMarquee(e.GetPosition(viewport));
            EndMarquee(restore: false);
            e.Handled = true; // A drag ending on a tile must not toggle it.
        }
        else EndMarquee(restore: false);
    }

    void Gallery_MarqueeCaptureLost(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Gallery)) EndMarquee(restore: true);
    }

    internal void EndMarquee(bool restore)
    {
        if (marqueeViewport == null) return;
        var before = marqueeBefore;
        var hits = marqueeHits;
        marqueeViewport = null;
        marqueeBefore = marqueeHits = null;
        MarqueeFrame.Visibility = Visibility.Collapsed;
        if (Gallery.IsMouseCaptured) Gallery.ReleaseMouseCapture();
        if (restore && before != null && hits != null) ApplyMarqueeSelection(hits.Except(before), before);
        else if (before != null) NotifySelection();
    }
}
