using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shiguang;

// The main catalog remains in its own process. This window only reads catalog references.
public partial class VideoPlayerWindow : Window
{
    internal enum PlaybackState { Opening, Playing, Paused, Ended, Failed }
    internal PlaybackState State { get; private set; } = PlaybackState.Opening;
    internal MediaElement Player { get; private set; } = null!;
    VideoQueue? queue;
    VideoRequest request;
    string currentPath = "", failure = "";
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly DispatcherTimer singleClick = new();
    readonly CancellationTokenSource closing = new();
    ContextMenu? openMenu;
    bool ready, dragging, buffering, keyboardNavigation, fullScreen, loop, finishing;
    double duration, speed = 1, volume = .6;
    bool muted;
    int generation, exitCode;
    WindowState priorWindowState;
    DateTime lastInput = DateTime.UtcNow, openedAt, feedbackUntil;
    Point lastPointer = new(-1, -1);
    internal bool IsFullScreen => fullScreen;
    internal string CurrentPath => currentPath;
    internal int QueueIndex => queue?.Index ?? 0;
    internal int QueueCount => queue?.Count ?? 0;
    internal double PlaybackSpeed => speed;
    internal string Failure => failure;
    internal string? PipeName { get; }

    public VideoPlayerWindow(string path, bool smoke = false) : this(new VideoRequest(path), null, smoke) { }
    public VideoPlayerWindow(VideoRequest request, string? pipeName = null, bool smoke = false)
    {
        this.request = request; PipeName = pipeName; InitializeComponent();
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/拾光照片;component/Assets/Shiguang.ico"));
        SourceInitialized += (_, _) => { try { int dark = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); } catch { } };
        var preferences = PlaybackPreferences.Read(); volume = preferences.Volume; muted = preferences.Muted; VolumeBar.Value = volume; ready = true;
        singleClick.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime());
        singleClick.Tick += (_, _) => { singleClick.Stop(); Toggle(); };
        timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { OpenRequest(request); timer.Start(); PlayerRoot.Focus(); if (pipeName != null) _ = Serve(pipeName); if (smoke) _ = VideoPlaybackTests.Run(this); };
        Deactivated += (_, _) => ShowControls();
        PreviewMouseWheel += (_, e) => { if (openMenu != null || !VolumeBar.IsEnabled) return; SetVolume(volume + (e.Delta > 0 ? .05 : -.05)); e.Handled = true; };
    }
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    internal void OpenRequest(VideoRequest value)
    {
        singleClick.Stop(); openMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false); openMenu = null;
        try
        {
            var next = new VideoQueue(value); queue?.Dispose(); queue = next; request = value;
            LoadCurrent();
        }
        catch (Exception e) { queue?.Dispose(); queue = null; request = value; currentPath = value.Path; FileTitle.Text = System.IO.Path.GetFileName(value.Path); ScopeLabel.Text = value.Scope + " · 1 / 1"; Fail(e.Message); }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate(); ShowControls();
    }
    internal void LoadCurrent(double startAt = 0, bool startPaused = false)
    {
        generation++; var token = generation; dragging = false; SeekBar.ReleaseMouseCapture();
        if (Player != null) { Player.Stop(); Player.Close(); } VideoSurface.Children.Clear();
        duration = 0; SeekBar.Value = 0; SeekBar.Maximum = 1; ElapsedLabel.Text = "00:00"; failure = ""; buffering = false; openedAt = DateTime.UtcNow;
        try { currentPath = queue?.CurrentPath ?? request.Path; } catch (Exception e) { Fail(e.Message); return; }
        Title = System.IO.Path.GetFileName(currentPath) + " · 时光相册视频"; FileTitle.Text = System.IO.Path.GetFileName(currentPath); FileTitle.ToolTip = currentPath;
        ScopeLabel.Text = $"{queue?.Scope ?? "视频"}  ·  {(queue?.Index ?? 0) + 1} / {queue?.Count ?? 1}";
        Player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform, ScrubbingEnabled = true, Volume = volume, IsMuted = muted, SpeedRatio = speed, IsHitTestVisible = false };
        VideoSurface.Children.Add(Player); var active = Player;
        active.MediaOpened += (_, _) =>
        {
            if (generation != token) return;
            duration = active.NaturalDuration.HasTimeSpan ? active.NaturalDuration.TimeSpan.TotalSeconds : 0;
            if (!active.HasVideo) { Fail("文件中没有可显示的视频画面。"); return; }
            SeekBar.Maximum = Math.Max(1, duration); active.SpeedRatio = speed;
            if (startPaused) { active.Pause(); active.Position = TimeSpan.FromSeconds(Math.Clamp(startAt, 0, duration)); }
            State = startPaused ? PlaybackState.Paused : PlaybackState.Playing; UpdateState();
        };
        active.MediaFailed += (_, e) => { if (generation == token) Fail("当前文件无法播放，可能已损坏或缺少对应的解码组件。\n" + e.ErrorException.Message); };
        active.MediaEnded += (_, _) => { if (generation != token) return; if (loop) { active.Position = TimeSpan.Zero; active.Play(); } else { active.Pause(); State = PlaybackState.Ended; SeekBar.Value = duration; ElapsedLabel.Text = FormatTime(TimeSpan.FromSeconds(duration)); UpdateState(); } };
        active.BufferingStarted += (_, _) => { if (generation == token) { buffering = true; UpdateState(); } };
        active.BufferingEnded += (_, _) => { if (generation == token) { buffering = false; UpdateState(); } };
        State = PlaybackState.Opening; UpdateState();
        if (!File.Exists(currentPath)) { Fail("找不到这个视频。来源磁盘可能已断开，或文件已在外部移动。请连接来源后重试。"); return; }
        try { active.Source = new Uri(System.IO.Path.GetFullPath(currentPath)); active.Play(); }
        catch (Exception e) { Fail(e.Message); }
    }
    void UpdateState()
    {
        bool playable = State is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Ended;
        PlayButton.IsEnabled = playable; SeekBar.IsEnabled = playable && duration > 0; BackTenButton.IsEnabled = ForwardTenButton.IsEnabled = SeekBar.IsEnabled;
        SpeedButton.IsEnabled = playable; MuteButton.IsEnabled = VolumeBar.IsEnabled = playable && Player?.HasAudio == true;
        MuteButton.ToolTip = Player?.HasAudio == false && playable ? "此视频没有音轨" : (muted || volume == 0 ? "取消静音（M）" : "静音（M）");
        PreviousButton.IsEnabled = queue?.Index > 0; NextButton.IsEnabled = queue != null && queue.Index < queue.Count - 1;
        string playLabel = State == PlaybackState.Playing ? "暂停" : State == PlaybackState.Ended ? "重新播放" : "播放";
        PlayIcon.Kind = State == PlaybackState.Playing ? "pause" : State == PlaybackState.Ended ? "arrow_clockwise" : "play";
        AutomationProperties.SetName(PlayButton, playLabel); PlayButton.ToolTip = playLabel + "（Space）";
        StateLabel.Text = buffering ? "正在缓冲" : State switch { PlaybackState.Opening => "正在打开", PlaybackState.Paused => "已暂停", PlaybackState.Ended => "播放完毕", PlaybackState.Failed => "无法播放", _ => "" };
        bool showState = State is PlaybackState.Opening or PlaybackState.Failed || buffering;
        StatePanel.Visibility = showState ? Visibility.Visible : Visibility.Collapsed;
        LoadingProgress.Visibility = State == PlaybackState.Opening || buffering ? Visibility.Visible : Visibility.Collapsed;
        RecoveryButtons.Visibility = State == PlaybackState.Failed ? Visibility.Visible : Visibility.Collapsed;
        StateIcon.Kind = State == PlaybackState.Failed ? "info" : "video";
        StateTitle.Text = State == PlaybackState.Failed ? "暂时无法播放" : buffering ? "正在缓冲" : "正在打开视频";
        StateDescription.Text = State == PlaybackState.Failed ? failure : "请稍候…";
        StateDescription.ToolTip = State == PlaybackState.Failed ? failure : null;
        // The drive can reconnect while this error screen is open. Retry must
        // remain available so users can revalidate the source without reopening.
        RetryButton.IsEnabled = State == PlaybackState.Failed;
        DurationLabel.Text = duration > 0 ? FormatTime(TimeSpan.FromSeconds(duration)) : "--:--";
        VolumeIcon.Kind = muted || volume == 0 ? "speaker_mute" : "speaker_2";
        AutomationProperties.SetName(MuteButton, muted || volume == 0 ? "取消静音" : "静音");
        VolumeBar.ToolTip = $"音量 {volume:P0}"; SpeedLabel.Text = speed.ToString("0.##", CultureInfo.InvariantCulture) + "×";
        if (State != PlaybackState.Playing || buffering) ShowControls();
    }
    void Fail(string message)
    {
        if (Player != null) { Player.Stop(); Player.Close(); }
        failure = message; State = PlaybackState.Failed; UpdateState();
    }
    internal void Toggle()
    {
        if (State is PlaybackState.Opening or PlaybackState.Failed) return;
        if (State == PlaybackState.Playing) { Player.Pause(); State = PlaybackState.Paused; Feedback("pause", "已暂停"); }
        else { if (State == PlaybackState.Ended) { Player.Position = TimeSpan.Zero; SeekBar.Value = 0; } Player.Play(); State = PlaybackState.Playing; Feedback("play", "播放"); }
        UpdateState(); Touch();
    }
    internal void SeekTo(double seconds)
    {
        if (!SeekBar.IsEnabled || !double.IsFinite(seconds)) return;
        double target = Math.Clamp(seconds, 0, duration);
        // Native playback can reset its clock on a seek after EOS. Re-open into a
        // paused position rather than displaying a target the decoder did not keep.
        if (State == PlaybackState.Ended) { LoadCurrent(target, startPaused: true); Touch(); return; }
        Player.Position = TimeSpan.FromSeconds(target); SeekBar.Value = target;
        if (State == PlaybackState.Playing) Player.Play(); else Player.Pause();
        ElapsedLabel.Text = FormatTime(TimeSpan.FromSeconds(target)); Touch();
    }
    void Skip(double seconds) { if (SeekBar.IsEnabled) { SeekTo(Player.Position.TotalSeconds + seconds); Feedback(seconds < 0 ? "skip_back_10" : "skip_forward_10", seconds < 0 ? "后退 10 秒" : "前进 10 秒"); } }
    internal void MoveVideo(int offset)
    {
        if (queue == null || queue.Index + offset < 0 || queue.Index + offset >= queue.Count) return;
        queue.Index += offset; LoadCurrent(); Touch();
    }
    internal void SetVolume(double value)
    {
        if (!double.IsFinite(value)) return;
        volume = Math.Clamp(value, 0, 1); if (volume > 0) muted = false;
        if (ready && Math.Abs(VolumeBar.Value - volume) > .001) VolumeBar.Value = volume;
        if (Player != null) { Player.Volume = volume; Player.IsMuted = muted; }
        if (ready) { UpdateState(); Feedback("speaker_2", $"音量 {volume:P0}"); Touch(); }
    }
    internal void ToggleMute()
    {
        if (!MuteButton.IsEnabled) return;
        muted = !(muted || volume == 0); if (!muted && volume == 0) volume = .6;
        Player.Volume = volume; Player.IsMuted = muted; VolumeBar.Value = volume; UpdateState(); Feedback(VolumeIcon.Kind, muted ? "已静音" : $"音量 {volume:P0}"); Touch();
    }
    internal void SetSpeed(double value)
    {
        if (value is not (.5 or .75 or 1 or 1.25 or 1.5 or 2)) return;
        speed = value;
        if (Player != null) { Player.SpeedRatio = value; if (State == PlaybackState.Playing) Player.Play(); }
        UpdateState(); Feedback("play", SpeedLabel.Text + " 播放"); Touch();
    }
    internal void ToggleFullscreen()
    {
        if (!fullScreen) { priorWindowState = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; fullScreen = true; }
        else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; WindowState = priorWindowState; fullScreen = false; }
        FullscreenIcon.Kind = fullScreen ? "full_screen_minimize" : "full_screen_maximize";
        AutomationProperties.SetName(FullscreenButton, fullScreen ? "退出全屏" : "全屏"); FullscreenButton.ToolTip = fullScreen ? "退出全屏（Esc / F11）" : "全屏（F11 / 双击画面）"; Touch();
    }
    void Tick()
    {
        if (State == PlaybackState.Opening && DateTime.UtcNow - openedAt > TimeSpan.FromSeconds(30)) Fail("打开视频超时。请检查磁盘连接或尝试用其他播放器打开。");
        if (!dragging && Player != null && State is PlaybackState.Playing or PlaybackState.Paused)
        {
            double position = Math.Max(0, Player.Position.TotalSeconds); SeekBar.Value = Math.Min(SeekBar.Maximum, position); ElapsedLabel.Text = FormatTime(TimeSpan.FromSeconds(position));
        }
        if (DateTime.UtcNow > feedbackUntil) FeedbackPanel.Visibility = Visibility.Collapsed;
        UpdateControlsVisibility();
    }
    internal void UpdateControlsVisibility()
    {
        bool pinned = State != PlaybackState.Playing || buffering || dragging || openMenu?.IsOpen == true || !IsActive || ControlBar.IsMouseOver || HeaderBar.IsMouseOver || keyboardNavigation && (ControlBar.IsKeyboardFocusWithin || HeaderBar.IsKeyboardFocusWithin);
        if (!pinned && DateTime.UtcNow - lastInput > TimeSpan.FromSeconds(3)) { HeaderBar.Visibility = ControlBar.Visibility = Visibility.Hidden; Cursor = Cursors.None; }
        else if (pinned) ShowControls();
    }
    void ShowControls() { HeaderBar.Visibility = ControlBar.Visibility = Visibility.Visible; Cursor = Cursors.Arrow; }
    void Touch() { lastInput = DateTime.UtcNow; ShowControls(); }
    void Feedback(string icon, string text) { FeedbackIcon.Kind = icon; FeedbackText.Text = text; FeedbackPanel.Visibility = Visibility.Visible; feedbackUntil = DateTime.UtcNow.AddMilliseconds(850); }
    void Player_MouseMove(object sender, MouseEventArgs e) { var point = e.GetPosition(PlayerRoot); if (Math.Abs(point.X - lastPointer.X) + Math.Abs(point.Y - lastPointer.Y) > 2) { lastPointer = point; Touch(); } }
    void Player_MouseDown(object sender, MouseButtonEventArgs e) { keyboardNavigation = false; Touch(); }
    void VideoSurface_Click(object sender, MouseButtonEventArgs e)
    {
        PlayerRoot.Focus(); e.Handled = true;
        if (e.ClickCount == 2) { singleClick.Stop(); ToggleFullscreen(); }
        else { singleClick.Stop(); singleClick.Start(); }
    }
    void Seek_Down(object sender, MouseButtonEventArgs e)
    {
        if (!SeekBar.IsEnabled) return; SeekBar.Focus(); dragging = true; if (State == PlaybackState.Playing) Player.Pause();
        SeekBar.CaptureMouse(); SetSeekPoint(e.GetPosition(SeekBar)); Touch(); e.Handled = true;
    }
    void SetSeekPoint(Point point) { SeekBar.Value = Math.Clamp((point.X - 7) / Math.Max(1, SeekBar.ActualWidth - 14), 0, 1) * duration; ElapsedLabel.Text = FormatTime(TimeSpan.FromSeconds(SeekBar.Value)); SeekBar.ToolTip = "跳转到 " + ElapsedLabel.Text; }
    void Seek_Move(object sender, MouseEventArgs e) { if (dragging) { SetSeekPoint(e.GetPosition(SeekBar)); e.Handled = true; } }
    void Seek_Up(object sender, MouseButtonEventArgs e) { if (dragging) { FinishSeek(); SeekBar.ReleaseMouseCapture(); e.Handled = true; } }
    void Seek_LostCapture(object sender, MouseEventArgs e) { if (dragging) FinishSeek(); }
    void FinishSeek() { double target = SeekBar.Value; dragging = false; SeekTo(target); if (State == PlaybackState.Playing) Player.Play(); }
    void Player_KeyDown(object sender, KeyEventArgs e)
    {
        if (openMenu?.IsOpen == true) return;
        if (e.Key == Key.Tab) { keyboardNavigation = true; Touch(); return; }
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown && Keyboard.FocusedElement == VolumeBar) return;
        bool handled = true;
        switch (e.Key)
        {
            case Key.Escape: if (fullScreen) ToggleFullscreen(); else Close(); break;
            case Key.F11: ToggleFullscreen(); break;
            case Key.Space:
                if (Keyboard.FocusedElement is ButtonBase) return;
                Toggle(); break;
            case Key.M: ToggleMute(); break;
            case Key.Left: if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) MoveVideo(-1); else if (Keyboard.FocusedElement == SeekBar) SeekTo(Player.Position.TotalSeconds - 5); else Skip(-10); break;
            case Key.Right: if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) MoveVideo(1); else if (Keyboard.FocusedElement == SeekBar) SeekTo(Player.Position.TotalSeconds + 5); else Skip(10); break;
            case Key.Up: if (VolumeBar.IsEnabled) SetVolume(volume + .05); break;
            case Key.Down: if (VolumeBar.IsEnabled) SetVolume(volume - .05); break;
            case Key.Home: SeekTo(0); break;
            case Key.End: SeekTo(duration); break;
            case Key.PageUp: SeekTo(Player.Position.TotalSeconds + 30); break;
            case Key.PageDown: SeekTo(Player.Position.TotalSeconds - 30); break;
            default: handled = false; break;
        }
        if (handled) { e.Handled = true; Touch(); }
    }
    ContextMenu Menu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Top, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(30, 39, 53)) };
        foreach (var key in new[] { "Text", "Muted" }) menu.Resources[key] = Brushes.White;
        menu.Resources["Surface"] = menu.Background; menu.Resources["Line"] = new SolidColorBrush(Color.FromRgb(63, 77, 99)); menu.Resources["Hover"] = new SolidColorBrush(Color.FromRgb(53, 71, 96));
        menu.Closed += (_, _) => { if (openMenu == menu) openMenu = null; Touch(); }; openMenu = menu; return menu;
    }
    static MenuItem MenuAction(string text, string icon, Action action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(new FluentIcon { Kind = icon, Foreground = Brushes.White, Width = 20, Height = 20 }); row.Children.Add(new TextBlock { Text = text, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var item = new MenuItem { Header = row }; AutomationProperties.SetName(item, text); item.Click += (_, _) => action(); return item;
    }
    void Speed_Click(object sender, RoutedEventArgs e)
    {
        var menu = Menu(SpeedButton);
        foreach (double rate in new[] { .5, .75, 1, 1.25, 1.5, 2 }) menu.Items.Add(MenuAction(rate.ToString("0.##", CultureInfo.InvariantCulture) + "×" + (rate == 1 ? "  正常速度" : ""), rate == speed ? "checkmark_circle" : "play", () => SetSpeed(rate)));
        menu.IsOpen = true; Touch();
    }
    void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = Menu(MoreButton);
        menu.Items.Add(MenuAction(loop ? "关闭单段循环" : "单段循环", loop ? "checkmark_circle" : "arrow_repeat_all", () => { loop = !loop; Feedback("arrow_repeat_all", loop ? "单段循环已开启" : "单段循环已关闭"); }));
        menu.Items.Add(MenuAction("从头播放", "arrow_clockwise", () => { SeekTo(0); if (State != PlaybackState.Playing) Toggle(); }));
        menu.Items.Add(MenuAction("视频信息", "info", ShowInfo));
        menu.Items.Add(MenuAction("用其他播放器打开", "video", External));
        menu.Items.Add(MenuAction("快捷键", "apps_list", () => MessageBox.Show(this, "Space  播放 / 暂停（按钮聚焦时执行按钮操作）\n← / →  后退 / 前进 10 秒\n进度条聚焦时 ← / → 调整 5 秒\nCtrl+← / →  上一段 / 下一段\n↑ / ↓  音量\nM  静音\nF11 / 双击画面  全屏\nEsc  退出全屏 / 返回媒体库", "播放快捷键")));
        menu.IsOpen = true; Touch();
    }
    void ShowInfo()
    {
        string size = File.Exists(currentPath) ? $"{new FileInfo(currentPath).Length / 1048576d:0.##} MB" : "来源不可访问";
        MessageBox.Show(this, $"{FileTitle.Text}\n\n{Player.NaturalVideoWidth} × {Player.NaturalVideoHeight} · {DurationLabel.Text} · {size}\n\n{currentPath}", "视频信息");
    }
    void External()
    {
        try { if (State == PlaybackState.Playing) Toggle(); Process.Start(new ProcessStartInfo(currentPath) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show(this, "其他播放器未能打开此文件：\n" + e.Message, "无法打开"); }
    }
    async Task Serve(string pipeName)
    {
        while (!closing.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(closing.Token);
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(closing.Token); readTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var reader = new StreamReader(server, leaveOpen: true); using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                string? line = await reader.ReadLineAsync(readTimeout.Token);
                var next = line == null || line.Length > 100000 ? null : JsonSerializer.Deserialize<VideoRequest>(line);
                if (next == null) { await writer.WriteLineAsync("invalid"); continue; }
                OpenRequest(next); await writer.WriteLineAsync("ok");
            }
            catch (OperationCanceledException) { if (closing.IsCancellationRequested) break; }
            catch (Exception e) { if (!closing.IsCancellationRequested) LibraryService.Log("播放窗口", "", e); }
        }
    }
    void Player_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (finishing) return; finishing = true;
        timer.Stop(); singleClick.Stop(); closing.Cancel();
        try { Player?.Stop(); Player?.Close(); queue?.Dispose(); new PlaybackPreferences(volume, muted).Save(); }
        catch (Exception error) { LibraryService.Log("关闭播放窗口", currentPath, error); }
        ThemeManager.Stop(); SafeShutdown.Finish(exitCode);
    }
    internal void FinishTest(int code) { exitCode = code; Close(); }
    internal void ForceIdleForTest() { lastInput = DateTime.UtcNow.AddSeconds(-4); UpdateControlsVisibility(); }
    internal static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
    void Back_Click(object sender, RoutedEventArgs e) => Close();
    void Play_Click(object sender, RoutedEventArgs e) => Toggle();
    void Previous_Click(object sender, RoutedEventArgs e) => MoveVideo(-1);
    void Next_Click(object sender, RoutedEventArgs e) => MoveVideo(1);
    void BackTen_Click(object sender, RoutedEventArgs e) => Skip(-10);
    void ForwardTen_Click(object sender, RoutedEventArgs e) => Skip(10);
    void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();
    void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    void Retry_Click(object sender, RoutedEventArgs e) => LoadCurrent();
    void External_Click(object sender, RoutedEventArgs e) => External();
    void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ready) SetVolume(e.NewValue); }
}
