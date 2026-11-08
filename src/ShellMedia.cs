using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Shiguang;

// Invoked exclusively by the disposable preview process, never by the gallery UI.
public static class ShellMedia
{
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey(Guid guid, uint id) { public Guid Guid = guid; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public ulong Number;
        [FieldOffset(8)] public nint Pointer;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key); void GetValue(ref PropertyKey key, out PropVariant value); void SetValue(ref PropertyKey key, ref PropVariant value); void Commit();
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeSize { public int Width, Height; }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { void GetImage(NativeSize size, uint flags, out nint bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] static extern void SHGetPropertyStoreFromParsingName(string path, nint bind, uint flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] static extern void SHCreateItemFromParsingName(string path, nint bind, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    public static Photo Read(Photo p)
    {
        p.MediaKind = "video"; p.ContentKind = "video"; p.Error = "";
        IPropertyStore? store = null; IShellItemImageFactory? factory = null; nint bitmap = 0;
        try
        {
            var iid = typeof(IPropertyStore).GUID; SHGetPropertyStoreFromParsingName(p.Path, 0, 0, ref iid, out store);
            ulong Read(string guid, uint id)
            {
                var key = new PropertyKey(new Guid(guid), id); store.GetValue(ref key, out var value);
                try { return value.Type is 19 or 21 or 64 ? value.Number : 0; } finally { PropVariantClear(ref value); }
            }
            p.Duration = Read("64440490-4C8B-11D1-8B70-080036B11A03", 3) / 10000000d;
            p.Width = (int)Read("64440491-4C8B-11D1-8B70-080036B11A03", 3); p.Height = (int)Read("64440491-4C8B-11D1-8B70-080036B11A03", 4);
            ulong encoded = Read("2E4B640D-5019-46D8-8881-55414CC5CAA0", 100);
            if (encoded > 0 && p.DateSource != "手动确认") { p.Taken = DateTime.FromFileTimeUtc((long)encoded).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); p.DateSource = "媒体日期"; }
            iid = typeof(IShellItemImageFactory).GUID; SHCreateItemFromParsingName(p.Path, 0, ref iid, out factory);
            factory.GetImage(new NativeSize { Width = 640, Height = 400 }, 0x8, out bitmap); // SIIGBF_THUMBNAILONLY; never substitute a file icon.
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            PreviewWorker.Save(source, p.ThumbPath);
            var metrics = Imaging.Analyze(source);
            p.VisualHash = metrics.Hash.ToString("X16"); p.Sharpness = metrics.Sharpness; p.Brightness = metrics.Brightness;
            if (p.Duration <= 0) p.Error = "无法读取视频时长；可用系统播放器打开";
        }
        catch (Exception e) { p.Error = "视频预览不可用：" + e.Message; }
        finally { if (bitmap != 0) DeleteObject(bitmap); if (factory != null) Marshal.ReleaseComObject(factory); if (store != null) Marshal.ReleaseComObject(store); }
        return p;
    }
}
