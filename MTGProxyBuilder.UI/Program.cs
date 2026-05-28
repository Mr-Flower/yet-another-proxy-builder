using Avalonia;
using System.Runtime.InteropServices;

namespace MTGProxyBuilder.UI;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // On Linux, force-load the bundled libSkiaSharp.so with RTLD_GLOBAL before Avalonia
        // initializes. This prevents the system-installed older version from being picked up,
        // even when LD_PRELOAD is ignored (e.g. nosuid FUSE mounts used by AppImage runtimes).
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var lib = Path.Combine(AppContext.BaseDirectory, "libSkiaSharp.so");
                if (File.Exists(lib))
                    LinuxDlopen(lib, RTLD_LAZY | RTLD_GLOBAL);
            }
            catch { /* best-effort; if it fails we proceed and may get the version error */ }
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    [DllImport("libdl", EntryPoint = "dlopen")]
    private static extern IntPtr LinuxDlopen(string path, int flags);

    private const int RTLD_LAZY   = 0x0001;
    private const int RTLD_GLOBAL = 0x0100;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
