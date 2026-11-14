using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Shiguang;

internal static class SlideshowTests
{
    public static async Task Run(MainWindow main, Catalog catalog)
    {
        var checks = new List<string>(); string? failure = null; PhotoViewer? viewer = null;
        void Check(bool value, string name) { if (!value) throw new IOException(name); checks.Add(name); }
        async Task Wait(Func<bool> condition, string name)
        {
            for (int i = 0; i < 150 && !condition(); i++) await Task.Delay(100);
            Check(condition(), name);
        }
        Button Button(string name) => UiElements.Descendants<Button>(viewer!).First(x => AutomationProperties.GetName(x) == name);
        void Click(string name) => Button(name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        try
        {
            while (main.IsBusy) await Task.Delay(100);
            string root = Path.Combine(AppPaths.Data, "slideshow-fixtures"); Directory.CreateDirectory(root);
            var photos = new List<Photo>();
            for (int i = 0; i < 3; i++)
            {
                string path = Path.Combine(root, $"照片_{i}.png"); SelfTest.CreateImage(path, i + 1, false);
                var p = MediaIndexer.Skeleton(path, root); catalog.Upsert(p); photos.Add(p);
            }
            var originals = photos.ToDictionary(p => p.Path, p => LibraryService.HashFile(p.Path));
            new SlideshowPreferences(3, true, true, .2).Save();
            viewer = new PhotoViewer(photos, 0) { Owner = main }; viewer.Show();
            await Wait(() => UiElements.Descendants<Image>(viewer).Any(x => x.Source != null), "photo renders before slideshow starts");
            Check(!viewer.IsPlaying && !viewer.SlideTimerRunning && !viewer.MusicOpened, "normal photo open stays quiet and paused");
            Click("自动播放"); await Wait(() => viewer.MusicOpened, "built-in original music opens through Windows media engine");
            await Task.Delay(500); Check(viewer.BackgroundMusic.Position > TimeSpan.Zero, "background music advances during playback");
            viewer.BackgroundMusic.Position = viewer.BackgroundMusic.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(200);
            await Wait(() => viewer.BackgroundMusic.Position < TimeSpan.FromSeconds(2), "music loops at the end of its track");
            await Wait(() => viewer.SlideIndex == 1, "real dispatcher timer advances to the next photo");
            Click("暂停播放"); var position = viewer.BackgroundMusic.Position; int pausedIndex = viewer.SlideIndex;
            await Task.Delay(3300); Check(viewer.SlideIndex == pausedIndex && !viewer.SlideTimerRunning, "pause prevents further automatic advances");
            Check((viewer.BackgroundMusic.Position - position).Duration() < TimeSpan.FromMilliseconds(350), "pause also pauses the music");
            Click("自动播放"); Click("关闭背景音乐");
            Check(viewer.IsPlaying && SlideshowPreferences.Read().Music == false, "music toggle leaves slideshow running and persists");
            Click("暂停播放"); Click("开启背景音乐");
            var intervals = UiElements.Descendants<ComboBox>(viewer).Single(); intervals.SelectedItem = 8;
            Check(SlideshowPreferences.Read().Seconds == 8, "interval preference persists");
            var loop = UiElements.Descendants<CheckBox>(viewer).Single(); loop.IsChecked = false;
            Check(!SlideshowPreferences.Read().Loop, "non-looping preference persists");
            Click("下一张"); await Wait(() => viewer.SlideIndex == 2 && UiElements.Descendants<Image>(viewer).Any(x => x.Source != null), "manual navigation reaches last photo");
            Click("自动播放"); viewer.AdvanceSlide(); Check(!viewer.IsPlaying && viewer.SlideIndex == 2, "non-looping playback stops on last photo");
            loop.IsChecked = true; Click("自动播放"); viewer.AdvanceSlide();
            await Wait(() => viewer.SlideIndex == 0 && viewer.SlideTimerRunning, "loop wraps and rearms timer only after decoding");
            Click("暂停播放");
            foreach (var theme in new[] { "light", "dark", "system" })
            {
                ThemeManager.SetPreference(theme); viewer.Width = 1250; viewer.Height = 850; await Task.Delay(250); UiSmoke.Save(viewer, "slideshow-" + theme + ".png");
                viewer.Width = 700; viewer.Height = 500; await Task.Delay(250); UiSmoke.Save(viewer, "slideshow-" + theme + "-minimum.png");
                Check(UiElements.Descendants<Button>(viewer).Where(x => x.IsVisible).All(x => { var p = x.TransformToAncestor(viewer).Transform(new Point()); return p.X >= 0 && p.X + x.ActualWidth <= viewer.ActualWidth; }), theme + " controls stay within minimum window");
                Check(((System.Windows.Media.SolidColorBrush)loop.Foreground).Color == ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Text")).Color, theme + " loop label uses readable theme text");
            }
            viewer.SetMusicPath(Path.Combine(root, "离线音乐.mp3")); Click("自动播放");
            Check(viewer.IsPlaying && UiElements.Descendants<TextBlock>(viewer).Any(x => x.Text.Contains("音乐文件不存在")), "missing music reports recovery while photos keep playing");
            UiSmoke.Save(viewer, "slideshow-missing-music.png");
            viewer.SetMusicPath(""); await Wait(() => viewer.MusicOpened, "built-in track recovers missing custom music");
            string corrupt = Path.Combine(root, "损坏音乐.wav"); File.WriteAllText(corrupt, "not an audio file"); var audioHash = LibraryService.HashFile(corrupt);
            viewer.SetMusicPath(corrupt);
            await Wait(() => UiElements.Descendants<TextBlock>(viewer).Any(x => x.Text.Contains("背景音乐无法播放")), "undecodable audio has a recoverable error");
            Check(viewer.IsPlaying && LibraryService.HashFile(corrupt) == audioHash, "audio decode failure keeps photos running and music file unchanged");
            viewer.SetMusicPath(""); await Wait(() => viewer.MusicOpened, "built-in music recovers an audio decoding failure");
            viewer.Close(); Check(viewer.PlaybackClosed && !viewer.IsPlaying && !viewer.SlideTimerRunning && !viewer.MusicOpened, "closing releases timer and music");
            viewer = new PhotoViewer([photos[0]], 0) { Owner = main, AutoPlay = true }; viewer.Show(); await Task.Delay(300);
            Check(!viewer.IsPlaying && !Button("自动播放").IsEnabled, "single image disables autoplay even with autoplay requested"); viewer.Close();
            new SlideshowPreferences(5, true, false).Save();
            viewer = new PhotoViewer(photos, 0) { Owner = main, AutoPlay = true }; viewer.Show();
            await Wait(() => viewer.SlideTimerRunning, "one-click slideshow starts automatically once image is ready"); viewer.Close();
            viewer = new PhotoViewer(_ => null, 2, 0) { Owner = main, AutoPlay = true }; viewer.Show(); await Task.Delay(400);
            Check(!viewer.SlideTimerRunning && !viewer.IsPlaying, "unavailable photo stops autoplay with no runaway timer"); viewer.Close();
            Check(originals.All(p => File.Exists(p.Key) && LibraryService.HashFile(p.Key) == p.Value), "all photo source paths and bytes are unchanged");
        }
        catch (Exception e) { failure = e.ToString(); }
        finally { if (viewer is { IsVisible: true }) viewer.Close(); }
        File.WriteAllText(Path.Combine(AppPaths.Data, "slideshow-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Count = checks.Count, Checks = checks, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        main.ExitCode = failure == null ? 0 : 1; main.Close();
    }
}
