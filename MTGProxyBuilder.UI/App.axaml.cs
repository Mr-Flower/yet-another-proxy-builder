using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MTGProxyBuilder.UI.ViewModels;
using Serilog;

namespace MTGProxyBuilder.UI;

public class App : Application
{
    /// <summary>The application's DI container, built once at startup.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigureLogging();
        Services = ServiceRegistration.Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += (_, _) =>
            {
                Log.Information("App shutting down");
                Log.CloseAndFlush();
            };
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Structured logging to a daily rolling file under the app data dir, plus global handlers so an
    // unhandled exception is recorded instead of vanishing. (Avalonia has no WPF-style
    // DispatcherUnhandledException; AppDomain + TaskScheduler cover background and UI-thread faults.)
    private static void ConfigureLogging()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "YetAnotherProxyBuilder", "Logs");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDir, "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Log.Fatal(e.ExceptionObject as Exception, "Unhandled AppDomain exception");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error(e.Exception, "Unobserved task exception");
                e.SetObserved();
            };

            Log.Information("App starting — version {Version}, OS {OS}",
                MainViewModel.GetAppVersion(), Environment.OSVersion);
        }
        catch
        {
            // Logging must never prevent startup; fall back to the silent logger.
        }
    }
}
