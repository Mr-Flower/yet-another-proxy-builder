using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;

namespace MTGProxyBuilder.UI;

internal sealed class Program
{
    // Diagnostics file – written with AutoFlush so output survives a SIGABRT crash.
    private static StreamWriter? _diag;

    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsLinux())
        {
            _diag = new StreamWriter("/tmp/tcg-skia-diag.txt", append: false) { AutoFlush = true };
            DiagnoseAndRegisterSkia();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void DiagnoseAndRegisterSkia()
    {
        var baseDir = AppContext.BaseDirectory;
        var bundled = Path.Combine(baseDir, "libSkiaSharp.so");

        Log($"AppContext.BaseDirectory = {baseDir}");
        Log($"bundled path             = {bundled}");
        Log($"File.Exists              = {File.Exists(bundled)}");

        if (!File.Exists(bundled))
        {
            Log("bundled lib MISSING – falling back to default resolution");
            return;
        }

        // 1. Direct NativeLibrary.TryLoad
        var ok = NativeLibrary.TryLoad(bundled, out var handle);
        Log($"NativeLibrary.TryLoad    = {ok}  handle = {handle}");
        if (!ok)
        {
            try   { NativeLibrary.Load(bundled); }
            catch (Exception ex) { Log($"NativeLibrary.Load threw : {ex.Message}"); }
        }

        // 2. dlopen(RTLD_GLOBAL)
        var dh = TryDlopen(bundled, RTLD_LAZY | RTLD_GLOBAL);
        Log($"dlopen(RTLD_GLOBAL)      = {dh}");

        // 3. SetDllImportResolver
        foreach (var asmName in new[] { "SkiaSharp", "Avalonia.Skia" })
        {
            try
            {
                var asm = Assembly.Load(asmName);
                Log($"Assembly.Load({asmName}) OK");
                NativeLibrary.SetDllImportResolver(asm, (lib, _, _) =>
                {
                    if (lib is "libSkiaSharp" or "SkiaSharp")
                    {
                        NativeLibrary.TryLoad(bundled, out var h);
                        Log($"  resolver hit: lib={lib} -> handle={h}");
                        return h;
                    }
                    return IntPtr.Zero;
                });
                Log($"SetDllImportResolver({asmName}) registered");
            }
            catch (Exception ex)
            {
                Log($"Assembly.Load({asmName}) failed: {ex.Message}");
            }
        }

        Log("DiagnoseAndRegisterSkia complete");
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

    private static void Log(string s)
    {
        var line = $"[SKIA] {s}";
        _diag?.WriteLine(line);        // to file (auto-flushed, survives crash)
        Console.Error.WriteLine(line); // to stderr (may be lost on SIGABRT)
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
