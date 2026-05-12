using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class AppSettingsServiceTests
{
    [Fact]
    public void Settings_ObjectHasExpectedDefaults()
    {
        // Test the AppSettings class defaults (not the file on disk which may have been modified)
        var settings = new MTGProxyBuilder.Core.Services.AppSettings();
        Assert.Equal("TOKEN", settings.DefaultTokenText);
        Assert.Equal(1.5f, settings.DefaultBleedMm);
        Assert.Equal("Magic: The Gathering", settings.DefaultCardSizePreset);
        Assert.Equal("A4", settings.DefaultPagePreset);
        Assert.True(settings.CheckForUpdates);
    }

    [Fact]
    public void Settings_CanBeModified()
    {
        var svc = new AppSettingsService();
        svc.Settings.DefaultTokenText = "PROXY";
        svc.Settings.DefaultBleedMm = 3f;
        svc.Settings.DefaultPagePreset = "Letter";
        svc.Settings.CheckForUpdates = false;

        Assert.Equal("PROXY", svc.Settings.DefaultTokenText);
        Assert.Equal(3f, svc.Settings.DefaultBleedMm);
        Assert.Equal("Letter", svc.Settings.DefaultPagePreset);
        Assert.False(svc.Settings.CheckForUpdates);
    }

    [Fact]
    public void Save_DoesNotThrow()
    {
        var svc = new AppSettingsService();
        var ex = Record.Exception(() => svc.Save());
        Assert.Null(ex);
    }
}
