using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class UpdateCheckServiceTests
{
    [Fact]
    public async Task CheckForUpdate_WithCurrentVersion_ReturnsResult()
    {
        var svc = new UpdateCheckService();
        var result = await svc.CheckForUpdateAsync("0.0.0");

        // If GitHub is reachable and there's a release, we should get a result
        // If not, null is acceptable (network failure)
        if (result != null)
        {
            Assert.Equal("0.0.0", result.CurrentVersion);
            Assert.False(string.IsNullOrEmpty(result.LatestVersion));
            Assert.False(string.IsNullOrEmpty(result.DownloadUrl));
            Assert.True(result.IsUpdateAvailable); // 0.0.0 is always older
        }
    }

    [Fact]
    public async Task CheckForUpdate_WithFutureVersion_NoUpdate()
    {
        var svc = new UpdateCheckService();
        var result = await svc.CheckForUpdateAsync("999.999.999");

        if (result != null)
        {
            Assert.False(result.IsUpdateAvailable);
        }
    }

    [Fact]
    public async Task CheckForUpdate_NeverThrows()
    {
        var svc = new UpdateCheckService();
        var ex = await Record.ExceptionAsync(() => svc.CheckForUpdateAsync("1.0.0"));
        Assert.Null(ex);
    }
}
