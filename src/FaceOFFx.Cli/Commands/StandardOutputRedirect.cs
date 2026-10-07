using System.Runtime.InteropServices;

namespace FaceOFFx.Cli.Commands;

internal sealed class StandardOutputRedirect : IDisposable
{
    private readonly int _savedStdout;
    private bool _disposed;
    private StandardOutputRedirect(int savedStdout) => _savedStdout = savedStdout;

    public static StandardOutputRedirect? ToNull()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) return null;
        var saved = Dup(1);
        if (saved == -1) throw Error("duplicate stdout");
        var nullDevice = Open("/dev/null", 0x0001);
        if (nullDevice == -1)
        {
            var error = Error("open /dev/null");
            Close(saved);
            throw error;
        }
        if (Dup2(nullDevice, 1) == -1)
        {
            var error = Error("redirect stdout");
            Close(nullDevice);
            Close(saved);
            throw error;
        }
        Close(nullDevice);
        return new StandardOutputRedirect(saved);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Dup2(_savedStdout, 1);
        Close(_savedStdout);
        _disposed = true;
    }

    private static InvalidOperationException Error(string operation) =>
        new($"Unable to {operation} for JSON output: errno {Marshal.GetLastPInvokeError()}.");
    [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static extern int Dup(int fd);
    [DllImport("libc", EntryPoint = "dup2", SetLastError = true)]
    private static extern int Dup2(int oldFd, int newFd);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);
}
