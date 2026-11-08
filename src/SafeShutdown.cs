using System.Runtime.InteropServices;

namespace Shiguang;

public static class SafeShutdown
{
    [DllImport("kernel32.dll")] static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(nint process, uint code);
    // Used ONLY after jobs have stopped and the DB/lock have explicitly closed.
    // The library calls this before the final WPF native-window teardown to avoid
    // a reproduced invalid-handle failure in native cleanup on this machine. The player
    // also uses it after its media resource closes. Never interrupt an active DB job.
    public static int Finish(int exitCode)
    {
        if (OperatingSystem.IsWindows()) TerminateProcess(GetCurrentProcess(), unchecked((uint)exitCode));
        return exitCode;
    }
}
