using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Shiguang;

internal sealed record SlideshowPreferences(int Seconds = 5, bool Loop = true, bool Music = true, double Volume = .3, string MusicPath = "")
{
    static string FileName => Path.Combine(AppPaths.Data, "slideshow-preferences.json");
    public static SlideshowPreferences Read()
    {
        try
        {
            var p = JsonSerializer.Deserialize<SlideshowPreferences>(File.ReadAllText(FileName));
            return p == null ? new() : p with { Seconds = p.Seconds is 3 or 5 or 8 or 12 ? p.Seconds : 5, Volume = double.IsFinite(p.Volume) ? Math.Clamp(p.Volume, 0, 1) : .3, MusicPath = p.MusicPath ?? "" };
        }
        catch { return new(); }
    }
    public void Save()
    {
        var temporary = FileName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, FileName, true);
    }
}

public sealed partial class PhotoViewer
{
    readonly DispatcherTimer slideTimer = new();
    readonly MediaPlayer musicPlayer = new();
    SlideshowPreferences playback = SlideshowPreferences.Read();
    readonly TextBlock playbackStatus = new() { FontSize = UiMetrics.Caption, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8), Visibility = Visibility.Collapsed };
    Button playButton = null!, musicButton = null!;
    bool playing, imageReady, playbackClosed, musicOpened;
    string? openedMusic;
    public bool AutoPlay { get; init; }
    internal bool IsPlaying => playing;
    internal int SlideIndex => index;
    internal bool SlideTimerRunning => slideTimer.IsEnabled;
    internal bool MusicOpened => musicOpened;
    internal bool PlaybackClosed => playbackClosed;
    internal MediaPlayer BackgroundMusic => musicPlayer;

    void AddPlaybackControls(DockPanel body)
    {
        var panel = new StackPanel { Margin = new(0, 0, 0, 8) }; DockPanel.SetDock(panel, Dock.Top); body.Children.Add(panel);
        var controls = new WrapPanel(); panel.Children.Add(controls); panel.Children.Add(playbackStatus);
        ThemeManager.Resource(playbackStatus, TextBlock.ForegroundProperty, "Warning");
        playButton = IconButton("play", "自动播放", ToggleSlideshow, true); playButton.MinWidth = 116; playButton.IsEnabled = count > 1;
        if (count < 2) playButton.ToolTip = "至少两张照片才能自动切换";
        controls.Children.Add(playButton);
        controls.Children.Add(new TextBlock { Text = "每张", VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 4, 0) });
        var interval = new ComboBox { ItemsSource = new[] { 3, 5, 8, 12 }, SelectedItem = playback.Seconds, Width = 64, MinHeight = UiMetrics.Control, ToolTip = "每张照片停留秒数" };
        AutomationProperties.SetName(interval, "每张照片停留秒数"); controls.Children.Add(interval);
        interval.SelectionChanged += (_, _) => { playback = playback with { Seconds = (int)interval.SelectedItem }; SavePlayback(); RestartSlideTimer(); };
        controls.Children.Add(new TextBlock { Text = "秒", VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 12, 0) });
        var loop = new CheckBox { Content = "循环", IsChecked = playback.Loop, MinHeight = UiMetrics.Control, VerticalContentAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0), ToolTip = "最后一张后从头播放" };
        ThemeManager.Resource(loop, Control.ForegroundProperty, "Text");
        AutomationProperties.SetName(loop, "循环播放"); controls.Children.Add(loop);
        loop.Checked += (_, _) => ChangeLoop(true); loop.Unchecked += (_, _) => ChangeLoop(false);
        musicButton = IconButton("speaker_2", "背景音乐", () => { playback = playback with { Music = !playback.Music }; SavePlayback(); RefreshPlaybackButtons(); SyncMusic(); }, true); controls.Children.Add(musicButton);
        var musicOptions = IconButton("more_horizontal", "选择背景音乐", () => { });
        var menu = new ContextMenu(); musicOptions.ContextMenu = menu;
        var choose = new MenuItem { Header = "选择本地音乐…" }; choose.Click += (_, _) => ChooseMusic(); menu.Items.Add(choose);
        var builtin = new MenuItem { Header = "使用内置轻音乐 · 拾光" }; builtin.Click += (_, _) => SetMusicPath(""); menu.Items.Add(builtin);
        musicOptions.Click += (_, _) => { menu.PlacementTarget = musicOptions; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; }; controls.Children.Add(musicOptions);
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = playback.Volume, Width = 96, MinHeight = UiMetrics.Control, VerticalAlignment = VerticalAlignment.Center, ToolTip = "背景音乐音量" };
        AutomationProperties.SetName(volume, "背景音乐音量"); controls.Children.Add(volume);
        volume.ValueChanged += (_, _) => { playback = playback with { Volume = volume.Value }; musicPlayer.Volume = playback.Volume; };
        volume.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => SavePlayback())); volume.LostKeyboardFocus += (_, _) => SavePlayback();
        slideTimer.Tick += (_, _) => AdvanceSlide();
        musicPlayer.MediaOpened += (_, _) => { if (playbackClosed) return; musicOpened = true; if (playing && playback.Music) musicPlayer.Play(); };
        musicPlayer.MediaEnded += (_, _) => { if (!playbackClosed && playing && playback.Music) { musicPlayer.Position = TimeSpan.Zero; musicPlayer.Play(); } };
        musicPlayer.MediaFailed += (_, e) => { if (playbackClosed) return; musicPlayer.Close(); openedMusic = null; musicOpened = false; ShowPlaybackStatus("背景音乐无法播放，请选择其他音乐。照片仍可自动播放。" + e.ErrorException?.Message); };
        RefreshPlaybackButtons();
    }
    void ChangeLoop(bool value) { playback = playback with { Loop = value }; SavePlayback(); }
    void SavePlayback()
    {
        try { playback.Save(); }
        catch (Exception e) { ShowPlaybackStatus("播放设置未能保存：" + e.Message); }
    }
    void ShowPlaybackStatus(string text) { playbackStatus.Text = text; playbackStatus.Visibility = Visibility.Visible; }
    void ToggleSlideshow() => SetPlaying(!playing);
    internal void SetPlaying(bool value)
    {
        playing = value && count > 1 && !playbackClosed;
        RefreshPlaybackButtons(); RestartSlideTimer(); SyncMusic();
    }
    void RefreshPlaybackButtons()
    {
        string label = playing ? "暂停播放" : "自动播放";
        playButton.Content = Dialogs.LabeledIcon(playing ? "pause" : "play", label); playButton.ToolTip = count < 2 ? "至少两张照片才能自动切换" : label + "（空格）"; AutomationProperties.SetName(playButton, label);
        musicButton.Content = Dialogs.LabeledIcon(playback.Music ? "speaker_2" : "speaker_mute", playback.Music ? "背景音乐" : "音乐已关");
        musicButton.ToolTip = (playback.Music ? "关闭" : "开启") + "背景音乐 · " + (playback.MusicPath.Length == 0 ? "内置轻音乐 · 拾光" : Path.GetFileName(playback.MusicPath));
        AutomationProperties.SetName(musicButton, playback.Music ? "关闭背景音乐" : "开启背景音乐");
        AutomationProperties.SetHelpText(musicButton, musicButton.ToolTip.ToString());
    }
    void RestartSlideTimer()
    {
        slideTimer.Stop(); slideTimer.Interval = TimeSpan.FromSeconds(playback.Seconds);
        if (playing && imageReady && !playbackClosed) slideTimer.Start();
    }
    internal void AdvanceSlide()
    {
        if (!playing || !imageReady || playbackClosed) return;
        if (index == count - 1 && !playback.Loop) { SetPlaying(false); return; }
        Navigate(1);
    }
    void SyncMusic()
    {
        if (playbackClosed) return;
        if (!playing || !playback.Music) { musicPlayer.Pause(); return; }
        try
        {
            string path = playback.MusicPath.Length == 0 ? Path.Combine(AppContext.BaseDirectory, "Assets", "Music", "shiguang.wav") : playback.MusicPath;
            if (!File.Exists(path)) throw new IOException("音乐文件不存在，请重新选择，或使用内置轻音乐。");
            musicPlayer.Volume = playback.Volume;
            if (openedMusic != path) { musicPlayer.Close(); musicOpened = false; openedMusic = path; musicPlayer.Open(new Uri(path, UriKind.Absolute)); }
            musicPlayer.Play();
        }
        catch (Exception e) { musicPlayer.Close(); openedMusic = null; musicOpened = false; ShowPlaybackStatus(e.Message); }
    }
    void ChooseMusic()
    {
        bool resume = playing; SetPlaying(false);
        var picker = new OpenFileDialog { Title = "选择背景音乐", Filter = "音乐文件|*.mp3;*.wav;*.wma;*.m4a;*.aac|所有文件|*.*", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) == true) SetMusicPath(picker.FileName);
        if (resume) SetPlaying(true);
    }
    internal void SetMusicPath(string path)
    {
        musicPlayer.Close(); openedMusic = null; musicOpened = false; playback = playback with { MusicPath = path, Music = true };
        playbackStatus.Visibility = Visibility.Collapsed; SavePlayback(); RefreshPlaybackButtons(); SyncMusic();
    }
    void ClosePlayback()
    {
        SavePlayback(); playbackClosed = true; playing = false; slideTimer.Stop(); musicPlayer.Close(); openedMusic = null; musicOpened = false;
    }
}
