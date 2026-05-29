using System.Runtime.InteropServices;
using Avalonia;

namespace MTGProxyBuilder.UI;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // On Linux, preload the bundled libSkiaSharp.so with RTLD_GLOBAL before
        // Avalonia initialises, so .NET's P/Invoke resolver always gets the bundled
        // handle even on systems that have a system libSkiaSharp.so installed.
        if (OperatingSystem.IsLinux())
            PreloadBundledSkia();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void PreloadBundledSkia()
    {
        var lib = Path.Combine(AppContext.BaseDirectory, "libSkiaSharp.so");
        if (!File.Exists(lib)) return;

        try { Dlopen_libdl (lib, RTLD_LAZY | RTLD_GLOBAL); return; } catch { }
        try { Dlopen_libdl2(lib, RTLD_LAZY | RTLD_GLOBAL); return; } catch { }
        System.Runtime.InteropServices.NativeLibrary.TryLoad(lib, out _);
    }

    [DllImport("libdl",      EntryPoint = "dlopen")] static extern IntPtr Dlopen_libdl (string p, int f);
    [DllImport("libdl.so.2", EntryPoint = "dlopen")] static extern IntPtr Dlopen_libdl2(string p, int f);

    private const int RTLD_LAZY   = 0x0001;
    private const int RTLD_GLOBAL = 0x0100;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
