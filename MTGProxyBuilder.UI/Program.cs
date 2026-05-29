using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;

namespace MTGProxyBuilder.UI;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsLinux())
            RegisterBundledSkiaResolver();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // On Arch/CachyOS (and any distro with libSkiaSharp.so.88 installed system-wide),
    // the OS dynamic linker or .NET's own native-library resolver may resolve
    // "libSkiaSharp" to the system copy instead of the bundled v119, even when
    // LD_PRELOAD and LD_LIBRARY_PATH are set correctly in the AppRun script.
    //
    // NativeLibrary.SetDllImportResolver intercepts P/Invoke resolution *before*
    // dlopen is ever called, giving us an unconditional full-path load of the
    // bundled library.  This must run before any SkiaSharp type is first accessed.
    private static void RegisterBundledSkiaResolver()
    {
        var baseDir = AppContext.BaseDirectory;
        var bundledPath = Path.Combine(baseDir, "libSkiaSharp.so");

        if (!File.Exists(bundledPath))
            return;

        // Preload with RTLD_GLOBAL so the handle is shared across all dlopen callers.
        PreloadRtldGlobal(bundledPath);

        // Register a DllImportResolver for both SkiaSharp and Avalonia.Skia so that
        // every P/Invoke targeting "libSkiaSharp" (regardless of which assembly issues
        // it) is redirected to the bundled copy.
        foreach (var asmName in new[] { "SkiaSharp", "Avalonia.Skia" })
        {
            try
            {
                var asm = Assembly.Load(asmName);
                NativeLibrary.SetDllImportResolver(asm, (lib, _, _) =>
                {
                    if (lib is "libSkiaSharp" or "SkiaSharp")
                    {
                        NativeLibrary.TryLoad(bundledPath, out var h);
                        return h;
                    }
                    return IntPtr.Zero;
                });
            }
            catch { /* assembly not present — proceed with default resolution */ }
        }
    }

    private static void PreloadRtldGlobal(string path)
    {
        // Try both libdl variants (glibc < 2.34 and glibc >= 2.34 / Arch).
        try { Dlopen_libdl(path, RTLD_LAZY | RTLD_GLOBAL); return; } catch { }
        try { Dlopen_libdl2(path, RTLD_LAZY | RTLD_GLOBAL); return; } catch { }
        // Last resort: let .NET load it without RTLD_GLOBAL.
        NativeLibrary.TryLoad(path, out _);
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
