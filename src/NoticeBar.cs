using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace Shiguang;

public sealed class NoticeBar : Border
{
    readonly TextBlock message = new() { FontSize = UiMetrics.Body, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly FluentIcon icon = new() { Kind = "checkmark_circle", Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0) };
    readonly Button action;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(7) };
    bool persistent;
    Action? callbackAction;
    public string Message => message.Text;
    public bool ActionEnabled { get => action.IsEnabled; set => action.IsEnabled = value; }
    public NoticeBar()
    {
        Visibility = Visibility.Collapsed; MaxWidth = 480; Padding = new(12, 8, 8, 8); CornerRadius = UiMetrics.SurfaceRadius; BorderThickness = new(1);
        ThemeManager.Resource(this, BackgroundProperty, "Surface"); ThemeManager.Resource(this, BorderBrushProperty, "Line");
        ThemeManager.Resource(icon, Control.ForegroundProperty, "Success");
        var row = new Grid(); Child = row;
        foreach (bool auto in new[] { true, false, true, true }) row.ColumnDefinitions.Add(new() { Width = auto ? GridLength.Auto : new(1, GridUnitType.Star) });
        row.Children.Add(icon); Grid.SetColumn(message, 1); row.Children.Add(message);
        action = Dialogs.Button("撤销", () => { var callback = callbackAction; Hide(); callback?.Invoke(); }); action.Margin = new(8, 0, 0, 0); Grid.SetColumn(action, 2); row.Children.Add(action);
        var close = Dialogs.Button("关闭提示", Hide); close.Style = (Style)Application.Current.FindResource("IconButton"); close.Content = new FluentIcon { Kind = "dismiss", Width = 16, Height = 16 }; close.ToolTip = "关闭提示"; close.Margin = new(4, 0, 0, 0); Grid.SetColumn(close, 3); row.Children.Add(close);
        System.Windows.Automation.AutomationProperties.SetName(this, "操作结果");
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        timer.Tick += (_, _) => { if (!IsMouseOver && !IsKeyboardFocusWithin) Hide(); };
        MouseEnter += (_, _) => timer.Stop(); MouseLeave += (_, _) => Restart();
        IsKeyboardFocusWithinChanged += (_, _) => { if (IsKeyboardFocusWithin) timer.Stop(); else Restart(); };
        Unloaded += (_, _) => timer.Stop();
    }
    void Restart() { if (!persistent && IsVisible && !IsMouseOver && !IsKeyboardFocusWithin) timer.Start(); }
    public void Show(string text, string? actionText = null, Action? run = null, bool error = false)
    {
        timer.Stop(); message.Text = text; callbackAction = run; persistent = error;
        action.Content = actionText ?? "撤销"; System.Windows.Automation.AutomationProperties.SetName(action, actionText ?? "撤销"); action.Visibility = run == null ? Visibility.Collapsed : Visibility.Visible;
        icon.Kind = error ? "info" : "checkmark_circle"; ThemeManager.Resource(icon, Control.ForegroundProperty, error ? "Danger" : "Success");
        Visibility = Visibility.Visible; Restart();
        if (UIElementAutomationPeer.CreatePeerForElement(this) is AutomationPeer peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    public void Hide() { timer.Stop(); Visibility = Visibility.Collapsed; callbackAction = null; }
    protected override AutomationPeer OnCreateAutomationPeer() => new NoticePeer(this);
    sealed class NoticePeer(NoticeBar owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.StatusBar;
        protected override string GetNameCore() => owner.Message;
    }
}
