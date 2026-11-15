using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Shiguang;

public partial class MainWindow
{
    void AlbumCover_Click(object sender, RoutedEventArgs e)
    {
        if (!InAlbum || Albums.FirstOrDefault(a => a.Id == albumId) is not { } album) return;
        var photo = focused;
        var menu = new ContextMenu { PlacementTarget = (Button)sender, Placement = PlacementMode.Bottom };
        bool usable = !IsSelecting && !busy && photo is { Status: "active", IsVideo: false, PreviewState: "ready", Error.Length: 0 } &&
            File.Exists(photo.ThumbPath);
        var set = new MenuItem
        {
            Header = "将当前照片设为封面",
            IsEnabled = usable,
            ToolTip = usable ? photo!.Name : "退出多选并选择一张预览可用的照片",
        };
        ToolTipService.SetShowOnDisabled(set, true);
        set.Click += (_, _) => SaveCover(album.Id, photo);
        menu.Items.Add(set);
        var reset = new MenuItem { Header = "恢复自动封面", IsEnabled = !busy && album.CoverPhotoId.Length > 0 };
        reset.Click += (_, _) => SaveCover(album.Id, null);
        menu.Items.Add(reset);
        var button = (Button)sender;
        button.ContextMenu = menu;
        menu.Closed += (_, _) => button.ClearValue(ContextMenuProperty);
        menu.IsOpen = true;
    }

    void SaveCover(string album, Photo? photo)
    {
        if (busy || !InAlbum || albumId != album || (photo != null && IsSelecting)) return;
        try
        {
            if (photo == null) catalog.ResetAlbumCover(album);
            else if (!catalog.TrySetAlbumCover(album, photo.Id))
            {
                Notification.Show("这张照片暂不能作为封面。请选择相册内预览可用的照片。", error: true);
                return;
            }
            ReloadSidebar();
            Notification.Show(photo == null ? "已恢复自动封面" : $"已设为封面：{photo.Name}");
        }
        catch (IOException error)
        {
            LibraryService.Log("保存相册封面", "", error);
            Notification.Show("未能保存相册封面，请稍后重试。", error: true);
        }
    }
}
