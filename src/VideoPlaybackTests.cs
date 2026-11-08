using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Shiguang;

internal static class VideoPlaybackTests
{
    public static async Task Run(VideoPlayerWindow window)
    {
        var checks = new List<string>(); string? failure = null;
        string original = window.CurrentPath; string before = LibraryService.HashFile(original);
        var fixtures = new Dictionary<string, string>();
        void Check(bool value, string name) { if (!value) throw new IOException(name); checks.Add(name); }
        async Task Wait(Func<bool> predicate, string name, int milliseconds = 10000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!predicate() && DateTime.UtcNow < until) await Task.Delay(100);
            Check(predicate(), name);
        }
        void SendKey(Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        try
        {
            await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Playing && window.Player.Position.TotalSeconds > .5, "real video opens and advances");
            Check(UiElements.Descendants<Button>(window.ControlBar).Where(x => x != window.SpeedButton).All(x => x.Content is FluentIcon), "transport commands use actual Fluent icons rather than text glyphs");
            Check(UiElements.Descendants<Button>(window.ControlBar).All(x => AutomationProperties.GetName(x).Length > 0), "all transport buttons expose accessible names");
            var playFrame = (Border)window.PlayButton.Template.FindName("Frame", window.PlayButton);
            Check(Math.Abs(window.PlayButton.ActualWidth - window.PlayButton.ActualHeight) < 1 && playFrame.CornerRadius.TopLeft >= window.PlayButton.ActualHeight / 2, "primary playback action is circular rather than a large square");
            window.Toggle(); double pausedAt = window.Player.Position.TotalSeconds; await Task.Delay(500);
            Check(window.State == VideoPlayerWindow.PlaybackState.Paused && Math.Abs(window.Player.Position.TotalSeconds - pausedAt) < .18, "pause freezes the actual playback clock");
            window.ForceIdleForTest(); Check(window.ControlBar.Visibility == Visibility.Visible, "paused controls remain visible after idle timeout");
            window.SeekTo(2); await Task.Delay(400);
            Check(window.State == VideoPlayerWindow.PlaybackState.Paused && Math.Abs(window.Player.Position.TotalSeconds - 2) < .25, "seeking while paused preserves paused state and changes actual position");
            window.SetSpeed(1.5); Check(window.PlaybackSpeed == 1.5 && window.Player.SpeedRatio == 1.5, "speed selection applies to the real media element");
            window.SetVolume(.35); Check(Math.Abs(window.Player.Volume - .35) < .001, "volume control changes actual output volume");
            if (window.Player.HasAudio)
            {
                window.ToggleMute(); Check(window.Player.IsMuted, "mute affects actual audio output");
                window.ToggleMute(); Check(!window.Player.IsMuted && Math.Abs(window.Player.Volume - .35) < .001, "unmute restores the previous volume");
            }
            else Check(!window.MuteButton.IsEnabled && !window.VolumeBar.IsEnabled, "silent video clearly disables audio controls");
            window.Width = 700; window.Height = 480; window.UpdateLayout(); await Task.Delay(250);
            var transport = UiElements.Descendants<Button>(window.ControlBar).ToList();
            Check(transport.All(x => x.TranslatePoint(new Point(0, 0), window.ControlBar).X >= 0 && x.TranslatePoint(new Point(x.ActualWidth, 0), window.ControlBar).X <= window.ControlBar.ActualWidth + 1), "all transport buttons fit the minimum window width");
            Check(window.ElapsedLabel.ActualWidth >= 50 && window.DurationLabel.ActualWidth >= 35 && window.SeekBar.ActualWidth > 150, "time and seeking remain readable at minimum width");
            UiSmoke.Save(window, "02-播放器-窄窗口.png"); window.Width = 1180; window.Height = 780; await Task.Delay(200);
            window.ToggleFullscreen(); Check(window.IsFullScreen && window.WindowStyle == WindowStyle.None, "fullscreen button removes native chrome");
            SendKey(Key.Escape); Check(!window.IsFullScreen && window.IsLoaded && window.WindowStyle == WindowStyle.SingleBorderWindow, "Escape exits fullscreen without closing the player");
            window.PlayerRoot.Focus(); SendKey(Key.Space); double resumeAt = window.Player.Position.TotalSeconds;
            await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Playing && window.Player.Position.TotalSeconds > resumeAt + .3, "Space resumes actual playback");
            window.Toggle(); window.PlayButton.Focus(); SendKey(Key.Space);
            Check(window.State == VideoPlayerWindow.PlaybackState.Paused, "window shortcut does not double-handle Space on a focused button");
            window.PlayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.State == VideoPlayerWindow.PlaybackState.Playing, "focused play button performs exactly one state transition");
            double buttonAt = window.Player.Position.TotalSeconds;
            await Wait(() => window.Player.Position.TotalSeconds > buttonAt + .2, "focused play button resumes the native playback clock");
            window.SetSpeed(2); window.SeekTo(Math.Max(0, window.SeekBar.Maximum - .4));
            await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Ended, "reaching the end displays replay state");
            Check(window.ElapsedLabel.Text == window.DurationLabel.Text, "elapsed time equals total duration at the end");
            window.SeekTo(2);
            await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Paused && Math.Abs(window.Player.Position.TotalSeconds - 2) < .25, "seeking back after playback ended preserves the chosen position");
            await Task.Delay(600);
            Check(window.State == VideoPlayerWindow.PlaybackState.Paused && Math.Abs(window.Player.Position.TotalSeconds - 2) < .25, "post-end seek remains at the selected paused position after native events settle");
            window.SeekTo(window.SeekBar.Maximum); window.Toggle();
            await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Ended, "seeking to the end can finish normally");
            window.Toggle(); await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Playing && window.Player.Position.TotalSeconds < 2, "replay restarts the actual clip");
            window.Toggle(); window.SetSpeed(1); UiSmoke.Save(window, "01-播放器-暂停.png");
            Check(VideoPlayerWindow.FormatTime(TimeSpan.FromHours(25) + TimeSpan.FromSeconds(3)) == "25:00:03", "long durations do not wrap after 24 hours");

            string root = Path.Combine(AppPaths.Data, "video-fixtures"); Directory.CreateDirectory(root);
            using (var catalog = new Catalog())
            {
                var items = new List<Photo>();
                for (int i = 0; i < 3; i++)
                {
                    string path = Path.Combine(root, $"测试视频-{i + 1}.mp4"); File.Copy(original, path, true); fixtures[path] = LibraryService.HashFile(path);
                    var p = MediaIndexer.Skeleton(path, root); p.MediaKind = "video"; p.Taken = $"2020-01-0{i + 1} 12:00:00"; items.Add(p);
                }
                catalog.UpsertMany(items); catalog.AddRoot(root); string album = catalog.CreateAlbum("播放器范围检查"); catalog.AddToAlbum(album, items.Take(2));
                var filter = new MediaFilter(View: "album", Album: album, Year: "2020");
                window.OpenRequest(new(items[1].Path, filter, items[1].Id, "播放器范围检查"));
                await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Playing, "album-scoped video opens in the same player");
                Check(window.QueueCount == 2 && window.QueueIndex == 0 && !window.PreviousButton.IsEnabled && window.NextButton.IsEnabled, "queue follows current album and chronological order");
                window.MoveVideo(1); await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Playing, "next command loads the adjacent clip");
                Check(window.CurrentPath == items[0].Path && window.QueueIndex == 1 && !window.NextButton.IsEnabled, "next stays inside the album and stops at its boundary");
                window.MoveVideo(-1); await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Playing, "previous command restores the preceding clip");
                Check(window.CurrentPath == items[1].Path, "previous uses the same filtered queue");
                if (window.PipeName != null)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using var client = await VideoPipeClient.ConnectAsync(window.PipeName, timeout.Token);
                    using var reader = new StreamReader(client, leaveOpen: true); using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new VideoRequest(items[0].Path, filter, items[0].Id, "IPC 范围检查")));
                    Check(await reader.ReadLineAsync(timeout.Token) == "ok", "live player acknowledges a subsequent open through its local pipe");
                    await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Playing && window.CurrentPath == items[0].Path, "subsequent open replaces the active media in the existing window");
                }
            }
            window.OpenRequest(new(Path.Combine(root, "不存在.mp4")));
            Check(window.State == VideoPlayerWindow.PlaybackState.Failed && window.StatePanel.Visibility == Visibility.Visible && !window.PlayButton.IsEnabled && !window.SeekBar.IsEnabled, "offline source shows a recoverable error instead of a misleading black player");
            Check(window.RetryButton.IsEnabled, "offline source keeps retry available for a reconnected drive");
            UiSmoke.Save(window, "03-播放器-来源离线.png");
            string reconnected = window.CurrentPath; File.Copy(original, reconnected); fixtures[reconnected] = LibraryService.HashFile(reconnected);
            window.RetryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Playing, "retry recovers in place when the missing source reappears");
            string bad = Path.Combine(root, "损坏.mp4"); File.WriteAllText(bad, "not a valid video");
            window.OpenRequest(new(bad)); await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Failed, "corrupt media fails within the player without terminating the library");
            window.OpenRequest(new(original)); await Wait(() => window.State == VideoPlayerWindow.PlaybackState.Playing, "player recovers by opening valid media after a failure");
            window.Toggle(); window.SetVolume(.35); new PlaybackPreferences(window.Player.Volume, window.Player.IsMuted).Save();
            Check(Math.Abs(PlaybackPreferences.Read().Volume - .35) < .001, "volume preference survives save and reload");
            Check(LibraryService.HashFile(original) == before && fixtures.All(x => LibraryService.HashFile(x.Key) == x.Value), "playback and navigation preserve every source path and byte");
        }
        catch (Exception e) { failure = e + $"\nState={window.State}; Position={window.Player.Position.TotalSeconds}; Duration={window.SeekBar.Maximum}; Speed={window.Player.SpeedRatio}; Error={window.Failure}"; }
        File.WriteAllText(Path.Combine(AppPaths.Data, "video-playback-results.json"), JsonSerializer.Serialize(new { Passed = failure == null, Count = checks.Count, Checks = checks, OriginalUnchanged = LibraryService.HashFile(original) == before, Failure = failure }, new JsonSerializerOptions { WriteIndented = true }));
        window.FinishTest(failure == null ? 0 : 1);
    }
}
