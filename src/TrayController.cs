using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Shiguang;

public sealed class TrayController : IDisposable
{
    readonly MainWindow main;
    readonly Forms.NotifyIcon icon;
    readonly System.Drawing.Icon image;
    readonly Forms.ContextMenuStrip menu = new();
    readonly Forms.ToolStripMenuItem status = new() { Enabled = false };
    readonly Forms.ToolStripMenuItem pause;
    readonly Forms.ToolStripMenuItem progress;
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(600) };
    bool notified, disposed, wasBusy;
    string lastText = "";
    public bool IsAvailable => !disposed && icon.Visible;
    public TrayController(MainWindow main)
    {
        this.main = main;
        using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/拾光照片;component/Assets/Shiguang.ico"))!.Stream;
        using var original = new System.Drawing.Icon(resource); image = (System.Drawing.Icon)original.Clone();
        icon = new Forms.NotifyIcon { Icon = image, Text = "时光相册", ContextMenuStrip = menu };
        menu.Items.Add("打开时光相册", null, (_, _) => Dispatch(main.RestoreFromTray));
        menu.Items.Add(status);
        progress = new Forms.ToolStripMenuItem("查看任务进度", null, (_, _) => Dispatch(main.RestoreFromTray)); menu.Items.Add(progress);
        pause = new Forms.ToolStripMenuItem("暂停任务", null, (_, _) => Dispatch(main.ToggleTaskPause)); menu.Items.Add(pause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("设置", null, (_, _) => Dispatch(() => { main.RestoreFromTray(); main.OpenSettings("general"); }));
        menu.Items.Add("退出程序", null, (_, _) => Dispatch(() => main.RequestExit()));
        menu.Opening += (_, _) => Update();
        icon.DoubleClick += (_, _) => Dispatch(main.RestoreFromTray);
        icon.BalloonTipClicked += (_, _) => Dispatch(main.RestoreFromTray);
        icon.Visible = true;
        refresh.Tick += (_, _) => Update(); refresh.Start(); Update();
    }
    void Dispatch(Action run) { if (!disposed && !main.Dispatcher.HasShutdownStarted) main.Dispatcher.BeginInvoke(run); }
    void Update()
    {
        if (disposed) return;
        string text = main.IsBusy || wasBusy ? "时光相册 · " + main.StatusText : "时光相册 · 当前无后台任务";
        text = text.Length > 63 ? text[..62] + "…" : text;
        if (text != lastText) { icon.Text = text; lastText = text; }
        status.Text = main.IsBusy ? main.StatusText.Length > 45 ? main.StatusText[..45] + "…" : main.StatusText : "当前没有后台任务";
        pause.Enabled = main.CanPause; pause.Text = main.PauseText == "继续" ? "继续任务" : "暂停任务";
        progress.Enabled = main.IsBusy;
        if (wasBusy && !main.IsBusy && main.IsInBackground)
            icon.ShowBalloonTip(4000, "时光相册 · 后台任务已结束", main.StatusText.Length > 200 ? main.StatusText[..200] : main.StatusText, Forms.ToolTipIcon.Info);
        wasBusy = main.IsBusy;
    }
    public void NotifyHidden()
    {
        Update();
        if (notified) return; notified = true;
        icon.ShowBalloonTip(4000, "时光相册在后台运行", main.IsBusy ? main.PauseText == "继续" ? "任务已暂停并在后台保留。双击托盘图标可返回。" : "当前任务继续进行。双击托盘图标可查看进度；右键菜单可退出程序。" : "双击托盘图标可返回。退出请使用托盘或窗口菜单中的“退出程序”。", Forms.ToolTipIcon.Info);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; refresh.Stop(); icon.Visible = false; icon.Dispose(); menu.Dispose(); image.Dispose();
    }
}
