using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Shiguang;

// The file lock remains the authority. This registered message only restores its window.
public sealed class LibraryInstance : IDisposable
{
    readonly string identity;
    readonly string dataDirectory;
    readonly uint message;
    HwndSource? source;
    HwndSourceHook? hook;
    nint handle;
    FileSystemWatcher? activationWatcher;
    string? activationDirectory;
    bool disposed;
    public LibraryInstance(string data)
    {
        dataDirectory = Path.GetFullPath(data);
        identity = Name(data); message = RegisterWindowMessage(identity);
        if (message == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    static string Name(string data) => "Shiguang.Restore." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())))[..24];
    public void OnRestore(Window window, Action restore)
    {
        void Attach()
        {
            if (disposed || handle != 0) return;
            handle = new WindowInteropHelper(window).Handle; source = HwndSource.FromHwnd(handle);
            if (!SetProp(handle, identity, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            hook = (nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
            {
                if ((uint)msg != message || disposed) return 0;
                handled = true; window.Dispatcher.BeginInvoke(restore); return 1;
            };
            source?.AddHook(hook);
            // A launch from another Windows isolation boundary may not enumerate our HWND.
            // The library directory already has the same access boundary as its session lock.
            activationDirectory = Path.Combine(Path.GetFullPath(dataDirectory), ".activation");
            Directory.CreateDirectory(activationDirectory);
            activationWatcher = new FileSystemWatcher(activationDirectory, "*.request") { NotifyFilter = NotifyFilters.FileName };
            void Queue(string path) => window.Dispatcher.BeginInvoke(() =>
            {
                if (disposed || !File.Exists(path) || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)) return;
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromMinutes(2)) { File.Delete(path); return; }
                    restore(); File.Move(path, Path.ChangeExtension(path, ".ack"), true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            });
            activationWatcher.Created += (_, e) => Queue(e.FullPath);
            activationWatcher.Renamed += (_, e) => Queue(e.FullPath);
            activationWatcher.EnableRaisingEvents = true;
            foreach (var pending in Directory.EnumerateFiles(activationDirectory, "*.request")) Queue(pending);
        }
        window.SourceInitialized += (_, _) => Attach();
        if (new WindowInteropHelper(window).Handle != 0) Attach();
    }
    public static bool TryRestoreExisting(string data)
    {
        string identity = Name(data); uint message = RegisterWindowMessage(identity);
        if (message == 0) return false;
        var until = DateTime.UtcNow.AddSeconds(3);
        do
        {
            nint target = 0;
            EnumWindows((window, _) => { if (GetProp(window, identity) == 0) return true; target = window; return false; }, 0);
            if (target != 0)
            {
                GetWindowThreadProcessId(target, out uint processId); AllowSetForegroundWindow(processId);
                if (SendMessageTimeout(target, message, 0, 0, 0x0002, 2500, out var accepted) != 0 && accepted == 1) return true;
                break;
            }
            Thread.Sleep(60);
        } while (DateTime.UtcNow < until);
        return TryRestoreThroughLibrary(data);
    }
    public static bool TryRestoreThroughLibrary(string data)
    {
        string directory = Path.Combine(Path.GetFullPath(data), ".activation");
        string request = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".request");
        string acknowledgment = Path.ChangeExtension(request, ".ack");
        try
        {
            Directory.CreateDirectory(directory);
            using (File.Open(request, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
            var until = DateTime.UtcNow.AddSeconds(4);
            do { if (File.Exists(acknowledgment)) return true; Thread.Sleep(60); } while (DateTime.UtcNow < until);
            return false;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally
        {
            try { File.Delete(request); File.Delete(acknowledgment); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        activationWatcher?.Dispose();
        if (source != null && hook != null) source.RemoveHook(hook);
        if (handle != 0) RemoveProp(handle, identity);
    }
    delegate bool EnumWindow(nint window, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetProp(nint window, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint GetProp(nint window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint RemoveProp(nint window, string name);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
}
