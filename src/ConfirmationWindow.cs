using System.Windows;
using System.Windows.Controls;

namespace Shiguang;

public sealed class ConfirmationWindow : Window
{
    public Button CancelButton { get; }
    public Button ConfirmButton { get; }
    public ConfirmationWindow(string title, string message, string action, bool danger = false, IReadOnlyList<Photo>? photos = null)
    {
        Title = "时光相册"; Width = 480; SizeToContent = SizeToContent.Height; MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 80);
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeManager.Attach(this);
        var root = new StackPanel(); ThemeManager.Resource(root, Panel.BackgroundProperty, "Bg"); Content = root;
        var content = new StackPanel { Margin = new(24, 22, 24, 20) }; root.Children.Add(content);
        var heading = new DockPanel { Margin = new(0, 0, 0, 12) };
        var icon = new FluentIcon { Kind = action.Contains("删除") || action.Contains("回收") ? "delete" : "info", Width = 24, Height = 24, Margin = new(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        ThemeManager.Resource(icon, Control.ForegroundProperty, danger ? "Danger" : "Muted"); heading.Children.Add(icon);
        heading.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }); content.Children.Add(heading);
        var copy = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = UiMetrics.Body, LineHeight = 22 };
        ThemeManager.Resource(copy, TextBlock.ForegroundProperty, "Muted"); content.Children.Add(new ScrollViewer { Content = copy, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        if (photos?.Count > 0)
        {
            var preview = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 16, 0, 0) };
            foreach (var photo in photos.Take(5)) preview.Children.Add(new ThumbnailImage { File = photo.ThumbPath, Width = 64, Height = 52, Stretch = System.Windows.Media.Stretch.UniformToFill, Margin = new(0, 0, 6, 0) });
            content.Children.Add(preview);
            var scope = new TextBlock { Text = $"{photos.Count:N0} 个项目 · {Photo.FormatSize(photos.Sum(p => p.Size))}", FontSize = 13, Margin = new(0, 10, 0, 0) };
            ThemeManager.Resource(scope, TextBlock.ForegroundProperty, "Muted"); content.Children.Add(scope);
        }
        var footer = new Border { BorderThickness = new(0, 1, 0, 0), Padding = new(24, 14, 24, 14) };
        ThemeManager.Resource(footer, Border.BackgroundProperty, "Side"); ThemeManager.Resource(footer, Border.BorderBrushProperty, "Line"); root.Children.Add(footer);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Child = buttons;
        CancelButton = Dialogs.Button("取消", () => DialogResult = false); CancelButton.IsCancel = true; CancelButton.FontSize = 14; CancelButton.Margin = new(0, 0, 8, 0); buttons.Children.Add(CancelButton);
        ConfirmButton = Dialogs.Button(action, () => DialogResult = true, !danger); ConfirmButton.FontSize = 14; ConfirmButton.Margin = new(0);
        if (danger) ConfirmButton.Style = (Style)FindResource("DangerButton");
        buttons.Children.Add(ConfirmButton);
        Border? scrim = null; Panel? ownerPanel = null;
        Loaded += (_, _) =>
        {
            if (Owner?.Content is Grid panel)
            {
                ownerPanel = panel; scrim = new Border { IsHitTestVisible = false };
                ThemeManager.Resource(scrim, Border.BackgroundProperty, "DialogScrim");
                Grid.SetRowSpan(scrim, 100); Grid.SetColumnSpan(scrim, 100); Panel.SetZIndex(scrim, 10000); panel.Children.Add(scrim);
            }
        };
        Closed += (_, _) => { if (scrim != null) ownerPanel?.Children.Remove(scrim); };
        Loaded += (_, _) => CancelButton.Focus();
    }
}
