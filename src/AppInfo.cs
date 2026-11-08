namespace Shiguang;

public static class AppInfo
{
    public static string Version => typeof(AppInfo).Assembly.GetName().Version!.ToString(3);
}
