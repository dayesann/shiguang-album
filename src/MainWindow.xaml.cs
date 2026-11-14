using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Shiguang;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    readonly Catalog catalog;
    readonly MediaIndexer indexer;
    readonly VideoLauncher videoLauncher = new();
    readonly List<Photo> loaded = [];
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(260) };
    readonly DispatcherTimer resize = new() { Interval = TimeSpan.FromMilliseconds(170) };
    CancellationTokenSource? cancellation;
    ManualResetEventSlim? activePause;
    bool ready, busy, paused, canPause, closeWhenDone, loading, details;
    string view = "timeline", mode = "days", albumId = "", albumName = "", kind = "all";
    string status = "选择来源目录建立索引，原文件保持原位";
    int total; double bucketWidth = 260;
    Photo? focused;
    static readonly string[] KindKeys = ["all", "image", "video", "panorama", "screenshot"];
    public ObservableCollection<GalleryRow> Rows { get; } = [];
    public ObservableCollection<TimeBucket> Buckets { get; } = [];
    public ObservableCollection<Album> Albums { get; } = [];
    public ObservableCollection<string> RailYears { get; } = [];
    public bool IsBusy => busy;
    internal Action<int>? SafeClose { get; set; }
    internal int ExitCode { get; set; }
    public bool CanMutate => !closeWhenDone;
    public bool CanScan => !busy && catalog.Operations().Count == 0;
    public bool CanFindDuplicates => !busy && catalog.Count() > 1;
    public bool CanPause => busy && canPause;
    public string PauseText => paused ? "继续" : "暂停";
    public string StatusText => status;
    public bool InAlbum => view == "album";
    public bool ShowTimelineSwitch => view != "albums" && !IsAlbumEmpty;
    public bool IsAlbumOverview => view == "albums";
    public bool CanPlaySlideshow => !IsAlbumOverview && kind != "video" && total > 1;
    public bool IsAlbumEmpty => InAlbum && Albums.FirstOrDefault(x => x.Id == albumId)?.Count == 0;
    public bool ShowFilters => view != "albums" && !IsAlbumEmpty;
    public GridLength FilterRowHeight => new(0);
    public GridLength RailWidth => new(ShowFilters ? 66 : 0);
    public bool ShowGallery => view != "albums" && mode == "days";
    public bool ShowBuckets => !ShowGallery;
    public bool ShowDetails => details && ShowGallery && !IsEmpty;
    public bool CanShowDetails => ShowGallery && !IsEmpty;
    public GridLength DetailWidth => new(ShowDetails ? 285 : 0);
    public bool IsSelecting { get; private set; }
    public string SelectionModeText => IsSelecting ? "完成" : "多选";
    public string SelectionModeHint => IsSelecting ? "退出多选（Esc）" : "勾选多个项目";
    public bool HasSelection => IsSelecting && selectedCount > 0;
    public string SelectionText => $"已选择 {selectedCount:N0} 个项目";
    public bool CanEditSelection => HasSelection && CanMutate;
    public bool CanTrashSelection => HasSelection && CanScan && CanMutate && view != "missing";
    public bool IsEmpty => total == 0;
    public double BucketWidth => bucketWidth;
    public Photo? FocusedPhoto => focused;
    public string MembershipText => focused == null ? "选择一个项目查看" : catalog.Memberships(focused.Id) is string s && s.Length > 0 ? s : "尚未加入相册";
    public string Heading => view switch { "album" => albumName, "albums" => "相册", "favorites" => "收藏", "errors" => "预览不可用", "missing" => "离线媒体", "unfiled" => "未加入相册", "media" => kind switch { "image" => "照片", "video" => "视频", "panorama" => "全景图", "screenshot" => "截图", _ => "全部媒体" }, _ => "时间线" };
    public string Summary => view == "albums" ? $"{Albums.Count} 个相册" : $"{total:N0} 个项目";
    public string DateBreadcrumb => CurrentFilter() is var f && f.Year.Length > 0 ? string.Join(" / ", new[] { f.Year + "年", f.Month.Length > 0 ? int.Parse(f.Month) + "月" : "", f.Day.Length > 0 ? int.Parse(f.Day) + "日" : "" }.Where(x => x.Length > 0)) : "全部时间";
    public bool HasStatus => busy;
    public bool ShowFooter => busy || IsSelecting;
    public string EmptyHeading => IsAlbumOverview ? "还没有相册" : IsAlbumEmpty ? "这个相册还没有内容" : catalog.Count() == 0 ? "把回忆连起来" : "没有符合条件的内容";
    public bool ShowEmptyImport => !IsAlbumOverview && catalog.Count() == 0;
    public bool ShowEmptyAlbumAction => IsAlbumOverview;
    public bool ShowEmptyTimelineAction => IsAlbumEmpty && catalog.Count() > 0;
    public string EmptyDescription => IsAlbumEmpty ? "返回时间线，点击“多选”并勾选项目，再点击“加入相册”。相册只保存引用，原文件保持原位。" : IsAlbumOverview ? "新建一个相册，按人物、旅行或活动收集回忆。相册只保存引用。" : catalog.Count() == 0 ? "添加已有照片或视频目录，按时间线、相册和媒体类型浏览。只建立索引，不移动原文件。" : view == "albums" ? "点击左侧相册旁的按钮新建相册，再选择媒体加入。" : "可以调整年份、月份、日期或媒体类型，也可以清空搜索条件。";
    public event PropertyChangedEventHandler? PropertyChanged;
    void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    void NotifyAll() => PropertyChanged?.Invoke(this, new(""));
    public MainWindow(Catalog catalog)
    {
        this.catalog = catalog; indexer = new(catalog); InitializeComponent(); DataContext = this;
        videoLauncher.Returned += () => Dispatcher.BeginInvoke(() => { if (IsLoaded) Activate(); });
        ThemeManager.Attach(this); ThemeManager.Changed += UpdateMode; Closed += (_, _) => ThemeManager.Changed -= UpdateMode;
        debounce.Tick += (_, _) => { debounce.Stop(); RefreshPage(); };
        resize.Tick += (_, _) => { resize.Stop(); Reflow(); Notify(nameof(DetailWidth)); };
        AlbumList.MouseRightButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(AlbumList, source) is ListBoxItem { DataContext: Album a }) { AlbumList.SelectedItem = a; AlbumMenu(a); e.Handled = true; } };
    }
    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (ready) return;
        new FileOrganizer(catalog).Reconcile(); // Only reconcile moves already finished; never delete a remaining original.
        KindBox.ItemsSource = new[] { "全部媒体", "照片", "视频", "全景图", "截图" }; KindBox.SelectedIndex = 0;
        ready = true; UpdateHeaderDensity(); ReloadSidebar(); ResetDates(); RefreshPage(); UpdateMode();
        if (catalog.Count() > 0) SetStatus($"媒体库已就绪 · {catalog.Count():N0} 个项目 · 原文件保持原位");
        if (catalog.Operations().Count > 0)
            Notification.Show("有未完成的文件操作。请先核对范围并恢复任务，再扫描或整理。", "任务恢复", async () => await RecoverTasks(), error: true);
        else if (catalog.PendingPreviews().Count > 0 || catalog.PendingVisualFingerprints().Count > 0)
            _ = RunJob("补充预览与视觉指纹", async (p, ct) => { int n = await PreviewPipeline.Enrich(catalog, p, ct); return $"已补充 {n} 项预览或视觉指纹 · 原文件未变"; });
    }
    public MediaFilter CurrentFilter() => new(view, albumId, kind, DatePart(YearBox, "全部年份"), DatePart(MonthBox, "全部月份"), DatePart(DayBox, "全部日期"), SearchBox.Text.Trim());
    static string DatePart(ComboBox box, string all) => box.SelectedItem is string s && s != all ? s : "";
    void SetCombo(ComboBox box, IEnumerable<string> values, string selected)
    {
        var list = values.ToList(); box.ItemsSource = list; box.SelectedItem = list.Contains(selected) ? selected : list.FirstOrDefault();
    }
    void ReloadSidebar()
    {
        bool prior = ready; ready = false;
        Albums.Clear(); foreach (var a in catalog.Albums()) Albums.Add(a);
        string selectedYear = YearBox.SelectedItem as string ?? "全部年份";
        var years = catalog.Years(); SetCombo(YearBox, new[] { "全部年份" }.Concat(years), selectedYear);
        RailYears.Clear(); foreach (var y in years) RailYears.Add(y);
        if (InAlbum) AlbumList.SelectedItem = Albums.FirstOrDefault(a => a.Id == albumId);
        ready = prior;
    }
    void ResetDates()
    {
        bool prior = ready; ready = false; YearBox.SelectedIndex = 0;
        SetCombo(MonthBox, ["全部月份"], "全部月份"); SetCombo(DayBox, ["全部日期"], "全部日期"); ready = prior;
    }
    public void SelectDate(string year, string month = "", string day = "")
    {
        bool prior = ready; ready = false;
        YearBox.SelectedItem = year.Length > 0 ? year : "全部年份";
        SetCombo(MonthBox, new[] { "全部月份" }.Concat(year.Length > 0 ? catalog.Months(year) : []), month.Length > 0 ? month : "全部月份");
        SetCombo(DayBox, new[] { "全部日期" }.Concat(year.Length > 0 && month.Length > 0 ? catalog.Days(year, month) : []), day.Length > 0 ? day : "全部日期");
        ready = prior; RefreshPage();
    }
    public void SetMode(string value) { mode = value; UpdateMode(); RefreshPage(); }
    public void SetCategory(string value) { kind = value; KindBox.SelectedIndex = Array.IndexOf(KindKeys, value); if (mode != "days") { mode = "days"; UpdateMode(); } RefreshPage(); }
    public void OpenAlbum(string id)
    {
        var a = Albums.First(x => x.Id == id); view = "album"; albumId = id; albumName = a.Name; kind = "all"; mode = "days";
        bool prior = ready; ready = false; SearchBox.Clear(); KindBox.SelectedIndex = 0; AlbumList.SelectedItem = a; ready = prior; debounce.Stop(); ResetDates(); RefreshPage(); UpdateMode();
    }
    public void RefreshPage(bool preserveSelection = false)
    {
        if (!ready) return;
        var selected = preserveSelection && IsSelecting ? loaded.Where(x => x.Selected).Select(x => x.Id).ToHashSet() : [];
        bool restoreKeyboardFocus = preserveSelection && Gallery.IsKeyboardFocusWithin;
        string? focusedId = preserveSelection ? focused?.Id : null;
        int previousCount = preserveSelection ? loaded.Count : 0;
        var scroll = UiElements.Descendants<ScrollViewer>(Gallery).FirstOrDefault(); double offset = scroll?.VerticalOffset ?? 0;
        foreach (var p in loaded) p.PropertyChanged -= Photo_Changed;
        loaded.Clear(); Rows.Clear(); Buckets.Clear(); focused = null; selectedCount = 0;
        if (!preserveSelection) IsSelecting = false;
        if (view == "albums")
        {
            foreach (var a in Albums) { var cover = catalog.Browse(new("album", a.Id), 0, 1).Items.FirstOrDefault(); Buckets.Add(new(a.Id, a.Name, a.Count, cover?.ThumbPath ?? "")); }
            total = Albums.Count;
        }
        else if (mode != "days")
        {
            var f = CurrentFilter();
            if (mode == "years") f = f with { Year = "", Month = "", Day = "" };
            else f = f with { Month = "", Day = "" };
            foreach (var b in catalog.Buckets(f, mode == "months")) Buckets.Add(b);
            total = Buckets.Sum(b => b.Count);
        }
        else
        {
            LoadMore(Math.Max(240, previousCount));
            updatingSelection = true;
            foreach (var photo in loaded)
                if (selected.Contains(photo.Id)) photo.Selected = true;
            updatingSelection = false;
            selectedCount = loaded.Count(photo => photo.Selected);
            focused = loaded.FirstOrDefault(photo => photo.Id == focusedId) ?? focused;
        }
        if (IsEmpty) IsSelecting = false;
        NotifyAll();
        if (restoreKeyboardFocus && focused != null) FocusGalleryPhoto(focused);
        else if (preserveSelection && scroll != null) Dispatcher.BeginInvoke(() => scroll.ScrollToVerticalOffset(offset));
    }
    void LoadMore(int limit = 240)
    {
        if (!ready || loading || !ShowGallery) return; loading = true;
        try
        {
            var page = catalog.Browse(CurrentFilter(), loaded.Count, limit); total = page.Total;
            foreach (var p in page.Items) { p.PropertyChanged += Photo_Changed; loaded.Add(p); }
            focused ??= loaded.FirstOrDefault(); Reflow(); NotifyAll();
        }
        catch (Exception e) { Error(e); }
        finally { loading = false; }
    }
    void Reflow()
    {
        if (!ready) return;
        bool restoreKeyboardFocus = Gallery.IsKeyboardFocusWithin;
        double area = Math.Max(300, Gallery.ActualWidth > 50 ? Gallery.ActualWidth - 32 : ActualWidth - 380 - (ShowDetails ? 285 : 0));
        bucketWidth = Math.Max(200, Math.Floor(area / Math.Max(2, (int)(area / 270))) - 20); Notify(nameof(BucketWidth));
        var rows = GalleryLayout.Build(loaded, area, view == "timeline"); int same = 0;
        while (same < Math.Min(Rows.Count, rows.Count) && Rows[same].Label == rows[same].Label && Rows[same].Items.Select(x => (x.Photo.Id, x.Width, x.Height)).SequenceEqual(rows[same].Items.Select(x => (x.Photo.Id, x.Width, x.Height)))) same++;
        while (Rows.Count > same) Rows.RemoveAt(Rows.Count - 1);
        for (int i = same; i < rows.Count; i++) Rows.Add(rows[i]);
        if (restoreKeyboardFocus && !Gallery.IsKeyboardFocusWithin && focused != null)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (Keyboard.FocusedElement is null || ReferenceEquals(Keyboard.FocusedElement, this))
                    FocusGalleryPhoto(focused);
            });
    }
    void SetStatus(string text) { status = text; Notify(nameof(StatusText)); Notify(nameof(HasStatus)); }
    void Error(Exception e) { SetStatus(e.Message); Notification.Show(e.Message, error: true); }
    async Task RunJob(string title, Func<IProgress<JobProgress>, CancellationToken, Task<string>> work, bool pause = false, ManualResetEventSlim? pauseSignal = null)
    {
        if (busy) return;
        busy = true; Notification.ActionEnabled = false; activePause = pause ? pauseSignal ?? indexer.Pause : null; canPause = activePause != null; paused = false;
        cancellation = new(); activePause?.Set(); NotifyAll(); SetStatus(title);
        var progress = new Progress<JobProgress>(p =>
        {
            SetStatus(p.Message);
            if (p.PhotoId.Length > 0)
            {
                var local = loaded.FirstOrDefault(x => x.Id == p.PhotoId); if (local != null && catalog.ById(p.PhotoId) is Photo update) local.RefreshFrom(update);
                if (p.Completed % 20 == 0) ReloadSidebar();
            }
        });
        try { SetStatus(await work(progress, cancellation.Token)); }
        catch (OperationCanceledException) { SetStatus("已安全停止 · 已有索引可浏览，原文件未变"); }
        catch (Exception e) { LibraryService.Log("任务", title, e); Error(e); }
        finally { busy = false; Notification.ActionEnabled = true; canPause = paused = false; activePause?.Set(); activePause = null; cancellation.Dispose(); cancellation = null; indexer.Pause.Set(); ReloadSidebar(); RefreshPage(true); NotifyAll(); if (closeWhenDone) Close(); }
    }
    public Task ScanDirectories(IEnumerable<string> roots) => RunJob("建立媒体索引", async (p, ct) =>
    {
        int existing = catalog.Count();
        int added = 0, updated = 0, failed = 0;
        foreach (var root in roots) { var r = await Task.Run(() => indexer.Scan(root, p, ct), ct); added += r.Added; updated += r.Updated; failed += r.Failed; }
        await Dispatcher.InvokeAsync(() => { ReloadSidebar(); RefreshPage(true); canPause = false; NotifyAll(); });
        int n = await PreviewPipeline.Enrich(catalog, p, ct);
        int duplicateGroups = 0;
        if (existing > 0 && added > 0)
        {
            var duplicateService = new LibraryService(catalog);
            duplicateGroups = (await Task.Run(() => duplicateService.FindDuplicates(p, ct), ct)).Count;
        }
        return $"索引完成 · 新增 {added:N0} · 更新 {updated:N0} · 预览 {n:N0} · 读取失败 {failed:N0}" +
            (existing > 0 && added > 0 ? $" · 更新重复索引 {duplicateGroups:N0} 组" : "") + " · 原文件未变";
    }, true);
    async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try { var source = await FolderPicker.ChooseAsync(this, catalog.Roots.FirstOrDefault() ?? System.IO.Path.GetPathRoot(AppPaths.Data)!); if (source != null) await ScanDirectories([source]); }
        catch (Exception error) { Error(error); }
    }
    async void Duplicates_Click(object sender, RoutedEventArgs e) => await ReviewDuplicates();
    public async Task ReviewDuplicates()
    {
        if (busy || catalog.Count() < 2) return;
        List<DuplicateGroup>? groups;
        if (catalog.HasDuplicateIndex())
        {
            groups = catalog.CachedDuplicateGroups();
            SetStatus($"已载入重复索引 · 完全重复 {groups.Count(x => x.Kind == "exact"):N0} 组 · 近似候选 {groups.Count(x => x.Kind == "similar"):N0} 组");
        }
        else
        {
            groups = null;
            var duplicateService = new LibraryService(catalog);
            var watch = Stopwatch.StartNew();
            await RunJob("读取媒体指纹", async (progress, ct) =>
            {
                groups = await Task.Run(() => duplicateService.FindDuplicates(progress, ct), ct);
                int exact = groups.Count(x => x.Kind == "exact");
                int similar = groups.Count(x => x.Kind == "similar");
                return $"快速查重完成 · {watch.Elapsed.TotalSeconds:0.0} 秒 · 完全重复 {exact:N0} 组 · 近似候选 {similar:N0} 组 · 原文件未变";
            }, true, duplicateService.Pause);
        }
        if (groups == null || !IsLoaded) return;

        var review = new DuplicateWindow(groups) { Owner = this };
        bool accepted = review.ShowDialog() == true;
        if (review.OpenTrashRequested)
        {
            await OpenTrash();
        }
        else if (accepted && review.ToDelete.Count > 0)
        {
            var organizer = new FileOrganizer(catalog);
            await RunJob("将重复照片移入最近删除", async (progress, ct) =>
                $"已移入最近删除 {await Task.Run(() => organizer.Trash(review.ToDelete, review.Keepers, progress, ct), ct):N0} 项 · 可稍后恢复",
                false);
        }
        else if (groups.Count == 0)
        {
            SetStatus("未发现重复照片 · 原文件未变");
        }
    }
    public async Task OpenTrash()
    {
        if (busy) return;
        var items = catalog.Trashed();
        var dialog = new TrashWindow(items) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedPhotos.Count == 0) return;
        var selected = dialog.SelectedPhotos.ToList();
        var organizer = new FileOrganizer(catalog);
        if (dialog.RequestedAction == TrashRequestedAction.Restore)
        {
            await RunJob("恢复最近删除", async (progress, ct) =>
                $"已恢复 {await Task.Run(() => organizer.Restore(selected, progress, ct), ct):N0} 项", false);
        }
        else if (dialog.RequestedAction == TrashRequestedAction.Purge)
        {
            await RunJob("永久删除最近删除项目", async (progress, ct) =>
                $"已永久删除 {await Task.Run(() => organizer.Purge(selected, progress, ct), ct):N0} 项", false);
        }
    }
    void Nav_Click(object sender, RoutedEventArgs e)
    {
        view = (string)((Button)sender).Tag; albumId = ""; kind = "all"; mode = "days";
        bool prior = ready; ready = false; AlbumList.SelectedIndex = -1; KindBox.SelectedIndex = 0; SearchBox.Clear(); ready = prior;
        ResetDates(); UpdateMode(); RefreshPage();
    }
    void Type_Click(object sender, RoutedEventArgs e)
    {
        view = "media"; albumId = ""; bool prior = ready; ready = false; SearchBox.Clear(); AlbumList.SelectedIndex = -1; ready = prior; debounce.Stop(); ResetDates(); SetCategory((string)((Button)sender).Tag); UpdateMode();
    }
    void Mode_Click(object sender, RoutedEventArgs e)
    {
        string value = (string)((Button)sender).Tag;
        if (value == "years") ResetDates();
        if (value == "months" && DatePart(YearBox, "全部年份").Length == 0 && RailYears.Count > 0) SelectDate(RailYears[0]);
        SetMode(value);
    }
    void UpdateMode()
    {
        foreach (var (button, value) in new[] { (YearMode, "years"), (MonthMode, "months"), (DayMode, "days") })
        {
            if (mode == value) button.SetResourceReference(BackgroundProperty, "Selected"); else button.ClearValue(BackgroundProperty);
            button.SetResourceReference(ForegroundProperty, mode == value ? "SelectedText" : "Muted"); button.FontWeight = mode == value ? FontWeights.SemiBold : FontWeights.Normal;
            button.IsTabStop = mode == value;
            if (button is SegmentChoice choice) choice.IsSelected = mode == value;
            System.Windows.Automation.AutomationProperties.SetHelpText(button, mode == value ? "当前视图" : "切换时间视图");
        }
        if (view == "timeline") TimelineNav.SetResourceReference(BackgroundProperty, "Selected"); else TimelineNav.Background = Brushes.Transparent;
        TimelineNav.SetResourceReference(ForegroundProperty, view == "timeline" ? "SelectedText" : "Text"); NotifyAll();
        foreach (var button in UiElements.Descendants<Button>(this).Where(b => b.Style == FindResource("NavButton") && b.Tag is string))
        {
            bool active = button.Tag is string tag && (tag == view || view == "album" && tag == "albums" || view == "media" && tag == kind);
            if (active) button.SetResourceReference(BackgroundProperty, "Selected"); else button.Background = Brushes.Transparent;
            button.SetResourceReference(ForegroundProperty, active ? "SelectedText" : "Text");
        }
    }
    void Bucket_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not TimeBucket b) return;
        if (view == "albums") OpenAlbum(b.Key);
        else if (mode == "years") { SelectDate(b.Key); SetMode("months"); }
        else { SelectDate(b.Key[..4], b.Key.Substring(5, 2)); SetMode("days"); }
    }
    void RailYear_Click(object sender, RoutedEventArgs e) { if (view == "albums") view = "timeline"; SelectDate((string)((Button)sender).Tag); SetMode("months"); }
    void Year_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return; ready = false;
        string year = DatePart(YearBox, "全部年份"); SetCombo(MonthBox, new[] { "全部月份" }.Concat(year.Length > 0 ? catalog.Months(year) : []), "全部月份"); SetCombo(DayBox, ["全部日期"], "全部日期"); ready = true; RefreshPage();
        if (year.Length > 0 && mode == "years") SetMode("months");
    }
    void Month_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return; ready = false; string year = DatePart(YearBox, "全部年份"), month = DatePart(MonthBox, "全部月份");
        SetCombo(DayBox, new[] { "全部日期" }.Concat(year.Length > 0 && month.Length > 0 ? catalog.Days(year, month) : []), "全部日期"); ready = true;
        if (month.Length > 0) { mode = "days"; UpdateMode(); } RefreshPage();
    }
    void Day_Changed(object sender, SelectionChangedEventArgs e) { if (ready) { mode = "days"; UpdateMode(); RefreshPage(); } }
    void Kind_Changed(object sender, SelectionChangedEventArgs e) { if (ready) { kind = KindKeys[Math.Max(0, KindBox.SelectedIndex)]; UpdateMode(); RefreshPage(); } }
    void Search_Changed(object sender, TextChangedEventArgs e) { if (ready) { debounce.Stop(); debounce.Start(); } }
    void Search_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { SearchBox.Clear(); e.Handled = true; } }
    void ModeSwitch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) return;
        var choices = new[] { YearMode, MonthMode, DayMode };
        int current = Array.FindIndex(choices, b => b.IsKeyboardFocusWithin); if (current < 0) return;
        int next = e.Key == Key.Home ? 0 : e.Key == Key.End ? 2 : (current + (e.Key == Key.Right ? 1 : -1) + 3) % 3;
        choices[next].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); choices[next].Focus(); e.Handled = true;
    }
    async void Rescan_Click(object sender, RoutedEventArgs e) => await RescanSources();
    void UpdateHeaderDensity()
    {
        if (SearchColumn == null) return;
        SearchColumn.Width = new(ActualWidth < 940 ? 104 : ActualWidth < 1120 ? 128 : 180);
        double actions = ActualWidth < 940 ? 68 : ActualWidth < 1120 ? 104 : 140;
        ActionColumn.Width = new(actions); ActionToolbar.Width = actions;
    }
    void Window_SizeChanged(object sender, SizeChangedEventArgs e) { if (ready) { UpdateHeaderDensity(); resize.Stop(); resize.Start(); } }
    void Gallery_ScrollChanged(object sender, ScrollChangedEventArgs e) { if (ready && !loading && e.VerticalChange > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 6 && loaded.Count < total) LoadMore(); }
    void Album_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ready && AlbumList.SelectedItem is Album a) OpenAlbum(a.Id); }
    void Details_Click(object sender, RoutedEventArgs e) { details = !details; NotifyAll(); resize.Stop(); resize.Start(); }
    async void Open(Photo p)
    {
        if (p.IsVideo)
        {
            try { await videoLauncher.Open(new(p.Path, CurrentFilter(), p.Id, Heading)); }
            catch (Exception e) { MessageBox.Show(this, "无法打开视频：\n" + e.Message, "视频播放"); }
        }
        else
        {
            var filter = CurrentFilter(); if (filter.Kind == "all") filter = filter with { Kind = "image" };
            int count = catalog.Browse(filter, 0, 1).Total; int index = catalog.BrowseIndex(filter, p.Id);
            new PhotoViewer(i => catalog.Browse(filter, i, 1).Items.FirstOrDefault(), count, Math.Max(0, index)) { Owner = this }.ShowDialog();
        }
    }
    void OpenFocused_Click(object sender, RoutedEventArgs e) { if (focused != null) Open(focused); }
    void Slideshow_Click(object sender, RoutedEventArgs e)
    {
        if (IsAlbumOverview) return;
        var filter = CurrentFilter(); if (filter.Kind == "all") filter = filter with { Kind = "image" };
        int count = catalog.Browse(filter, 0, 1).Total;
        if (count < 2) { SetStatus("当前范围至少需要两张照片才能播放幻灯片。"); return; }
        int index = focused is { IsVideo: false } ? catalog.BrowseIndex(filter, focused.Id) : 0;
        new PhotoViewer(i => catalog.Browse(filter, i, 1).Items.FirstOrDefault(), count, Math.Max(0, index)) { Owner = this, AutoPlay = true }.ShowDialog();
    }
    void CopyPath_Click(object sender, RoutedEventArgs e) { if (focused != null) { Clipboard.SetText(focused.Path); SetStatus("已复制原文件路径"); Notification.Show("已复制文件路径"); } }
    void NewAlbum_Click(object sender, RoutedEventArgs e)
    {
        var name = Dialogs.Prompt(this, "新建相册", "相册名称（只保存引用）"); if (string.IsNullOrWhiteSpace(name)) return;
        string id = catalog.CreateAlbum(name.Trim()); ReloadSidebar(); OpenAlbum(id);
    }
    void AddToAlbum_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selection(); if (selected.Count == 0) return;
        var album = Dialogs.ChooseAlbum(this, catalog.Albums(), name => { string id = catalog.CreateAlbum(name); ReloadSidebar(); return new Album(id, name, 0); }, selected.Count); if (album == null) return;
        catalog.AddToAlbum(album.Id, selected);
        ReloadSidebar();
        SetSelectionMode(false);
        if (focused != null) FocusGalleryPhoto(focused);
        Notify(nameof(MembershipText));
        SetStatus($"已将 {selected.Count} 个引用加入“{album.Name}”，原文件未变");
        Notification.Show($"已将 {selected.Count:N0} 个项目加入“{album.Name}”");
    }
    void RemoveFromAlbum_Click(object sender, RoutedEventArgs e) { if (InAlbum) { catalog.RemoveFromAlbum(albumId, Selection()); ReloadSidebar(); RefreshPage(); SetStatus("已移除相册引用，原文件未变"); } }
    void Favorite_Click(object sender, RoutedEventArgs e) { var selected = Selection(); bool favorite = !selected.All(p => p.Favorite); catalog.Favorite(selected, favorite); RefreshPage(); Notification.Show(favorite ? $"已收藏 {selected.Count:N0} 个项目" : "已取消收藏"); }
    void AlbumMenu(Album a)
    {
        var menu = new ContextMenu();
        Item(menu, "重命名相册", () => { var name = Dialogs.Prompt(this, "重命名相册", "相册名称", a.Name); if (!string.IsNullOrWhiteSpace(name)) { catalog.RenameAlbum(a.Id, name); albumName = name; ReloadSidebar(); NotifyAll(); } });
        Item(menu, "删除相册", () => { if (Dialogs.Confirm(this, "删除这个相册？", $"“{a.Name}”中的照片和视频会保留，只移除这个相册。", "删除相册")) { catalog.DeleteAlbum(a.Id); view = "albums"; ReloadSidebar(); RefreshPage(); } }); menu.IsOpen = true;
    }
    void SelectionMore_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu(); var selected = Selection();
        foreach (var (key, name) in new[] { ("photo", "普通照片"), ("panorama", "全景图"), ("screenshot", "截图") }) Item(menu, "分类为“" + name + "”", () => { catalog.SetKind(selected, key); RefreshPage(); });
        Item(menu, "设置标签（只写索引）", () => { var tags = Dialogs.Prompt(this, "设置标签", "用空格分隔标签", selected.FirstOrDefault()?.Tags ?? "", allowEmpty: true); if (tags != null) { catalog.SetTags(selected, tags); RefreshPage(); } });
        Item(menu, "确认索引日期（不改原文件）", () => { var date = Dialogs.Prompt(this, "索引日期", "格式 yyyy-MM-dd；不改写原图 EXIF", selected.FirstOrDefault()?.DateLabel ?? ""); if (date == null) return; if (!DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) { Error(new Exception("日期格式应为 yyyy-MM-dd")); return; } catalog.SetDate(selected, d); ReloadSidebar(); RefreshPage(); });
        menu.IsOpen = true;
    }
    static void Item(ContextMenu menu, string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    void More_Click(object sender, RoutedEventArgs e) => OpenSettings();
    async void Trash_Click(object sender, RoutedEventArgs e) => await OpenTrash();
    async void DeleteSelection_Click(object sender, RoutedEventArgs e) => await DeleteSelection();
    async Task DeleteSelection()
    {
        if (!CanTrashSelection) return;
        var selected = Selection();
        var confirm = new ConfirmationWindow($"将 {selected.Count:N0} 个项目移入回收站？", "文件会移入来源目录的回收站。可以随时恢复，相册关系也会保留。", "移入回收站", photos: selected) { Owner = this };
        if (confirm.ShowDialog() != true) return;
        var organizer = new FileOrganizer(catalog);
        await RunJob("移入回收站", async (progress, ct) => $"已移入回收站 {await Task.Run(() => organizer.Trash(selected, null, progress, ct), ct):N0} 个项目");
        var trashed = selected.Select(p => catalog.ById(p.Id)).OfType<Photo>().Where(p => p.Status == "trash").ToList();
        bool partial = trashed.Count != selected.Count;
        if (trashed.Count > 0) Notification.Show(partial ? $"已移入 {trashed.Count:N0} 个项目；其余未完成。{StatusText}" : $"已移入回收站 {trashed.Count:N0} 个项目", "撤销", async () =>
        {
            await RunJob("恢复所选项目", async (progress, ct) => $"已恢复 {await Task.Run(() => organizer.Restore(trashed, progress, ct), ct):N0} 个项目");
            if (trashed.All(p => catalog.ById(p.Id)?.Status == "active")) Notification.Show("已恢复到原位置");
        }, error: partial);
    }
    public Task ExportLibrary(string parent, bool includeMedia) => RunJob(includeMedia ? "导出完整媒体库" : "备份相册资料", async (progress, ct) =>
    {
        var folder = await Task.Run(() => LibraryPackage.Export(catalog, parent, includeMedia, progress, ct), ct);
        await Dispatcher.InvokeAsync(() => Dialogs.Text(this, "备份完成", "已保存到：\n" + folder + (includeMedia ? "\n\n复制整个文件夹到新电脑，在设置 → 备份与迁移 → 打开媒体库中选择 library.sqlite。" : "\n\n这份备份不包含原图。照片目录改变后，请在来源目录中重新连接位置。")));
        return "备份完成：" + folder;
    });
    public void LaunchLibrary(string folder)
    {
        if (AppPaths.Data.Equals(folder, StringComparison.OrdinalIgnoreCase)) throw new IOException("这个媒体库已经打开。");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add("--data"); start.ArgumentList.Add(folder);
        Process.Start(start);
    }
    public Task RelinkSource(string oldRoot, string newRoot) => RunJob("校验并重新连接来源目录", async (progress, ct) =>
    {
        int count = await Task.Run(() => catalog.RelinkRoot(oldRoot, newRoot), ct);
        await PreviewPipeline.Enrich(catalog, progress, ct);
        return $"已重新连接 {count:N0} 个项目，相册和标签已保留";
    });
    public async Task RecoverTasks()
    {
        if (busy) return;
        var pending = catalog.Operations();
        if (pending.Count == 0) { Notification.Show("没有未完成任务"); return; }
        var review = new RecoveryWindow(pending) { Owner = this };
        if (review.ShowDialog() != true) return;
        var organizer = new FileOrganizer(catalog);
        await RunJob("恢复文件操作", async (progress, ct) =>
        {
            int count = await Task.Run(() => review.CancelUntouched ? organizer.CancelUntouched() : organizer.ResumePending(progress, ct), ct);
            return $"已处理 {count:N0} 项任务 · 剩余 {catalog.Operations().Count:N0} 项";
        });
        Notification.Show(StatusText, error: catalog.Operations().Count > 0);
    }
    void ClearDate_Click(object sender, RoutedEventArgs e) { ResetDates(); RefreshPage(); }
    void LibraryMore_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (Button)sender };
        Item(menu, "添加来源目录", () => Import_Click(sender, e));
        Item(menu, "重新扫描来源目录", () => _ = RescanSources());
        Item(menu, "设置", () => More_Click(sender, e)); menu.IsOpen = true;
    }
    public void BrowseMaintenance(string target) { view = target; albumId = ""; kind = "all"; mode = "days"; bool prior = ready; ready = false; SearchBox.Clear(); AlbumList.SelectedIndex = -1; KindBox.SelectedIndex = 0; ready = prior; debounce.Stop(); ResetDates(); UpdateMode(); RefreshPage(); }
    public async Task RescanSources() { if (!busy && catalog.Roots.Count > 0) await ScanDirectories(catalog.Roots.Where(r => !catalog.Roots.Any(x => x != r && AppPaths.Within(r, x)))); }
    public string BackupLibrary()
    {
        if (busy) throw new IOException("请等待当前索引任务结束后再备份。");
        var folder = System.IO.Path.Combine(AppPaths.Data, "backups"); Directory.CreateDirectory(folder);
        var path = System.IO.Path.Combine(folder, "时光-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".sqlite");
        catalog.Backup(path); AlbumBackup.Export(catalog, path + ".albums.json"); SetStatus("已备份索引与相册引用：" + path); return path;
    }
    public string ImportAlbumFile(string path)
    {
        if (busy) throw new IOException("请等待当前索引任务结束后再导入。");
        var result = AlbumBackup.Import(catalog, path); SetStatus(result); ReloadSidebar(); RefreshPage(); return result;
    }
    void Pause_Click(object sender, RoutedEventArgs e) { if (!CanPause || activePause == null) return; paused = !paused; if (paused) activePause.Reset(); else activePause.Set(); Notify(nameof(PauseText)); }
    void Stop_Click(object sender, RoutedEventArgs e) { cancellation?.Cancel(); activePause?.Set(); indexer.Pause.Set(); SetStatus("正在安全停止…"); }
    void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!exitRequested && !sessionEnding && HasTray && WindowBehaviorSettings.CloseToTray)
        {
            e.Cancel = true; HideToTray(); return;
        }
        if (!exitRequested && !sessionEnding && lifetimeEnabled && busy)
        {
            e.Cancel = true; Dispatcher.BeginInvoke(() => RequestExit()); return;
        }
        if (busy) { e.Cancel = true; closeWhenDone = true; cancellation?.Cancel(); activePause?.Set(); indexer.Pause.Set(); SetStatus("正在安全停止任务，随后退出…"); return; }
        // All source/index jobs have stopped. Close storage before WPF's final native
        // window teardown, which reproduces an invalid-handle failure on this machine.
        StopTray(); SafeClose?.Invoke(ExitCode);
    }
}
