using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;

namespace Shiguang;

public sealed class SettingsWindow : Window
{
    readonly MainWindow main;
    readonly Catalog catalog;
    readonly StackPanel page = new();
    readonly ScrollViewer pageScroll;
    readonly TextBlock heading = Label("外观", UiMetrics.Title, false, true);
    readonly TextBlock description = Label("让时光相册以你喜欢的方式呈现。", 14, true);
    readonly TextBlock saved = Label("更改会自动保存", 13, true);
    readonly Dictionary<string, Button> navigation = [];
    readonly Dictionary<string, Button> themes = [];
    readonly Dictionary<string, TextBlock> indicators = [];
    TextBlock? effective;
    SettingRow? updateRow;
    CheckBox? autoDownload;
    string currentPage = "appearance";
    public string CurrentPage => currentPage;

    public SettingsWindow(MainWindow main, Catalog catalog)
    {
        this.main = main; this.catalog = catalog;
        Title = "设置 · 时光相册"; Width = 1060; Height = 790; MinWidth = 940; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ThemeManager.Attach(this);
        var root = new Grid(); Resource(root, Panel.BackgroundProperty, "Bg"); Content = root;
        root.ColumnDefinitions.Add(new() { Width = new(176) }); root.ColumnDefinitions.Add(new());
        var side = new Border { BorderThickness = new(0, 0, 1, 0), Padding = new(16, 22, 16, 18) };
        Resource(side, Border.BackgroundProperty, "Side"); Resource(side, Border.BorderBrushProperty, "Line"); root.Children.Add(side);
        var sideDock = new DockPanel(); side.Child = sideDock;
        var identity = new StackPanel { Margin = new(10, 0, 0, 24) }; identity.Children.Add(Label("设置", 24, false, true)); identity.Children.Add(Label("时光相册", 13, true, margin: new(0, 7, 0, 0))); DockPanel.SetDock(identity, Dock.Top); sideDock.Children.Add(identity);
        var nav = new StackPanel(); NavigationKeys.SetEnabled(nav, true); sideDock.Children.Add(nav);
        foreach (var (key, icon, text) in new[] { ("general", "settings", "常规"), ("appearance", "grid", "外观"), ("sources", "folder", "来源目录"), ("library", "apps_list", "媒体库"), ("backup", "copy", "备份与迁移"), ("shortcuts", "apps_list", "快捷键"), ("about", "info", "关于") })
        {
            var button = new Button { Style = (Style)FindResource("NavButton"), Content = IconText(icon, text), Tag = key };
            System.Windows.Automation.AutomationProperties.SetName(button, text);
            button.Click += (_, _) => SelectPage(key); navigation[key] = button; nav.Children.Add(button);
        }
        var right = new Grid(); Grid.SetColumn(right, 1); root.Children.Add(right);
        right.RowDefinitions.Add(new() { Height = new(84) }); right.RowDefinitions.Add(new()); right.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new(28, 23, 28, 0) }; header.Children.Add(heading); description.Margin = new(0, 7, 0, 0); header.Children.Add(description); right.Children.Add(header);
        pageScroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(28, 6, 28, 22) }; Grid.SetRow(pageScroll, 1); right.Children.Add(pageScroll);
        var footer = new Border { Padding = new(28, 12, 22, 12), BorderThickness = new(0, 1, 0, 0) }; Resource(footer, Border.BorderBrushProperty, "Line"); Grid.SetRow(footer, 2); right.Children.Add(footer);
        var footerDock = new DockPanel(); footer.Child = footerDock; var close = Dialogs.Button("返回媒体库", Close); close.Content = IconText("arrow_left", "返回媒体库"); DockPanel.SetDock(close, Dock.Right); footerDock.Children.Add(close); saved.VerticalAlignment = VerticalAlignment.Center; footerDock.Children.Add(saved);
        ThemeManager.Changed += ThemeChanged; main.UpdateChanged += RefreshUpdateRow;
        Closed += (_, _) => { ThemeManager.Changed -= ThemeChanged; main.UpdateChanged -= RefreshUpdateRow; };
        SelectPage("appearance");
    }
    static void Resource(FrameworkElement target, DependencyProperty property, string key) => ThemeManager.Resource(target, property, key);
    static TextBlock Label(string text, double size = 16, bool muted = false, bool bold = false, Thickness? margin = null)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = margin ?? new() };
        Resource(label, TextBlock.ForegroundProperty, muted ? "Muted" : "Text"); return label;
    }
    static StackPanel IconText(string icon, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(new FluentIcon { Kind = icon, Width = 18, Height = 18 }); row.Children.Add(new TextBlock { Text = text, Margin = new(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); return row;
    }
    static Border Card(UIElement content, Thickness? padding = null)
    {
        var border = new Border { Child = content, Padding = padding ?? new(16), CornerRadius = UiMetrics.SurfaceRadius, BorderThickness = new(1), Margin = new(0, 0, 0, 16) };
        Resource(border, Border.BackgroundProperty, "Surface"); Resource(border, Border.BorderBrushProperty, "Line"); return border;
    }
    void Section(string text, string? detail = null)
    {
        page.Children.Add(Label(text, UiMetrics.Section, false, true, new(0, 12, 0, 8)));
        if (detail != null) page.Children.Add(Label(detail, 14, true, margin: new(0, 0, 0, 18)));
    }
    public void SelectPage(string key)
    {
        currentPage = key; page.Children.Clear(); themes.Clear(); indicators.Clear(); effective = null; updateRow = null; autoDownload = null;
        foreach (var (id, button) in navigation) { bool active = id == key; if (active) Resource(button, Control.BackgroundProperty, "Selected"); else button.Background = Brushes.Transparent; Resource(button, Control.ForegroundProperty, active ? "SelectedText" : "Text"); }
        var titles = key switch
        {
            "general" => ("常规", "管理窗口关闭方式与后台任务。"),
            "sources" => ("来源目录", "连接照片与视频所在的位置，统一浏览散落在各处的回忆。"),
            "library" => ("媒体库", "查看媒体库状态与整理工具。"),
            "backup" => ("备份与迁移", "为相册留一份备份，或把完整媒体库带到新电脑。"),
            "shortcuts" => ("快捷键", "按使用场景查找键盘与鼠标操作。"),
            "about" => ("关于时光相册", "一个保留原始目录的本地照片与视频查看器。"),
            _ => ("外观", "让时光相册以你喜欢的方式呈现。")
        };
        heading.Text = titles.Item1; description.Text = titles.Item2;
        if (key == "general") BuildGeneral(); else if (key == "sources") BuildSources(); else if (key == "library") BuildLibrary(); else if (key == "backup") BuildBackup(); else if (key == "shortcuts") BuildShortcuts(); else if (key == "about") BuildAbout(); else BuildAppearance();
        pageScroll.ScrollToHome();
        saved.Visibility = key == "shortcuts" ? Visibility.Collapsed : Visibility.Visible;
        ThemeChanged();
    }
    void BuildAppearance()
    {
        Section("主题", "选择应用的背景与界面颜色，切换后立即生效。");
        var choices = new UniformGrid { Columns = 3, Margin = new(-5, 0, -5, 24) };
        foreach (var (key, title, detail) in new[] { ("light", "浅色", "明亮、清晰"), ("dark", "深色", "沉浸浏览"), ("system", "跟随系统", "自动匹配 Windows") })
        {
            var stack = new StackPanel(); stack.Children.Add(ThemePreview(key));
            var row = new DockPanel { Margin = new(0, 16, 0, 7) }; var indicator = Label("已选择", 12); indicator.VerticalAlignment = VerticalAlignment.Center; Resource(indicator, TextBlock.ForegroundProperty, "Accent"); DockPanel.SetDock(indicator, Dock.Right); row.Children.Add(indicator); row.Children.Add(Label(title, 17, false, true)); stack.Children.Add(row); stack.Children.Add(Label(detail, 12, true));
            var button = Dialogs.Button(title, () => ChooseTheme(key)); button.Style = (Style)FindResource("ChoiceCardButton"); button.Content = stack; button.Padding = new(12); button.Margin = new(5); button.BorderThickness = new(2); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.MinHeight = 184; button.ToolTip = "使用" + title + "主题";
            System.Windows.Automation.AutomationProperties.SetName(button, "主题：" + title); themes[key] = button; indicators[key] = indicator; choices.Children.Add(button);
        }
        page.Children.Add(choices);
        effective = Label("", UiMetrics.Caption, true, margin: new(0, 0, 0, 8)); page.Children.Add(effective);
        page.Children.Add(Label("跟随系统会自动匹配 Windows 的浅色或深色外观。", UiMetrics.Caption, true, margin: new(0, 0, 0, 16)));
        page.Children.Add(Label("预览使用 AI 生成的风景插图。", 12, true));
    }
    void BuildGeneral()
    {
        Section("关闭主窗口");
        var closeToTray = new CheckBox { Content = "关闭窗口时最小化到系统托盘", IsChecked = WindowBehaviorSettings.CloseToTray, Style = (Style)FindResource("ChoiceCheck"), Margin = new(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(closeToTray, "关闭窗口时最小化到系统托盘");
        page.Children.Add(closeToTray);
        page.Children.Add(Label("默认开启。关闭主窗口后，扫描、查重和预览任务继续运行。", UiMetrics.Caption, true, margin: new(0, 0, 0, 8)));
        page.Children.Add(Label("双击任务栏右下角的托盘图标，或再次点击启动入口，即可返回。", UiMetrics.Caption, true, margin: new(0, 0, 0, 16)));
        bool updating = false;
        closeToTray.Click += (_, _) =>
        {
            if (updating) return;
            try { WindowBehaviorSettings.SetCloseToTray(closeToTray.IsChecked == true); saved.Text = "窗口关闭方式已保存"; }
            catch (Exception error) { updating = true; closeToTray.IsChecked = WindowBehaviorSettings.CloseToTray; updating = false; ShowError(error); }
        };
        page.Children.Add(ActionRow("退出程序", "彻底退出请使用此入口或托盘菜单；有任务时会先确认并安全停止。", "退出程序", () => { Close(); main.RequestExit(); }));
    }
    UIElement ThemePreview(string key)
    {
        // The preview always uses a bundled AI landscape; never query personal media here.
        Grid Mini(bool light)
        {
            var grid = new Grid { Height = 98, Background = Brush(light ? "#F5F7FA" : "#20242C"), ClipToBounds = true };
            grid.ColumnDefinitions.Add(new() { Width = new(28) }); grid.ColumnDefinitions.Add(new());
            grid.Children.Add(new Border { Background = Brush(light ? "#E4EAF3" : "#333B49"), Margin = new(0, 0, 0, 0) });
            var body = new StackPanel { Margin = new(8, 13, 8, 10) }; Grid.SetColumn(body, 1); grid.Children.Add(body);
            body.Children.Add(new Border { Width = 48, Height = 5, Background = Brush(light ? "#8999B0" : "#ADB9CE"), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new(2), Margin = new(0, 0, 0, 12) });
            var media = new Image { Height = 55, Stretch = Stretch.UniformToFill, Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/拾光照片;component/Assets/ThemeLandscape.png")) };
            body.Children.Add(media); return grid;
        }
        if (key != "system") return new Border { CornerRadius = new(7), ClipToBounds = true, Child = Mini(key == "light") };
        var split = new Grid { Height = 98, ClipToBounds = true }; split.ColumnDefinitions.Add(new()); split.ColumnDefinitions.Add(new()); split.Children.Add(Mini(true)); var dark = Mini(false); Grid.SetColumn(dark, 1); split.Children.Add(dark); return split;
    }
    static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    public void ChooseTheme(string key)
    {
        try { ThemeManager.SetPreference(key); saved.Text = "外观已保存 · 立即生效"; }
        catch (Exception e) { ShowError(e); }
    }
    void ThemeChanged()
    {
        foreach (var (key, button) in themes) { Resource(button, Control.BorderBrushProperty, key == ThemeManager.Preference ? "Accent" : "Line"); indicators[key].Visibility = key == ThemeManager.Preference ? Visibility.Visible : Visibility.Hidden; }
        if (effective != null) effective.Text = ThemeManager.Preference == "system" ? "当前跟随系统：" + ThemeManager.EffectiveLabel : "当前外观：" + ThemeManager.EffectiveLabel + " · 已固定";
    }
    void BuildSources()
    {
        var introduction = new DockPanel { Margin = new(0, 0, 0, 18) }; var add = Dialogs.Button("添加来源目录", Connect, true); add.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(main.CanScan)) { Source = main }); DockPanel.SetDock(add, Dock.Right); introduction.Children.Add(add); introduction.Children.Add(Label($"已连接 {catalog.Roots.Count} 个目录", 18, false, true, new(0, 9, 0, 0))); page.Children.Add(introduction);
        if (catalog.Roots.Count == 0) page.Children.Add(Card(Label("还没有连接来源目录。添加后，只建立索引与预览缓存，媒体仍保存在原来的位置。", 15, true)));
        foreach (var path in catalog.Roots)
        {
            var stack = new StackPanel(); var row = IconText("folder", System.IO.Path.GetFileName(path.TrimEnd('\\')) is string name && name.Length > 0 ? name : path); stack.Children.Add(row); stack.Children.Add(Label(path, 13, true, margin: new(34, 10, 0, 12)));
            bool online = Directory.Exists(path); var status = Label(online ? "目录可访问" : "目录当前不可访问 · 已有引用会保留", 13, true, margin: new(34, 0, 0, 0)); Resource(status, TextBlock.ForegroundProperty, online ? "Success" : "Warning"); stack.Children.Add(status);
            var reconnect = Dialogs.Button("重新连接位置", () => Reconnect(path)); reconnect.FontSize = 13; reconnect.HorizontalAlignment = HorizontalAlignment.Right; reconnect.IsEnabled = main.CanScan; stack.Children.Add(reconnect); page.Children.Add(Card(stack));
        }
        page.Children.Add(ActionRow("增量重新扫描", "发现新媒体、更新文件状态，并重试失败的预览。已有缓存会重复使用。", "重新扫描", () => { Close(); _ = main.RescanSources(); }, true, catalog.Roots.Count > 0));
        page.Children.Add(Label(main.IsBusy ? "当前有索引任务进行中，可在媒体库底部查看进度或停止。" : "来源目录中的文件不受相册分类、标签和外观设置影响。", 13, true));
    }
    async void Connect()
    {
        if (!main.CanScan) return;
        try { var source = await FolderPicker.ChooseAsync(this, catalog.Roots.FirstOrDefault() ?? System.IO.Path.GetPathRoot(AppPaths.Data)!); if (source != null) { Close(); await main.ScanDirectories([source]); } }
        catch (Exception error) { ShowError(error); }
    }
    async void Reconnect(string source)
    {
        try
        {
            var dialog = new OpenFolderDialog { Title = "选择这个来源目录的新位置（内部目录结构应保持一致）" };
            if (dialog.ShowDialog(this) != true) return;
            if (!Dialogs.Confirm(this, "重新连接来源目录？", $"原位置：{source}\n新位置：{dialog.FolderName}\n\n只更新连接，保留相册和标签。文件结构或内容不匹配时会停止。", "重新连接")) return;
            Close(); await main.RelinkSource(source, dialog.FolderName);
        }
        catch (Exception error) { ShowError(error); }
    }
    void BuildLibrary()
    {
        var stats = new UniformGrid { Columns = 3, Margin = new(-5, 0, -5, 18) };
        foreach (var (value, name) in new[] { (catalog.Count().ToString("N0"), "媒体项目"), (catalog.Albums().Count.ToString("N0"), "虚拟相册"), (catalog.Roots.Count.ToString("N0"), "来源目录") })
        {
            var stack = new StackPanel(); stack.Children.Add(Label(value, 26, false, true)); stack.Children.Add(Label(name, 13, true, margin: new(0, 8, 0, 0))); var card = Card(stack, new(18)); card.Margin = new(5, 0, 5, 0); stats.Children.Add(card);
        }
        page.Children.Add(stats);
        if (catalog.Operations().Count > 0)
            page.Children.Add(ActionRow("任务恢复", $"{catalog.Operations().Count:N0} 项未完成操作；核对源位置和目标位置后继续，或取消未开始任务。", "任务恢复", () => { Close(); _ = main.RecoverTasks(); }));
        page.Children.Add(ActionRow("重复照片", "比较副本，选择需要保留的内容。", "检查重复", () => { Close(); _ = main.ReviewDuplicates(); }, true, catalog.Count() > 1));
        page.Children.Add(ActionRow("回收站", $"{catalog.Count("trash"):N0} 个项目，可恢复或永久删除。", catalog.Count("trash") > 0 ? $"查看 {catalog.Count("trash"):N0} 项" : "暂无内容", () => { Close(); _ = main.OpenTrash(); }, false, catalog.Count("trash") > 0));
        page.Children.Add(ActionRow("媒体检查", "预览失败、离线项目与未归类内容。", "查看内容", Maintenance));
        page.Children.Add(ActionRow("媒体库位置", AppPaths.Data, "打开文件夹", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", AppPaths.Data) { UseShellExecute = true })));
    }
    void BuildBackup()
    {
        Section("备份", "选择一个独立磁盘保存；完整迁移需要足够空间容纳原始媒体。");
        page.Children.Add(ActionRow("完整迁移", "包含照片、视频、相册、标签与预览。新电脑可直接打开。", "导出媒体库", () => ExportLibrary(true), true));
        page.Children.Add(ActionRow("相册资料备份", "保存相册、标签和索引。照片、视频需另行备份。", "备份资料", () => ExportLibrary(false), true));
        Section("恢复", "打开已有媒体库会切换到独立窗口，不覆盖当前媒体库。");
        page.Children.Add(ActionRow("打开已有媒体库", "选择媒体库文件夹中的 library.sqlite；支持迁移后更换盘符。", "打开媒体库", OpenLibrary, true));
        page.Children.Add(ActionRow("合并旧版相册", "导入旧版 .albums.json 相册引用。", "选择备份", () => { var dialog = new OpenFileDialog { Filter = "相册引用备份|*.albums.json" }; if (dialog.ShowDialog(this) == true) try { saved.Text = main.ImportAlbumFile(dialog.FileName); } catch (Exception e) { ShowError(e); } }, true));
    }
    async void ExportLibrary(bool includeMedia)
    {
        try
        {
            var dialog = new OpenFolderDialog { Title = "选择备份保存位置（会新建独立媒体库文件夹）" };
            if (dialog.ShowDialog(this) != true) return;
            Close(); await main.ExportLibrary(dialog.FolderName, includeMedia);
        }
        catch (Exception e) { ShowError(e); }
    }
    void OpenLibrary()
    {
        try
        {
            var dialog = new OpenFileDialog { Title = "打开已有媒体库", Filter = "时光媒体库|library.sqlite" };
            if (dialog.ShowDialog(this) != true) return;
            var folder = System.IO.Path.GetDirectoryName(dialog.FileName)!;
            LibraryPackage.Validate(folder);
            main.LaunchLibrary(folder);
        }
        catch (Exception e) { ShowError(e); }
    }
    void Maintenance()
    {
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };
        foreach (var (key, text) in new[] { ("errors", "预览不可用"), ("missing", "离线媒体"), ("unfiled", "未加入任何相册") }) { var item = new MenuItem { Header = text }; item.Click += (_, _) => { Close(); main.BrowseMaintenance(key); }; menu.Items.Add(item); } menu.IsOpen = true;
    }
    Border ActionRow(string title, string text, string action, Action run, bool requireIdle = false, bool available = true)
    {
        var item = new SettingRow(title, text, action, run, action switch { "备份资料" or "导出媒体库" => "copy", "选择备份" => "folder", "重新扫描" => "arrow_clockwise", "查看内容" => "search", _ => "arrow_right" });
        item.ActionButton.IsEnabled = available;
        if (available && requireIdle) item.ActionButton.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(main.CanScan)) { Source = main });
        return item;
    }
    void BuildAbout()
    {
        var intro = new StackPanel(); intro.Children.Add(Label("时光相册", 26, false, true)); intro.Children.Add(Label($"版本 {AppInfo.Version} · Windows 桌面版", 14, true, margin: new(0, 10, 0, 22))); var explanation = Label("按时间、相册和媒体类型浏览回忆。分类只保存引用，让照片与视频继续留在你熟悉的文件夹中。", 15, true); explanation.LineHeight = 26; intro.Children.Add(explanation); page.Children.Add(Card(intro));
        updateRow = new SettingRow("软件更新", "", "检查更新", async () => await main.UseUpdateActionAsync(this), "arrow_clockwise");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(updateRow.Description, System.Windows.Automation.AutomationLiveSetting.Polite);
        page.Children.Add(updateRow);
        autoDownload = new CheckBox { Content = "自动下载更新", Style = (Style)FindResource("ChoiceCheck"), Margin = new(0, 8, 0, 0) };
        autoDownload.Click += async (_, _) => await main.SetAutoDownloadAsync(autoDownload.IsChecked == true);
        page.Children.Add(autoDownload);
        RefreshUpdateRow();
        page.Children.Add(Label("HEIC、视频预览与播放依赖本机解码组件；无法预览不代表原始文件已损坏。", 13, true, margin: new(0, 12, 0, 0)));
    }
    void BuildShortcuts()
    {
        Group("媒体网格",
            ("搜索文件名或标签", "Ctrl + F"),
            ("在照片间移动焦点", "↑ / ↓ / ← / →"),
            ("按屏移动焦点", "Page Up / Page Down"),
            ("打开当前照片或视频（浏览模式）", "双击 / Enter / 空格"));
        Group("多选模式",
            ("勾选或取消当前项目", "Enter / 空格"),
            ("全选已加载的项目", "Ctrl + A"),
            ("追加鼠标框选", "Ctrl + 拖动"),
            ("取消正在进行的框选，或退出多选", "Esc"),
            ("打开移入回收站的确认", "Delete"));
        Group("图片预览",
            ("切换上一张或下一张", "方向键 / Page Up / Page Down"),
            ("播放或暂停幻灯片", "空格"),
            ("缩放当前照片", "鼠标滚轮"),
            ("进入或退出全屏", "F11"),
            ("退出全屏或关闭预览", "Esc"));
        Group("回收站", ("勾选回收站中的所有项目", "Ctrl + A"));

        void Group(string title, params (string Action, string Keys)[] shortcuts)
        {
            Section(title);
            foreach (var (action, keys) in shortcuts)
            {
                var row = new Grid { MinHeight = UiMetrics.Control };
                row.ColumnDefinitions.Add(new());
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var name = Label(action, UiMetrics.Body, margin: new(0, 0, 12, 0));
                name.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(name);
                var badge = new Border
                {
                    Child = Label(keys, UiMetrics.Caption), Padding = new(8, 4, 8, 4),
                    BorderThickness = new(1), CornerRadius = UiMetrics.ControlRadius,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Resource(badge, Border.BackgroundProperty, "Surface");
                Resource(badge, Border.BorderBrushProperty, "Line");
                Grid.SetColumn(badge, 1);
                row.Children.Add(badge);
                var line = new Border { Child = row, Padding = new(0, 4, 0, 4), BorderThickness = new(0, 0, 0, 1) };
                Resource(line, Border.BorderBrushProperty, "Line");
                page.Children.Add(line);
            }
        }
    }
    void RefreshUpdateRow()
    {
        if (updateRow == null) return;
        updateRow.Description.Text = main.UpdateStatus;
        var button = updateRow.ActionButton;
        if (System.Windows.Automation.AutomationProperties.GetName(button) != main.UpdateAction)
        {
            button.Content = Dialogs.LabeledIcon("arrow_clockwise", main.UpdateAction);
            System.Windows.Automation.AutomationProperties.SetName(button, main.UpdateAction);
        }
        button.IsEnabled = main.CanUseUpdateAction;
        if (autoDownload != null) { autoDownload.IsChecked = main.AutoDownloadEnabled; autoDownload.IsEnabled = main.CanSetAutoDownload; }
    }
    void ShowError(Exception e) { saved.Text = "操作未完成：" + e.Message; Resource(saved, TextBlock.ForegroundProperty, "Danger"); }
}
