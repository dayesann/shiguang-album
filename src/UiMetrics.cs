using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Shiguang;

public static class UiMetrics
{
    public const double Body = 14, Caption = 13, Micro = 12, Title = 22, Section = 16;
    public const double Control = 32, Navigation = 36, Segment = 28;
    public static CornerRadius ControlRadius => new(4);
    public static CornerRadius SurfaceRadius => new(8);
}

public sealed class SettingRow : Border
{
    public Button ActionButton { get; }
    public TextBlock Description { get; }
    public SettingRow(string title, string description, string action, Action run, string icon = "arrow_right")
    {
        Padding = new(0, 12, 0, 12); BorderThickness = new(0, 0, 0, 1);
        ThemeManager.Resource(this, BorderBrushProperty, "Line");
        var row = new Grid(); Child = row; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var copy = new StackPanel { Margin = new(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = title, FontSize = UiMetrics.Body, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var sub = new TextBlock { Text = description, FontSize = UiMetrics.Caption, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0) };
        Description = sub;
        ThemeManager.Resource(sub, TextBlock.ForegroundProperty, "Muted"); copy.Children.Add(sub); row.Children.Add(copy);
        ActionButton = Dialogs.Button(action, run); ActionButton.Content = Dialogs.LabeledIcon(icon, action); ActionButton.Margin = new(0); ActionButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(ActionButton, 1); row.Children.Add(ActionButton);
    }
}

public static class NavigationKeys
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(NavigationKeys), new PropertyMetadata(false, Changed));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Panel panel) return;
        KeyboardNavigation.SetTabNavigation(panel, (bool)e.NewValue ? KeyboardNavigationMode.Once : KeyboardNavigationMode.Continue);
        if ((bool)e.NewValue) panel.PreviewKeyDown += KeyDown; else panel.PreviewKeyDown -= KeyDown;
    }
    static void KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Up or Key.Home or Key.End)) return;
        var buttons = UiElements.Descendants<Button>((Panel)sender).Where(b => b.IsVisible && b.IsEnabled).ToList();
        int index = buttons.FindIndex(b => b.IsKeyboardFocusWithin); if (index < 0 || buttons.Count == 0) return;
        int next = e.Key == Key.Home ? 0 : e.Key == Key.End ? buttons.Count - 1 : (index + (e.Key == Key.Down ? 1 : -1) + buttons.Count) % buttons.Count;
        buttons[next].Focus(); e.Handled = true;
    }
}
