using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shiguang;

public sealed class ComponentGalleryWindow : Window
{
    public ToolBar SampleToolbar { get; }
    public Button SamplePrimary { get; }
    public NoticeBar Notice { get; } = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(24) };
    public ComponentGalleryWindow()
    {
        Title = "控件样板 · 时光相册"; Width = 1000; Height = 800; MinWidth = 760; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ThemeManager.Attach(this);
        var root = new Grid(); ThemeManager.Resource(root, Panel.BackgroundProperty, "Bg"); Content = root;
        var content = new StackPanel { Margin = new(28, 24, 28, 28) };
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        content.Children.Add(Text("紧凑控件样板", UiMetrics.Title, bold: true));
        content.Children.Add(Text("统一尺寸、状态与反馈。此页演示不会修改照片。", UiMetrics.Caption, true, new(0, 6, 0, 16)));
        var themes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 12) };
        foreach (var (key, label) in new[] { ("light", "浅色"), ("dark", "深色"), ("system", "跟随系统") }) themes.Children.Add(Dialogs.Button(label, () => ThemeManager.SetPreference(key)));
        content.Children.Add(themes);
        Group("工具栏 · 空间不足时自动收纳");
        SampleToolbar = new ToolBar { Style = (Style)FindResource("CompactToolBar"), Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (icon, label) in new[] { ("folder_add", "添加来源目录"), ("info", "显示详情"), ("arrow_clockwise", "重新扫描"), ("copy", "重复照片"), ("settings", "设置") })
        {
            var button = Dialogs.Button(label, () => Notice.Show("已点击“" + label + "”")); button.Style = (Style)FindResource("ToolbarActionButton"); button.ToolTip = label;
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(new FluentIcon { Kind = icon, Width = 18, Height = 18 }); row.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("ToolbarCaption") }); button.Content = row; SampleToolbar.Items.Add(button);
        }
        content.Children.Add(SampleToolbar);
        var resizeToolbar = Dialogs.Button("收窄 / 展开工具栏", () => SampleToolbar.Width = SampleToolbar.Width > 100 ? 80 : 420); resizeToolbar.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(resizeToolbar);
        Group("按钮 · 常规、主要、禁用与危险操作");
        var buttons = new WrapPanel();
        SamplePrimary = Dialogs.Button("保存更改", () => Notice.Show("更改已保存"), true); buttons.Children.Add(SamplePrimary);
        var secondary = Dialogs.Button("取消", () => Notice.Show("已取消")); secondary.Style = (Style)FindResource("SecondaryButton"); buttons.Children.Add(secondary);
        buttons.Children.Add(Dialogs.Button("更多选项", () => Notice.Show("普通操作采用轻量样式")));
        var disabled = Dialogs.Button("等待选择", () => { }); disabled.IsEnabled = false; disabled.ToolTip = "选择内容后可用"; buttons.Children.Add(disabled);
        var danger = Dialogs.Button("永久删除", () => new ConfirmationWindow("永久删除这些项目？", "这是确认框样例，没有连接任何文件。", "永久删除", true) { Owner = this }.ShowDialog()); danger.Style = (Style)FindResource("DangerButton"); buttons.Children.Add(danger); content.Children.Add(buttons);
        Group("设置行 · 操作与说明属于同一行");
        content.Children.Add(new SettingRow("来源目录", "连接照片与视频所在的位置。", "添加目录", () => Notice.Show("来源目录操作样例"), "folder_add"));
        content.Children.Add(new SettingRow("完整迁移", "包含原始媒体、相册与标签，可在新电脑打开。", "导出媒体库", () => Notice.Show("媒体库导出操作样例"), "copy"));
        Group("输入与错误 · 反馈紧贴控件");
        var input = new Grid(); input.ColumnDefinitions.Add(new() { Width = new(80) }); input.ColumnDefinitions.Add(new() { Width = new(350) });
        var inputLabel = Text("相册名称", UiMetrics.Body); inputLabel.VerticalAlignment = VerticalAlignment.Center; input.Children.Add(inputLabel);
        var entry = new TextBox { Text = "家庭旅行 · 2026", Height = UiMetrics.Control }; System.Windows.Automation.AutomationProperties.SetName(entry, "相册名称"); Grid.SetColumn(entry, 1); input.Children.Add(entry); content.Children.Add(input);
        var validation = Text("示例：请输入相册名称。", UiMetrics.Caption, margin: new(80, 4, 0, 0)); ThemeManager.Resource(validation, TextBlock.ForegroundProperty, "Danger"); content.Children.Add(validation);
        Group("完成与恢复 · 不打断当前浏览");
        var feedback = new WrapPanel(); feedback.Children.Add(Dialogs.Button("显示完成提示", () => Notice.Show("已加入相册")));
        feedback.Children.Add(Dialogs.Button("显示撤销提示", () => Notice.Show("已移入回收站 3 个项目", "撤销", () => Notice.Show("已撤销示例操作"))));
        feedback.Children.Add(Dialogs.Button("显示错误提示", () => Notice.Show("目录当前不可访问，请检查磁盘连接。", error: true))); content.Children.Add(feedback);
        root.Children.Add(Notice);
        void Group(string title) => content.Children.Add(Text(title, UiMetrics.Section, bold: true, margin: new(0, 18, 0, 10)));
    }
    static TextBlock Text(string value, double size, bool muted = false, Thickness? margin = null, bool bold = false)
    {
        var text = new TextBlock { Text = value, FontSize = size, Margin = margin ?? new(), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        ThemeManager.Resource(text, TextBlock.ForegroundProperty, muted ? "Muted" : "Text"); return text;
    }
}
