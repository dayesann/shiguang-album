using System.Windows;
using Microsoft.Win32;

namespace Shiguang;

public static class FolderPicker
{
    public static Task<string?> ChooseAsync(Window owner, string initial)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择照片或视频所在的文件夹",
            InitialDirectory = Directory.Exists(initial) ? initial : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Multiselect = false, AddToRecent = false,
            ClientGuid = new Guid("204a347c-bcf7-43b8-8cdb-bd84ed572132")
        };
        if (dialog.ShowDialog(owner) != true) return Task.FromResult<string?>(null);
        string folder = Path.GetFullPath(dialog.FolderName);
        if (!Directory.Exists(folder)) throw new IOException("选中的目录当前不可访问，请重新选择。");
        return Task.FromResult<string?>(folder);
    }
}
