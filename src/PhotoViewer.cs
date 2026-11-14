using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Shiguang;

public sealed partial class PhotoViewer : Window
{
    readonly Func<int, Photo?> fetch;
    readonly int count;
    int index, generation;
    CancellationTokenSource requestCancel = new();
    readonly Image image = new() { Stretch = Stretch.Uniform };
    readonly TextBlock description = new() { TextWrapping = TextWrapping.Wrap, FontSize = UiMetrics.Caption };
    readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly ScaleTransform scale = new(1, 1);
    readonly TextBlock zoomLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 8, 0) };
    double fittedWidth = 900, fittedHeight = 650;
    public PhotoViewer(IReadOnlyList<Photo> photos, int index) : this(i => photos[i], photos.Count, index) { }
    public PhotoViewer(Func<int, Photo?> fetch, int count, int index)
    {
        if (count < 1) throw new ArgumentException("没有可预览的照片");
        this.fetch = fetch; this.count = count; this.index = Math.Clamp(index, 0, count - 1); Title = "查看照片"; Width = 1250; Height = 850; MinWidth = 700; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new DockPanel { Margin = new(20) }; var root = new Grid { Background = Background }; root.Children.Add(body); Content = root;
        ThemeManager.Attach(this); ThemeManager.Resource(this, BackgroundProperty, "Bg"); ThemeManager.Resource(this, ForegroundProperty, "Text"); ThemeManager.Resource(root, Panel.BackgroundProperty, "Bg"); ThemeManager.Resource(description, TextBlock.ForegroundProperty, "Muted"); ThemeManager.Resource(zoomLabel, TextBlock.ForegroundProperty, "Text");
        var toolbar = new Grid { Margin = new(0, 0, 0, 15) }; DockPanel.SetDock(toolbar, Dock.Top); body.Children.Add(toolbar);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; toolbar.Children.Add(buttons);
        buttons.Children.Add(IconButton("zoom_out", "缩小", () => Zoom(scale.ScaleX / 1.25), false)); buttons.Children.Add(zoomLabel); buttons.Children.Add(IconButton("zoom_in", "放大", () => Zoom(scale.ScaleX * 1.25), false));
        buttons.Children.Add(IconButton("grid", "适应窗口", Fit)); buttons.Children.Add(IconButton("full_screen_maximize", "全屏 F11", Fullscreen)); buttons.Children.Add(IconButton("dismiss", "关闭", Close));
        AddPlaybackControls(body);
        description.MaxHeight = 76; description.TextTrimming = TextTrimming.CharacterEllipsis;
        var bottom = new DockPanel { Margin = new(0, 15, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); body.Children.Add(bottom);
        var nav = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(nav, Dock.Right); bottom.Children.Add(nav);
        nav.Children.Add(IconButton("arrow_left", "上一张", () => Navigate(-1))); nav.Children.Add(IconButton("arrow_right", "下一张", () => Navigate(1))); bottom.Children.Add(description);
        scroll.Content = image; image.LayoutTransform = scale; body.Children.Add(scroll);
        scroll.PreviewMouseWheel += (_, e) => { Zoom(scale.ScaleX * (e.Delta > 0 ? 1.2 : 1 / 1.2)); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            if (e.Handled || Keyboard.Modifiers != ModifierKeys.None
                || e.OriginalSource is System.Windows.Controls.Primitives.RangeBase or ComboBoxItem or ComboBox) return;
            switch (e.Key)
            {
                case Key.Right or Key.Down or Key.PageDown:
                    Navigate(1);
                    break;
                case Key.Left or Key.Up or Key.PageUp:
                    Navigate(-1);
                    break;
                case Key.Space when e.OriginalSource is not Button and not CheckBox:
                    ToggleSlideshow();
                    break;
                case Key.Escape:
                    if (WindowStyle == WindowStyle.None) Fullscreen();
                    else Close();
                    break;
                case Key.F11:
                    Fullscreen();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        };
        Loaded += (_, _) => { if (AutoPlay) SetPlaying(true); Load(); }; Closed += (_, _) => { ClosePlayback(); generation++; requestCancel.Cancel(); requestCancel.Dispose(); };
        SizeChanged += (_, _) => { if (Math.Abs(scale.ScaleX - 1) < .01) Fit(); };
    }
    public static Button DarkButton(string text, Action action)
    {
        return Dialogs.Button(text, action);
    }
    static Button IconButton(string icon, string label, Action action, bool showLabel = false)
    {
        var button = DarkButton(label, action);
        if (!showLabel) button.Style = (Style)Application.Current.FindResource("IconButton");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new FluentIcon { Kind = icon });
        if (showLabel) content.Children.Add(new TextBlock { Text = label, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        button.Content = content; button.ToolTip = label; System.Windows.Automation.AutomationProperties.SetName(button, label); return button;
    }
    void Navigate(int offset) { index = (index + offset + count) % count; Load(); }
    async void Load()
    {
        slideTimer.Stop(); imageReady = false;
        requestCancel.Cancel(); requestCancel.Dispose(); requestCancel = new(); var token = requestCancel.Token;
        int request = ++generation; Photo? p; image.Source = null;
        try { p = fetch(index); }
        catch (Exception e) { SetPlaying(false); description.Text = "无法读取当前照片：" + e.Message; return; }
        if (p == null) { SetPlaying(false); description.Text = "当前项目不再可用，请关闭预览后刷新媒体库。"; return; }
        Title = p.Name + " · 时光相册";
        description.Text = $"{index + 1} / {count}   {p.Name}   {p.Details}\n{p.DateLabel} · {p.DateSource}   {p.Path}"; description.ToolTip = description.Text;
        try { var cache = await PreviewSession.Large(p, token); token.ThrowIfCancellationRequested(); var source = await Task.Run(() => Imaging.Decode(cache, 0), token); if (request != generation) return; image.Source = source; imageReady = true; Fit(); RestartSlideTimer(); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (request == generation) { SetPlaying(false); description.Text += "\n无法显示，已暂停自动播放：" + e.Message; } }
    }
    void Fit()
    {
        if (image.Source is not System.Windows.Media.Imaging.BitmapSource source) return;
        var ratio = Math.Min(Math.Max(100, scroll.ActualWidth - 30) / source.PixelWidth, Math.Max(100, scroll.ActualHeight - 30) / source.PixelHeight);
        fittedWidth = source.PixelWidth * ratio; fittedHeight = source.PixelHeight * ratio;
        image.Width = fittedWidth; image.Height = fittedHeight; Zoom(1);
    }
    void Zoom(double value) { scale.ScaleX = scale.ScaleY = Math.Clamp(value, .2, 12); zoomLabel.Text = $"{scale.ScaleX:0.0}×"; }
    void Fullscreen() { if (WindowStyle == WindowStyle.None) { WindowStyle = WindowStyle.SingleBorderWindow; WindowState = WindowState.Normal; } else { WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; } }
}

public sealed class CompareWindow : Window
{
    bool synchronizing, closed;
    readonly List<Image> images = [];
    readonly List<ScrollViewer> scrolls = [];
    readonly Slider zoom = new() { Minimum = 1, Maximum = 8, Value = 1, Width = 220, Margin = new(16, 0, 10, 0) };
    public CompareWindow(Photo left, Photo right)
    {
        Title = "并排比较 · 同步缩放与滚动"; Width = 1350; Height = 850; MinWidth = 900; MinHeight = 550; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new DockPanel { Margin = new(20) }; Content = body;
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 20) }; DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "同步缩放", VerticalAlignment = VerticalAlignment.Center }); top.Children.Add(zoom); top.Children.Add(Dialogs.Button("适应窗口", () => zoom.Value = 1));
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); body.Children.Add(grid);
        int col = 0;
        foreach (var p in new[] { left, right })
        {
            var panel = new DockPanel { Margin = new(6) }; Grid.SetColumn(panel, col++); grid.Children.Add(panel);
            var label = new TextBlock { Text = p.Name + "\n" + p.Details, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
            var image = new Image { Stretch = Stretch.Uniform }; images.Add(image);
            var scroll = new ScrollViewer { Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(25, 29, 37)) }; scrolls.Add(scroll); panel.Children.Add(scroll);
            scroll.ScrollChanged += (_, e) => { if (synchronizing || e.ExtentWidthChange != 0 || e.ExtentHeightChange != 0) return; synchronizing = true; foreach (var other in scrolls.Where(s => s != scroll)) { other.ScrollToHorizontalOffset(scroll.ScrollableWidth <= 0 ? 0 : scroll.HorizontalOffset / scroll.ScrollableWidth * other.ScrollableWidth); other.ScrollToVerticalOffset(scroll.ScrollableHeight <= 0 ? 0 : scroll.VerticalOffset / scroll.ScrollableHeight * other.ScrollableHeight); } synchronizing = false; };
        }
        zoom.ValueChanged += (_, _) => ResizeImages(); SizeChanged += (_, _) => ResizeImages(); Closed += (_, _) => closed = true;
        Loaded += async (_, _) =>
        {
            try { var aPath = await PreviewSession.Large(left); var a = await Task.Run(() => Imaging.Decode(aPath, 0)); if (closed) return; images[0].Source = a; var bPath = await PreviewSession.Large(right); var b = await Task.Run(() => Imaging.Decode(bPath, 0)); if (closed) return; images[1].Source = b; ResizeImages(); }
            catch (Exception e) { if (!closed) MessageBox.Show(this, e.Message, "无法读取照片"); }
        };
    }
    void ResizeImages() { for (int i = 0; i < images.Count; i++) { images[i].Width = Math.Max(200, scrolls[i].ActualWidth - 20) * zoom.Value; images[i].Height = Math.Max(200, scrolls[i].ActualHeight - 20) * zoom.Value; } }
}
