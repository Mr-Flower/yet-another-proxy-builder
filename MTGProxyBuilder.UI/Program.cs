using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;

namespace MTGProxyBuilder.UI;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // On Linux, guarantee the bundled libSkiaSharp.so is used instead of any
        // system-installed copy (e.g. Arch/CachyOS libSkiaSharp.so.88).
        // Two layers of defence:
        //   1. RTLD_GLOBAL dlopen so the handle is shared by all subsequent callers.
        //   2. SetDllImportResolver so .NET's P/Invoke resolver always returns the
        //      bundled handle, bypassing ldconfig / LD_LIBRARY_PATH lookups entirely.
        if (OperatingSystem.IsLinux())
            RegisterBundledSkiaResolver();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void RegisterBundledSkiaResolver()
    {
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "libSkiaSharp.so");
        if (!File.Exists(bundledPath))
            return;

        // Layer 1: preload with RTLD_GLOBAL so native callers (e.g. fontconfig
        // loaded by SkiaSharp) all share the same handle.
        try { Dlopen_libdl (bundledPath, RTLD_LAZY | RTLD_GLOBAL); }
        catch
        {
            try { Dlopen_libdl2(bundledPath, RTLD_LAZY | RTLD_GLOBAL); }
            catch { NativeLibrary.TryLoad(bundledPath, out _); }
        }

        // Layer 2: intercept .NET's P/Invoke resolver so every DllImport targeting
        // "libSkiaSharp" or "SkiaSharp" (from SkiaSharp or Avalonia.Skia) is
        // redirected to the bundled path, regardless of ldconfig order.
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
            catch { /* assembly not loaded — skip */ }
        }
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
