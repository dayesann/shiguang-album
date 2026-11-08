using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shiguang;
public static class UiSmoke
{
    static TextWriterTraceListener? listener;
    public static void EnableTrace()
    {
        listener = new(System.IO.Path.Combine(AppPaths.Data, "binding-errors.log")) { TraceOutputOptions = TraceOptions.None }; PresentationTraceSources.DataBindingSource.Listeners.Add(listener); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning; Trace.AutoFlush = true;
    }
    public static async Task Run(MainWindow main, Catalog catalog)
    {
        var checks = new List<string>(); string? failure = null; Task? closingJob = null;
        void Check(bool value, string name) { if (!value) throw new IOException(name); checks.Add(name); }
        async Task Settle(int ms = 500) { await Task.Delay(ms); main.UpdateLayout(); }
        try
        {
            while (main.IsBusy) await Task.Delay(50); await Settle(1000);
            Check(main.Rows.Count > 0, "timeline binds actual indexed media"); Save(main, "01-时间线.png");
            main.SetMode("years"); await Settle(); Check(main.Buckets.Count >= 3, "year overview exposes historical years"); Save(main, "02-年份总览.png");
            ((ComboBox)main.FindName("YearBox")).SelectedItem = "2020"; await Settle(); Check(main.CurrentFilter().Year == "2020" && main.Buckets.All(x => x.Key.StartsWith("2020-")), "year dropdown enters that year's month overview");
            ((ComboBox)main.FindName("MonthBox")).SelectedItem = "10"; await Settle(); Check(main.ShowGallery && main.CurrentFilter().Month == "10", "month dropdown enters that month's media");
            main.SelectDate("2020"); main.SetMode("months"); await Settle(); Check(main.Buckets.Count > 0, "month overview follows selected year"); Save(main, "03-月份总览.png");
            main.SelectDate("2020", "10", "03"); main.SetMode("days"); await Settle(); Check(main.CurrentFilter().Day == "03" && main.Rows.Count > 0, "day filter resolves exact calendar day"); Save(main, "04-指定日期.png");
            main.SelectDate(""); main.SetCategory("panorama"); await Settle(); Check(main.Rows.SelectMany(r => r.Items).All(i => i.Photo.IsPanorama), "panorama view filters image traits"); Save(main, "05-全景图.png");
            main.SetCategory("screenshot"); await Settle(); Check(main.Rows.SelectMany(r => r.Items).All(i => i.Photo.IsScreenshot), "screenshot view works independently"); Save(main, "06-截图.png");
            main.SetCategory("video"); await Settle(); Check(main.Rows.SelectMany(r => r.Items).Any(i => i.Photo.IsVideo), "video view has real video files"); Save(main, "07-视频.png");
            main.OpenAlbum(catalog.Albums().First().Id); await Settle(); Check(main.InAlbum && main.Rows.Count > 0, "virtual mixed album view resolves"); Save(main, "08-相册.png");
            var p = main.Rows.SelectMany(r => r.Items).First().Photo; p.Selected = true; await Settle(); Check(main.HasSelection, "selection enables reference-only actions");
            ((Button)main.FindName("DetailsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(); Check(main.ShowDetails && main.DetailWidth.Value == 285, "detail panel can open without replacing gallery"); Save(main, "11-详情栏.png");
            ((Button)main.FindName("DetailsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            main.Width = 1180; main.Height = 820; await Settle(); Save(main, "09-紧凑窗口.png");
            var gallery = (ListBox)main.FindName("Gallery"); Check(main.Rows.All(r => r.Items.Sum(i => i.Width + 8) <= gallery.ActualWidth + 2), "gallery reflows at compact width");
            main.Width = 1536; main.Height = 1024; main.SetCategory("all"); await Settle();
            var still = catalog.AllActive().First(x => !x.IsVideo && x.Error.Length == 0); var viewer = new PhotoViewer([still], 0) { Owner = main }; viewer.Show();
            for (int i = 0; i < 60 && !Descendants<Image>(viewer).Any(x => x.Source != null); i++) await Settle(150);
            Check(Descendants<Image>(viewer).Any(x => x.Source != null), "large image preview uses isolated cache generation"); Save(viewer, "10-大图预览.png"); viewer.Close();
            listener?.Flush(); Check(!File.Exists(System.IO.Path.Combine(AppPaths.Data, "binding-errors.log")) || new FileInfo(System.IO.Path.Combine(AppPaths.Data, "binding-errors.log")).Length == 0, "no WPF data binding errors");
            closingJob = main.ScanDirectories(catalog.Roots); Check(main.IsBusy, "close-during-scan check begins with an active index job");
        }
        catch (Exception e) { failure = e.ToString(); }
        File.WriteAllText(System.IO.Path.Combine(AppPaths.Data, "ui-smoke-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true })); listener?.Flush(); main.ExitCode = failure == null ? 0 : 1;
        if (closingJob != null) { main.Close(); await closingJob; } else Application.Current.Shutdown(main.ExitCode);
    }
    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var x in Descendants<T>(child)) yield return x; }
    }
    public static void Save(Window window, string file)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32); bmp.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp)); using var output = File.Create(System.IO.Path.Combine(AppPaths.Data, file)); encoder.Save(output);
    }
}
