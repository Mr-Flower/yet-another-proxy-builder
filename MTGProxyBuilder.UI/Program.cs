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
            DiagnoseAndRegisterSkia();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void DiagnoseAndRegisterSkia()
    {
        var baseDir = AppContext.BaseDirectory;
        var bundled = Path.Combine(baseDir, "libSkiaSharp.so");

        Err($"[SKIA] AppContext.BaseDirectory = {baseDir}");
        Err($"[SKIA] bundled path             = {bundled}");
        Err($"[SKIA] File.Exists              = {File.Exists(bundled)}");

        if (!File.Exists(bundled))
        {
            Err("[SKIA] bundled lib missing — falling back to default resolution");
            return;
        }

        // 1. Try to load it directly and report result.
        var ok = NativeLibrary.TryLoad(bundled, out var handle);
        Err($"[SKIA] NativeLibrary.TryLoad    = {ok}  handle = {handle}");

        if (!ok)
        {
            try   { NativeLibrary.Load(bundled); }
            catch (Exception ex) { Err($"[SKIA] Load threw: {ex.Message}"); }
        }

        // 2. Also try via dlopen so we can compare.
        var dlopenHandle = TryDlopen(bundled, RTLD_LAZY | RTLD_GLOBAL);
        Err($"[SKIA] dlopen(RTLD_GLOBAL)      = {dlopenHandle}");

        // 3. Register SetDllImportResolver for SkiaSharp and Avalonia.Skia.
        //    Assembly.Load may trigger a SkiaSharp module-initialiser that
        //    registers its own resolver first; we try anyway, but log if it
        //    was already claimed.
        foreach (var asmName in new[] { "SkiaSharp", "Avalonia.Skia" })
        {
            try
            {
                var asm = Assembly.Load(asmName);
                Err($"[SKIA] Assembly.Load({asmName}) OK, registering resolver...");
                NativeLibrary.SetDllImportResolver(asm, (lib, _, _) =>
                {
                    if (lib is "libSkiaSharp" or "SkiaSharp")
                    {
                        NativeLibrary.TryLoad(bundled, out var h);
                        Err($"[SKIA] resolver hit: lib={lib} -> handle={h}");
                        return h;
                    }
                    return IntPtr.Zero;
                });
                Err($"[SKIA] SetDllImportResolver({asmName}) completed");
            }
            catch (Exception ex)
            {
                Err($"[SKIA] Assembly.Load({asmName}) failed: {ex.Message}");
            }
        }
    }

    private static IntPtr TryDlopen(string path, int flags)
    {
        try { return Dlopen_libdl (path, flags); } catch { }
        try { return Dlopen_libdl2(path, flags); } catch { }
        return IntPtr.Zero;
    }

    [DllImport("libdl",      EntryPoint = "dlopen")] static extern IntPtr Dlopen_libdl (string p, int f);
    [DllImport("libdl.so.2", EntryPoint = "dlopen")] static extern IntPtr Dlopen_libdl2(string p, int f);

    private const int RTLD_LAZY   = 0x0001;
    private const int RTLD_GLOBAL = 0x0100;

    private static void Err(string s) => Console.Error.WriteLine(s);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
