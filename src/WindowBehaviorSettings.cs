using System.Text.Json;

namespace Shiguang;

public static class WindowBehaviorSettings
{
    public sealed record Preferences(bool CloseToTray = true);
    static string SettingsPath => Path.Combine(AppPaths.Data, "window-preferences.json");
    public static bool CloseToTray
    {
        get
        {
            try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath))?.CloseToTray ?? true; }
            catch { return true; }
        }
    }
    public static void SetCloseToTray(bool value)
    {
        var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preferences(value), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, SettingsPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
