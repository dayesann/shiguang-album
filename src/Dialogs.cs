using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Shiguang;

public static class Dialogs
{
    public static Window Shell(Window owner, string title, double width, double height, out DockPanel body)
    {
        body = new DockPanel { Margin = new Thickness(25) };
        var root = new Grid(); ThemeManager.Resource(root, Panel.BackgroundProperty, "Bg"); root.Children.Add(body);
        var window = new Window { Owner = owner, Title = title, Width = width, Height = height, MinWidth = Math.Min(width, 500), MinHeight = Math.Min(height, 300), WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root };
        ThemeManager.Attach(window); return window;
    }
    public static string? Prompt(Window owner, string title, string description, string initial = "", bool allowEmpty = false)
    {
        var w = Shell(owner, title, 480, 250, out var body); w.ResizeMode = ResizeMode.NoResize; w.MinHeight = 0; w.SizeToContent = SizeToContent.Height;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); body.Children.Add(actions);
        var input = new TextBox { Text = initial, MaxLength = 160, Height = UiMetrics.Control, VerticalAlignment = VerticalAlignment.Top };
        var cancel = Button("取消", () => w.DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel);
        var validation = new TextBlock { Text = "请输入内容后继续。", FontSize = 13, Visibility = Visibility.Collapsed, Margin = new(0, 8, 0, 0) }; ThemeManager.Resource(validation, TextBlock.ForegroundProperty, "Warning"); DockPanel.SetDock(validation, Dock.Bottom); body.Children.Add(validation);
        input.TextChanged += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) validation.Visibility = Visibility.Collapsed; };
        var ok = Button("确定", () => { if (allowEmpty || !string.IsNullOrWhiteSpace(input.Text)) w.DialogResult = true; else { validation.Visibility = Visibility.Visible; input.Focus(); } }, true); ok.IsDefault = true; actions.Children.Add(ok);
        var text = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 14) }; DockPanel.SetDock(text, Dock.Top); body.Children.Add(text); body.Children.Add(input);
        w.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return w.ShowDialog() == true ? input.Text : null;
    }
    public static Album? ChooseAlbum(Window owner, List<Album> albums, Func<string, Album>? create = null, int selectionCount = 0)
    {
        var w = Shell(owner, "加入相册", 580, 500, out var body);
        var list = new ListBox { ItemsSource = albums, DisplayMemberPath = "Label", SelectedIndex = 0, BorderThickness = new(0), Padding = new(10) };
        ThemeManager.Resource(list, Control.BackgroundProperty, "Surface"); ThemeManager.Resource(list, Control.ForegroundProperty, "Text");
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12))); itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 16d)); list.ItemContainerStyle = itemStyle;
        var top = new DockPanel { Margin = new(0, 0, 0, 18) }; DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        if (create != null)
        {
            var add = Button("新建相册", () => { var name = Prompt(w, "新建相册", "相册名称（只保存引用）"); if (string.IsNullOrWhiteSpace(name)) return; var album = create(name.Trim()); albums.Add(album); list.Items.Refresh(); list.SelectedItem = album; }); DockPanel.SetDock(add, Dock.Right); top.Children.Add(add);
        }
        top.Children.Add(new TextBlock { Text = selectionCount > 0 ? $"将 {selectionCount:N0} 个项目加入相册\n只增加引用，原文件保持原位" : "选择相册，只增加引用", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); body.Children.Add(actions);
        var cancel = Button("取消", () => w.DialogResult = false); cancel.IsCancel = true; actions.Children.Add(cancel);
        var ok = Button("添加到相册", () => w.DialogResult = true, true); ok.IsDefault = true; ok.IsEnabled = list.SelectedItem is Album; actions.Children.Add(ok);
        var content = new Grid(); content.Children.Add(list); var empty = new TextBlock { Text = "还没有相册\n点击“新建相册”后即可加入所选项目", TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, LineHeight = 28, IsHitTestVisible = false, Visibility = albums.Count == 0 ? Visibility.Visible : Visibility.Collapsed }; ThemeManager.Resource(empty, TextBlock.ForegroundProperty, "Muted"); content.Children.Add(empty); body.Children.Add(content);
        list.SelectionChanged += (_, _) => { ok.IsEnabled = list.SelectedItem is Album; empty.Visibility = list.HasItems ? Visibility.Collapsed : Visibility.Visible; };
        return w.ShowDialog() == true ? list.SelectedItem as Album : null;
    }
    public static void Text(Window owner, string title, string text)
    {
        var w = new ConfirmationWindow(title, text, "关闭") { Owner = owner };
        w.CancelButton.Visibility = Visibility.Collapsed;
        w.Loaded += (_, _) => w.ConfirmButton.Focus();
        w.ShowDialog();
    }
    public static Button Button(string title, Action action, bool primary = false)
    {
        var b = new Button { Content = title };
        System.Windows.Automation.AutomationProperties.SetName(b, title);
        if (primary) b.Style = (Style)Application.Current.FindResource("PrimaryButton");
        b.Click += (_, _) => action(); return b;
    }
    public static StackPanel LabeledIcon(string icon, string text, string? foregroundResource = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var graphic = new FluentIcon { Kind = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        if (foregroundResource != null) ThemeManager.Resource(graphic, Control.ForegroundProperty, foregroundResource);
        row.Children.Add(graphic);
        row.Children.Add(new TextBlock { Text = text, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
    public static bool Confirm(Window owner, string title, string message, string confirmText, bool danger = false) =>
        new ConfirmationWindow(title, message, confirmText, danger) { Owner = owner }.ShowDialog() == true;
}

public sealed class ArchiveWindow : Window
{
    public ArchiveWindow(List<ArchiveItem> plan, string target)
    {
        Title = "年月归档 · 预览"; Width = 1200; Height = 720; MinWidth = 850; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeManager.Attach(this);
        var shell = new Grid(); ThemeManager.Resource(shell, Panel.BackgroundProperty, "Bg"); Content = shell;
        var body = new DockPanel { Margin = new(25) }; shell.Children.Add(body);
        var heading = new StackPanel(); DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "确认每张照片要去的地方", FontSize = 28, FontWeight = FontWeights.SemiBold });
        var summary = new TextBlock { Text = $"共 {plan.Count:N0} 项可移动 · 目标 {target}", Margin = new(0, 10, 0, 8), TextWrapping = TextWrapping.Wrap, FontSize = 16 };
        ThemeManager.Resource(summary, TextBlock.ForegroundProperty, "Muted"); heading.Children.Add(summary);
        var warning = new TextBlock { Text = "拍摄日期可靠的照片默认勾选。推断或冲突日期默认不勾选；勾选表示接受所示日期。", Margin = new(0, 0, 0, 16), TextWrapping = TextWrapping.Wrap, FontSize = 16 };
        ThemeManager.Resource(warning, TextBlock.ForegroundProperty, "Warning"); heading.Children.Add(warning);
        var footer = new DockPanel { Margin = new(0, 18, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        void Update() => count.Text = $"已勾选 {plan.Count(i => i.Selected):N0} 项 · {Photo.FormatSize(plan.Where(i => i.Selected).Sum(i => i.Photo.Size))}";
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons); footer.Children.Add(count);
        buttons.Children.Add(Dialogs.Button("取消", () => DialogResult = false));
        buttons.Children.Add(Dialogs.Button("执行所选移动", () => { if (plan.Any(i => i.Selected)) DialogResult = true; }, true));
        var grid = new DataGrid { ItemsSource = plan, SelectionMode = DataGridSelectionMode.Extended };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "移动", Binding = new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 50 });
        foreach (var col in new[] { ("日期", "Date", 100d), ("日期来源", "DateSource", 115d), ("原位置", "Source", 300d), ("目标位置", "Destination", 320d), ("说明", "Note", 230d) })
        {
            var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis)); style.Setters.Add(new Setter(ToolTipProperty, new Binding(col.Item2)));
            grid.Columns.Add(new DataGridTextColumn { Header = col.Item1, Binding = new Binding(col.Item2), IsReadOnly = true, Width = col.Item3, ElementStyle = style });
        }
        body.Children.Add(grid); foreach (var i in plan) i.PropertyChanged += (_, _) => Update(); Update();
    }
}

public enum TrashRequestedAction { None, Restore, Purge }

public sealed class DuplicateWindow : Window
{
    readonly List<DuplicateGroup> groups;
    readonly Dictionary<string, bool> selected = [];
    readonly ListBox groupList = new();
    readonly WrapPanel cards = new();
    readonly StackPanel emptyState = new();
    readonly TextBlock info = new();
    readonly ComboBox kind = new();
    readonly Button applyButton;
    public List<Photo> ToDelete { get; private set; } = [];
    public Dictionary<string, string> Keepers { get; private set; } = [];
    public bool OpenTrashRequested { get; private set; }
    public string CurrentKind => kind.SelectedIndex == 0 ? "exact" : "similar";
    public Button ApplyButton => applyButton;

    public DuplicateWindow(List<DuplicateGroup> groups)
    {
        this.groups = groups;
        Title = "重复照片清理 · 时光相册";
        Width = 1220;
        Height = 820;
        MinWidth = 960;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeManager.Attach(this);
        foreach (var group in groups)
            foreach (var photo in group.Photos)
                selected[photo.Id] = group.Kind == "exact" && photo.Id != group.Keeper;

        var root = new Grid();
        ThemeManager.Resource(root, Panel.BackgroundProperty, "Bg");
        Content = root;
        var body = new Grid { Margin = new(28, 22, 28, 22) };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(body);

        var header = new Grid { Margin = new(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(Text("重复照片", UiMetrics.Title, false, true));
        heading.Children.Add(Text("使用已建立索引快速筛选；完全重复默认选中副本，近似候选只给建议，每一组都需要人工确认。", UiMetrics.Body, true, margin: new(0, 8, 0, 0)));
        header.Children.Add(heading);
        kind.Width = 240;
        kind.Height = UiMetrics.Control;
        kind.VerticalAlignment = VerticalAlignment.Center;
        kind.Margin = new(24, 0, 0, 0);
        kind.ItemsSource = new[] { $"完全重复（{groups.Count(g => g.Kind == "exact")} 组）", $"近似候选（{groups.Count(g => g.Kind == "similar")} 组）" };
        kind.SelectedIndex = 0;
        kind.SelectionChanged += (_, _) => RefreshGroups();
        System.Windows.Automation.AutomationProperties.SetName(kind, "重复类型");
        Grid.SetColumn(kind, 1);
        header.Children.Add(kind);
        body.Children.Add(header);

        var content = new Grid();
        content.ColumnDefinitions.Add(new() { Width = new(250) });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetRow(content, 1);
        body.Children.Add(content);

        var groupFrame = new Border { Margin = new(0, 0, 18, 0), CornerRadius = new(8), BorderThickness = new(1), Padding = new(6) };
        ThemeManager.Resource(groupFrame, Border.BackgroundProperty, "Surface");
        ThemeManager.Resource(groupFrame, Border.BorderBrushProperty, "Line");
        groupFrame.Child = groupList;
        groupList.DisplayMemberPath = nameof(DuplicateGroup.Label);
        groupList.BorderThickness = new(0);
        groupList.Background = Brushes.Transparent;
        groupList.ItemContainerStyle = ListItemStyle();
        groupList.SelectionChanged += (_, _) => ShowGroup();
        content.Children.Add(groupFrame);

        var right = new Grid();
        right.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetColumn(right, 1);
        content.Children.Add(right);
        var scroll = new ScrollViewer
        {
            Content = cards,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        right.Children.Add(scroll);

        emptyState.HorizontalAlignment = HorizontalAlignment.Center;
        emptyState.VerticalAlignment = VerticalAlignment.Center;
        emptyState.Margin = new(32);
        var emptyIcon = new FluentIcon { Kind = "checkmark_circle", Width = 38, Height = 38, HorizontalAlignment = HorizontalAlignment.Center };
        ThemeManager.Resource(emptyIcon, Control.ForegroundProperty, "Success");
        emptyState.Children.Add(emptyIcon);
        var emptyTitle = Text("没有发现重复照片", 20, false, true, new(0, 16, 0, 7));
        emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        emptyState.Children.Add(emptyTitle);
        var emptyCopy = Text("可以切换上方类型，或稍后重新检查。", 14, true);
        emptyCopy.HorizontalAlignment = HorizontalAlignment.Center;
        emptyState.Children.Add(emptyCopy);
        right.Children.Add(emptyState);

        var footer = new Grid { Margin = new(0, 18, 0, 0) };
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetRow(footer, 2);
        body.Children.Add(footer);
        info.FontSize = 14;
        info.VerticalAlignment = VerticalAlignment.Center;
        ThemeManager.Resource(info, TextBlock.ForegroundProperty, "Muted");
        footer.Children.Add(info);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var trash = Dialogs.Button("最近删除", () => { OpenTrashRequested = true; DialogResult = false; });
        trash.Content = IconText("arrow_clockwise", "最近删除");
        trash.ToolTip = "查看可恢复项目";
        actions.Children.Add(trash);
        var close = Dialogs.Button("关闭", () => DialogResult = false); close.IsCancel = true; actions.Children.Add(close);
        applyButton = Dialogs.Button("移入最近删除", Apply, true);
        applyButton.ToolTip = "先移入可恢复目录，不会立即永久删除";
        System.Windows.Automation.AutomationProperties.SetName(applyButton, "将所选重复照片移入最近删除");
        actions.Children.Add(applyButton);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);

        RefreshGroups();
    }

    void RefreshGroups()
    {
        groupList.ItemsSource = groups.Where(g => g.Kind == CurrentKind).ToList();
        groupList.SelectedIndex = groupList.Items.Count > 0 ? 0 : -1;
        ShowGroup();
        UpdateInfo();
    }

    void ShowGroup()
    {
        cards.Children.Clear();
        var hasGroup = groupList.SelectedItem is DuplicateGroup;
        emptyState.Visibility = hasGroup ? Visibility.Collapsed : Visibility.Visible;
        if (groupList.SelectedItem is not DuplicateGroup group) return;
        foreach (var photo in group.Photos) cards.Children.Add(PhotoCard(group, photo));
    }

    Border PhotoCard(DuplicateGroup group, Photo photo)
    {
        var stack = new StackPanel { Width = 224 };
        var imageFrame = new Border { Height = 154, CornerRadius = new(6), ClipToBounds = true, Margin = new(0, 0, 0, 10) };
        ThemeManager.Resource(imageFrame, Border.BackgroundProperty, "Placeholder");
        imageFrame.Child = new ThumbnailImage { File = photo.ThumbPath, Stretch = Stretch.UniformToFill };
        stack.Children.Add(imageFrame);

        bool keeper = photo.Id == group.Keeper;
        var state = Text(keeper ? "建议保留" : group.Kind == "exact" ? "重复副本" : "近似候选", 13, !keeper, true, new(0, 0, 0, 5));
        if (keeper) ThemeManager.Resource(state, TextBlock.ForegroundProperty, "Success");
        stack.Children.Add(state);
        stack.Children.Add(new TextBlock { Text = photo.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = photo.Path });
        stack.Children.Add(Text(photo.Details, 13, true, margin: new(0, 6, 0, 0)));
        if (group.Kind == "similar")
            stack.Children.Add(Text($"清晰度 {photo.Sharpness:0} · 平均亮度 {photo.Brightness:0}", 12, true, margin: new(0, 5, 0, 0)));

        var check = new CheckBox
        {
            Content = group.Kind == "exact" ? "清理此副本" : "选择清理",
            IsChecked = selected.GetValueOrDefault(photo.Id),
            Margin = new(0, 14, 0, 8),
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.FindResource("ChoiceCheck")
        };
        System.Windows.Automation.AutomationProperties.SetName(check, (keeper ? "保留项：" : "待清理项：") + photo.Name);
        check.Checked += (_, _) => { selected[photo.Id] = true; UpdateInfo(); };
        check.Unchecked += (_, _) => { selected[photo.Id] = false; UpdateInfo(); };
        stack.Children.Add(check);

        var compare = new StackPanel { Orientation = Orientation.Horizontal };
        var view = Dialogs.Button("查看", () => new PhotoViewer(group.Photos, group.Photos.IndexOf(photo)) { Owner = this }.ShowDialog());
        view.Content = IconText("zoom_in", "查看");
        compare.Children.Add(view);
        if (group.Photos.Count > 1)
        {
            var other = group.Photos.First(x => x.Id != photo.Id && x.Id == group.Keeper || x.Id != photo.Id);
            var side = Dialogs.Button("并排比较", () => new CompareWindow(other, photo) { Owner = this }.ShowDialog());
            side.Content = IconText("image_multiple", "比较");
            compare.Children.Add(side);
        }
        stack.Children.Add(compare);

        var card = new Border
        {
            Child = stack,
            Padding = new(12),
            Margin = new(0, 0, 14, 14),
            CornerRadius = new(8),
            BorderThickness = new(1)
        };
        ThemeManager.Resource(card, Border.BackgroundProperty, "Surface");
        ThemeManager.Resource(card, Border.BorderBrushProperty, "Line");
        return card;
    }

    void UpdateInfo()
    {
        var activeGroups = groups.Where(g => g.Kind == CurrentKind).ToList();
        var chosen = activeGroups.SelectMany(g => g.Photos).Where(p => selected.GetValueOrDefault(p.Id)).DistinctBy(p => p.Id).ToList();
        var invalid = activeGroups.Any(g => g.Photos.All(p => selected.GetValueOrDefault(p.Id)));
        applyButton.IsEnabled = chosen.Count > 0 && !invalid;
        info.Text = invalid
            ? "每组至少保留一张照片。"
            : chosen.Count == 0
                ? "尚未选择要清理的照片。"
                : $"已选择 {chosen.Count:N0} 张 · 预计释放 {Photo.FormatSize(chosen.Sum(p => p.Size))} · 删除后可恢复";
        ThemeManager.Resource(info, TextBlock.ForegroundProperty, invalid ? "Danger" : "Muted");
    }
    void Apply()
    {
        var chosen = new List<Photo>();
        var keepers = new Dictionary<string, string>();
        foreach (var group in groups.Where(g => g.Kind == CurrentKind))
        {
            var remove = group.Photos.Where(p => selected.GetValueOrDefault(p.Id)).ToList();
            var remain = group.Photos.Where(p => !selected.GetValueOrDefault(p.Id)).ToList();
            if (remain.Count == 0 || remove.Count == 0) continue;
            chosen.AddRange(remove);
            if (group.Kind == "exact")
                foreach (var photo in remove)
                    keepers[photo.Id] = remain[0].Id;
        }
        ToDelete = chosen.DistinctBy(p => p.Id).ToList();
        Keepers = keepers;
        if (ToDelete.Count > 0) DialogResult = true;
    }

    static TextBlock Text(string value, double size, bool muted, bool semibold = false, Thickness? margin = null)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal, Margin = margin ?? new(), VerticalAlignment = VerticalAlignment.Center };
        ThemeManager.Resource(text, TextBlock.ForegroundProperty, muted ? "Muted" : "Text");
        return text;
    }
    static StackPanel IconText(string icon, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new FluentIcon { Kind = icon, Width = 18, Height = 18 });
        row.Children.Add(new TextBlock { Text = text, Margin = new(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
    static Style ListItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 12, 14, 12)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 2, 0, 2)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("Text")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 15d));
        var template = new ControlTemplate(typeof(ListBoxItem));
        var border = new FrameworkElementFactory(typeof(Border), "Frame");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = border;
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Selected"), "Frame"));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("SelectedText")));
        template.Triggers.Add(selected);
        var hover = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Hover"), "Frame"));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = ListBoxItem.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Accent"), "Frame"));
        template.Triggers.Add(focus);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
}

public sealed class TrashWindow : Window
{
    readonly List<Photo> items;
    readonly TextBlock info = new();
    readonly Button restoreButton;
    readonly Button purgeButton;
    public IReadOnlyList<Photo> SelectedPhotos => items.Where(p => p.Selected).ToList();
    public TrashRequestedAction RequestedAction { get; private set; }

    public TrashWindow(List<Photo> items)
    {
        this.items = items;
        foreach (var photo in items) photo.Selected = false;
        Title = "最近删除 · 时光相册";
        Width = 1080;
        Height = 760;
        MinWidth = 880;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeManager.Attach(this);

        var shell = new Grid();
        ThemeManager.Resource(shell, Panel.BackgroundProperty, "Bg");
        Content = shell;
        var root = new Grid { Margin = new(28, 22, 28, 22) };
        shell.Children.Add(root);
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = "回收站", FontSize = UiMetrics.Title, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "项目保存在来源目录的受控回收文件夹中。恢复不会覆盖同位置已有文件。", FontSize = UiMetrics.Body, Margin = new(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        ThemeManager.Resource((TextBlock)heading.Children[1], TextBlock.ForegroundProperty, "Muted");
        root.Children.Add(heading);

        var content = new Grid();
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        var rows = new StackPanel();
        foreach (var photo in items) rows.Children.Add(TrashRow(photo));
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 8, 0) };
        content.Children.Add(scroll);
        if (items.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var icon = new FluentIcon { Kind = "checkmark_circle", Width = 38, Height = 38, HorizontalAlignment = HorizontalAlignment.Center };
            ThemeManager.Resource(icon, Control.ForegroundProperty, "Success");
            empty.Children.Add(icon);
            empty.Children.Add(new TextBlock { Text = "最近删除是空的", FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 16, 0, 7) });
            empty.Children.Add(new TextBlock { Text = "清理重复照片后，可在这里恢复。", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
            ThemeManager.Resource((TextBlock)empty.Children[2], TextBlock.ForegroundProperty, "Muted");
            content.Children.Add(empty);
        }

        var footer = new Grid { Margin = new(0, 18, 0, 0) };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        info.FontSize = 14;
        info.VerticalAlignment = VerticalAlignment.Center;
        ThemeManager.Resource(info, TextBlock.ForegroundProperty, "Muted");
        footer.Children.Add(info);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = Dialogs.Button("关闭", () => DialogResult = false); close.IsCancel = true; close.Margin = new(0, 0, 10, 0); actions.Children.Add(close);
        purgeButton = Dialogs.Button("永久删除", ConfirmPurge);
        ThemeManager.Resource(purgeButton, Control.ForegroundProperty, "Danger");
        purgeButton.Content = Dialogs.LabeledIcon("delete", "永久删除", "Danger");
        purgeButton.Margin = new(0, 0, 12, 0);
        actions.Children.Add(purgeButton);
        restoreButton = Dialogs.Button("恢复所选", () => Finish(TrashRequestedAction.Restore), true);
        restoreButton.Margin = new(0);
        actions.Children.Add(restoreButton);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        UpdateInfo();
    }

    Border TrashRow(Photo photo)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

        var imageFrame = new Border { Width = 112, Height = 76, CornerRadius = new(6), ClipToBounds = true };
        ThemeManager.Resource(imageFrame, Border.BackgroundProperty, "Placeholder");
        imageFrame.Child = new ThumbnailImage { File = photo.ThumbPath, Stretch = Stretch.UniformToFill };
        row.Children.Add(imageFrame);

        var copy = new StackPanel { Margin = new(16, 2, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = photo.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = photo.Name });
        var path = new TextBlock { Text = photo.OriginalPath, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 8, 0, 0), ToolTip = photo.OriginalPath };
        ThemeManager.Resource(path, TextBlock.ForegroundProperty, "Muted");
        copy.Children.Add(path);
        var date = new TextBlock { Text = "删除时间：" + photo.TrashDate, FontSize = 13, Margin = new(0, 8, 0, 0) };
        ThemeManager.Resource(date, TextBlock.ForegroundProperty, "Muted");
        copy.Children.Add(date);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);

        var check = new CheckBox
        {
            Content = "选择",
            IsChecked = photo.Selected,
            Style = (Style)Application.Current.FindResource("ChoiceCheck"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(18, 0, 6, 0)
        };
        System.Windows.Automation.AutomationProperties.SetName(check, "选择项目 " + photo.Name);
        check.Checked += (_, _) => { photo.Selected = true; UpdateInfo(); };
        check.Unchecked += (_, _) => { photo.Selected = false; UpdateInfo(); };
        Grid.SetColumn(check, 2);
        row.Children.Add(check);

        var item = new Border { Child = row, Padding = new(8), Margin = new(0, 0, 0, 6), CornerRadius = new(8), BorderThickness = new(1) };
        ThemeManager.Resource(item, Border.BackgroundProperty, "Surface");
        ThemeManager.Resource(item, Border.BorderBrushProperty, "Line");
        return item;
    }

    void UpdateInfo()
    {
        var chosen = SelectedPhotos;
        info.Text = chosen.Count == 0 ? "选择项目后可恢复或永久删除。" : $"已选择 {chosen.Count:N0} 项 · {Photo.FormatSize(chosen.Sum(p => p.Size))}";
        restoreButton.IsEnabled = chosen.Count > 0;
        purgeButton.IsEnabled = chosen.Count > 0;
    }

    void ConfirmPurge()
    {
        if (SelectedPhotos.Count == 0) return;
        if (new ConfirmationWindow($"永久删除 {SelectedPhotos.Count:N0} 个项目？", "这些文件和相册引用将从磁盘中永久删除，无法恢复。", "永久删除", true, SelectedPhotos) { Owner = this }.ShowDialog() != true) return;
        Finish(TrashRequestedAction.Purge);
    }
    void Finish(TrashRequestedAction action)
    {
        RequestedAction = action;
        DialogResult = true;
    }
}

public sealed class RecoveryWindow : Window
{
    public bool CancelUntouched { get; private set; }
    public RecoveryWindow(List<FileOperation> operations)
    {
        Title = "文件操作日志与任务恢复"; Width = 1100; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeManager.Attach(this);
        var shell = new Grid(); ThemeManager.Resource(shell, Panel.BackgroundProperty, "Bg"); Content = shell;
        var body = new DockPanel { Margin = new(25) }; shell.Children.Add(body);
        var intro = new TextBlock { Text = "未完成任务可校验后继续。取消只处理源文件完整、目标尚未生成的任务；不会撤销已完成的移动。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 18), FontSize = 16 };
        ThemeManager.Resource(intro, TextBlock.ForegroundProperty, "Muted"); DockPanel.SetDock(intro, Dock.Top); body.Children.Add(intro);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
        footer.Children.Add(Dialogs.Button("关闭", () => DialogResult = false));
        footer.Children.Add(Dialogs.Button("取消未开始任务", () => { CancelUntouched = true; DialogResult = true; }));
        footer.Children.Add(Dialogs.Button("校验并继续未完成任务", () => DialogResult = true, true));
        var grid = new DataGrid { ItemsSource = operations, IsReadOnly = true };
        foreach (var c in new[] { ("状态", "State", 100d), ("操作", "Kind", 70d), ("源位置", "Source", 310d), ("目标位置", "Target", 310d), ("错误", "Error", 220d) }) grid.Columns.Add(new DataGridTextColumn { Header = c.Item1, Binding = new Binding(c.Item2), Width = c.Item3 });
        body.Children.Add(grid);
    }
}
