using Microsoft.Extensions.DependencyInjection;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;
using MTGProxyBuilder.UI.ViewModels;

namespace MTGProxyBuilder.UI;

/// <summary>
/// Typed bundle of the app's shared, process-wide services. One instance is created by the DI container
/// and injected into the view-models, so every project tab reuses the same Scryfall/MPCFill/image-cache
/// instances instead of each <see cref="MainViewModel"/> newing its own (which spun up duplicate API
/// clients and re-fetched MPCFill sources once per tab). Injecting the bundle keeps the view-model
/// constructors small and testable — pass an AppServices built from fakes in a test.
/// </summary>
public sealed class AppServices
{
    public AppSettingsService Settings { get; }
    public ImageCacheService ImageCache { get; }
    public ProjectSerializationService Serialization { get; }
    public PdfGeneratorService Pdf { get; }
    public ScryfallService Scryfall { get; }
    public YgoProDeckService Ygo { get; }
    public DeckImportService DeckImport { get; }
    public MpcFillSourceManager MpcSources { get; }
    public MpcFillService MpcFill { get; }
    public MpcFillXmlImportService MpcXmlImport { get; }
    public SearchCoordinator Search { get; }
    public ImportCoordinator Import { get; }
    public CacheManager CacheManager { get; }
    public UpdateCheckService UpdateCheck { get; }
    public ImageAdjustmentService ImageAdjust { get; }
    public ScryfallBulkDataService BulkData { get; }

    public AppServices(
        AppSettingsService settings,
        ImageCacheService imageCache,
        ProjectSerializationService serialization,
        PdfGeneratorService pdf,
        ScryfallService scryfall,
        YgoProDeckService ygo,
        DeckImportService deckImport,
        MpcFillSourceManager mpcSources,
        MpcFillService mpcFill,
        MpcFillXmlImportService mpcXmlImport,
        SearchCoordinator search,
        ImportCoordinator import,
        CacheManager cacheManager,
        UpdateCheckService updateCheck,
        ImageAdjustmentService imageAdjust,
        ScryfallBulkDataService bulkData)
    {
        Settings = settings;
        ImageCache = imageCache;
        Serialization = serialization;
        Pdf = pdf;
        Scryfall = scryfall;
        Ygo = ygo;
        DeckImport = deckImport;
        MpcSources = mpcSources;
        MpcFill = mpcFill;
        MpcXmlImport = mpcXmlImport;
        Search = search;
        Import = import;
        CacheManager = cacheManager;
        UpdateCheck = updateCheck;
        ImageAdjust = imageAdjust;
        BulkData = bulkData;
    }
}

/// <summary>Builds the application's DI container.</summary>
public static class ServiceRegistration
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Shared singletons — one instance per process. The dependency graph is resolved by type
        // (e.g. ScryfallService gets the single ImageCacheService), so order here doesn't matter.
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<MpcFillSourceManager>();
        services.AddSingleton<ImageCacheService>();
        services.AddSingleton<ProjectSerializationService>();
        services.AddSingleton<PdfGeneratorService>();
        services.AddSingleton<ScryfallService>();
        services.AddSingleton<YgoProDeckService>();
        services.AddSingleton<MoxfieldService>();
        services.AddSingleton<ArchidektService>();
        services.AddSingleton<DeckImportService>();
        services.AddSingleton<MpcFillService>();
        services.AddSingleton<MpcFillXmlImportService>();
        services.AddSingleton<SearchCoordinator>();
        services.AddSingleton<ImportCoordinator>();
        services.AddSingleton<CacheManager>();
        services.AddSingleton<UpdateCheckService>();
        services.AddSingleton<ImageAdjustmentService>();
        services.AddSingleton<ScryfallBulkDataService>();
        services.AddSingleton<AppServices>();

        services.AddSingleton<IDialogService, AvaloniaDialogService>();

        // The shell is a singleton; each project tab is a fresh MainViewModel (its own undo stack).
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
