using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Shiguang;

public static class ThemeManager
{
    public static string Preference { get; private set; } = "system";
    public static bool IsLight { get; private set; }
    public static bool IsHighContrast { get; private set; }
    public static string EffectiveLabel => IsHighContrast ? "高对比度" : IsLight ? "浅色" : "深色";
    public static event Action? Changed;
    static DispatcherTimer? monitor;
    internal static Func<bool> SystemLight = ReadSystemLight;
    internal static Func<bool> HighContrastMode = () => SystemParameters.HighContrast;
    static string SettingsPath => Path.Combine(AppPaths.Data, "settings.json");
    sealed record Preferences(string Theme);

    public static void Initialize()
    {
        Preference = ReadPreference(); Apply();
        monitor = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        monitor.Tick += (_, _) => RefreshSystem();
        monitor.Start(); Application.Current.Exit += (_, _) => monitor.Stop();
    }
    public static string ReadPreference()
    {
        try { var value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath))?.Theme; return value is "light" or "dark" or "system" ? value : "system"; }
        catch { return "system"; }
    }
    public static void SetPreference(string value)
    {
        if (value is not ("light" or "dark" or "system")) throw new ArgumentException("未知外观选项");
        // Persist before changing the UI; a failed write never claims the preference was saved.
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Preferences(value), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, true); Preference = value; Apply();
    }
    internal static void RefreshSystem()
    {
        bool highContrast = HighContrastMode();
        if (highContrast != IsHighContrast || Preference == "system" && IsLight != SystemLight()) Apply();
    }
    internal static void Reapply() => Apply();
    public static void Stop() => monitor?.Stop();
    static bool ReadSystemLight()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is not int value || value != 0; }
        catch { return true; }
    }
    static void Apply()
    {
        IsLight = Preference == "light" || Preference == "system" && SystemLight();
        IsHighContrast = HighContrastMode();
        var colors = IsHighContrast ? new Dictionary<string, string>
        {
            ["Primary"] = Hex(SystemColors.HighlightColor), ["OnAccent"] = Hex(SystemColors.HighlightTextColor), ["PrimaryHover"] = Hex(SystemColors.HotTrackColor),
            ["PrimaryPressed"] = Hex(SystemColors.ControlDarkColor), ["Bg"] = Hex(SystemColors.WindowColor), ["Side"] = Hex(SystemColors.ControlColor),
            ["Surface"] = Hex(SystemColors.ControlColor), ["Line"] = Hex(SystemColors.ControlDarkColor), ["Text"] = Hex(SystemColors.WindowTextColor),
            ["SelectedText"] = Hex(SystemColors.HighlightTextColor),
            ["Muted"] = Hex(SystemColors.GrayTextColor), ["Accent"] = Hex(SystemColors.HighlightColor), ["Hover"] = Hex(SystemColors.ControlLightColor),
            ["Selected"] = Hex(SystemColors.HighlightColor), ["Input"] = Hex(SystemColors.WindowColor), ["Placeholder"] = Hex(SystemColors.ControlColor),
            ["ScrollThumb"] = Hex(SystemColors.ScrollBarColor), ["Warning"] = Hex(SystemColors.WindowTextColor), ["Success"] = Hex(SystemColors.WindowTextColor),
            ["Danger"] = Hex(SystemColors.WindowTextColor), ["DangerFill"] = Hex(SystemColors.HighlightColor),
            ["DangerFillHover"] = Hex(SystemColors.HotTrackColor), ["DangerFillPressed"] = Hex(SystemColors.ControlDarkColor),
            ["OnDanger"] = Hex(SystemColors.HighlightTextColor),
            ["MediaScrim"] = "#FF000000", ["Favorite"] = Hex(SystemColors.WindowTextColor), ["SelectionHover"] = Hex(SystemColors.ControlLightColor)
        } : IsLight ? new Dictionary<string, string>
        {
            ["Primary"]="#176DDB", ["OnAccent"]="#FFFFFF", ["PrimaryHover"]="#145DB8", ["PrimaryPressed"]="#0F4C99",
            ["Bg"]="#F5F7FA", ["Side"]="#EDF1F6", ["Surface"]="#FFFFFF", ["Line"]="#D6DEE9",
            ["Text"]="#202938", ["Muted"]="#5D6C80", ["SelectedText"]="#202938", ["Accent"]="#176DDB", ["Hover"]="#E3EBF6",
            ["Selected"]="#DCEAFF", ["Input"]="#FFFFFF", ["Placeholder"]="#E7ECF3", ["ScrollThumb"]="#A9B6C8",
            ["Warning"]="#945114", ["Success"]="#18754A", ["Danger"]="#B42318", ["DangerFill"]="#C42B1C",
            ["DangerFillHover"]="#A4262C", ["DangerFillPressed"]="#7A1C20", ["OnDanger"]="#FFFFFF",
            ["MediaScrim"]="#D3191D24", ["Favorite"]="#B77900", ["SelectionHover"]="#EDF3FC"
        } : new Dictionary<string, string>
        {
            ["Primary"]="#176DDB", ["OnAccent"]="#FFFFFF", ["PrimaryHover"]="#145DB8", ["PrimaryPressed"]="#0F4C99",
            ["Bg"]="#20242C", ["Side"]="#252A34", ["Surface"]="#2C323D", ["Line"]="#424A57",
            ["Text"]="#F0F3F9", ["Muted"]="#AEB9CA", ["SelectedText"]="#F0F3F9", ["Accent"]="#2786F5", ["Hover"]="#354257",
            ["Selected"]="#244D7F", ["Input"]="#282E38", ["Placeholder"]="#2B313C", ["ScrollThumb"]="#566174",
            ["Warning"]="#FFBE8B", ["Success"]="#7DD8AC", ["Danger"]="#FF99A4", ["DangerFill"]="#C42B1C",
            ["DangerFillHover"]="#A4262C", ["DangerFillPressed"]="#7A1C20", ["OnDanger"]="#FFFFFF",
            ["MediaScrim"]="#D3191D24", ["Favorite"]="#FFCF6B", ["SelectionHover"]="#303A49"
        };
        colors["DialogScrim"] = IsHighContrast ? "#00000000" : IsLight ? "#50000000" : "#70000000";
        foreach (var (name, color) in new Dictionary<string, string>
        {
            ["ViewerBackground"] = "#080A0D", ["ViewerText"] = "#FFFFFF", ["ViewerSurface"] = "#252E3C",
            ["ViewerAccent"] = "#2B70D6", ["ViewerHover"] = "#354C70", ["ViewerTrack"] = "#394354",
            ["ViewerLine"] = "#4B5C73", ["ViewerDisabled"] = "#637084", ["ViewerFocus"] = "#6EAAFF",
            ["ViewerLink"] = "#A6CAFF", ["ViewerHighlight"] = "#A9CFFF", ["ViewerSecondary"] = "#BDC7D6",
            ["ViewerMuted"] = "#BFCBDB", ["ViewerDetail"] = "#C1CBDA", ["ViewerToast"] = "#D91B2431",
            ["ViewerTop"] = "#DA121720", ["ViewerError"] = "#EF171E2A", ["ViewerControls"] = "#F019202D",
            ["ViewerBright"] = "#F4F6FA", ["ViewerOnAccent"] = "#F4F7FF"
        }) colors[name] = IsHighContrast ? Hex(name is "ViewerBackground" or "ViewerSurface" or "ViewerToast" or "ViewerTop" or "ViewerError" or "ViewerControls" ? SystemColors.WindowColor : SystemColors.WindowTextColor) : color;
        foreach (var (key, value) in colors) Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        foreach (Window window in Application.Current.Windows) ApplyTitleBar(window);
        Changed?.Invoke();
    }
    static string Hex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    public static void Attach(Window window)
    {
        window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
        window.SourceInitialized += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            window.MinWidth = Math.Min(window.MinWidth, area.Width); window.MinHeight = Math.Min(window.MinHeight, area.Height);
            if (!double.IsNaN(window.Width)) window.Width = Math.Min(window.Width, area.Width);
            if (!double.IsNaN(window.Height)) window.Height = Math.Min(window.Height, area.Height);
            ApplyTitleBar(window);
        };
    }
    static void ApplyTitleBar(Window window)
    {
        try { int dark = IsLight ? 0 : 1; var handle = new WindowInteropHelper(window).Handle; if (handle != 0) DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)); } catch { }
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    public static void Resource(FrameworkElement control, DependencyProperty property, string key) => control.SetResourceReference(property, key);
}
