using Avalonia;
using System.Runtime.InteropServices;

namespace MTGProxyBuilder.UI;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // On Linux, pin the bundled libSkiaSharp.so with RTLD_GLOBAL before Avalonia
        // initialises so .NET's P/Invoke resolver always gets the bundled handle.
        if (OperatingSystem.IsLinux())
            PreloadBundledSkia();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void PreloadBundledSkia()
    {
        var lib = Path.Combine(AppContext.BaseDirectory, "libSkiaSharp.so");
        if (!File.Exists(lib)) return;

        // Try RTLD_GLOBAL dlopen so any subsequent dlopen("libSkiaSharp") returns this handle.
        // Two entries: on modern glibc (>=2.34, Arch/CachyOS/Ubuntu 24+) only libdl.so.2
        // exists as a standalone file; older distros ship a plain libdl.so.
        if (TryDlopen(lib, RTLD_LAZY | RTLD_GLOBAL) == IntPtr.Zero)
        {
            // Last resort: use .NET's own loader (no RTLD_GLOBAL, but better than nothing).
            System.Runtime.InteropServices.NativeLibrary.TryLoad(lib, out _);
        }
    }

    private static IntPtr TryDlopen(string path, int flags)
    {
        try { return DlopenLibdl(path, flags); }     // glibc < 2.34 / most distros
        catch { }
        try { return DlopenLibdlSo2(path, flags); }  // glibc >= 2.34 (Arch, CachyOS, Ubuntu 24+)
        catch { }
        return IntPtr.Zero;
    }

    [DllImport("libdl",      EntryPoint = "dlopen")] static extern IntPtr DlopenLibdl(string p, int f);
    [DllImport("libdl.so.2", EntryPoint = "dlopen")] static extern IntPtr DlopenLibdlSo2(string p, int f);

    private const int RTLD_LAZY   = 0x0001;
    private const int RTLD_GLOBAL = 0x0100;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
