using MTGProxyBuilder.Core;

namespace MTGProxyBuilder.Tests.Models;

public class ConstantsTests
{
    [Fact]
    public void DefaultCardWidthMm_Is63()
    {
        Assert.Equal(63f, Constants.DefaultCardWidthMm);
    }

    [Fact]
    public void DefaultCardHeightMm_Is88()
    {
        Assert.Equal(88f, Constants.DefaultCardHeightMm);
    }

    [Fact]
    public void DefaultBleedMm_Is1Mm()
    {
        Assert.Equal(1f, Constants.DefaultBleedMm);
    }

    [Fact]
    public void DefaultDpi_Is300()
    {
        Assert.Equal(300, Constants.DefaultDpi);
    }

    [Fact]
    public void CardDimensions_ArePortrait()
    {
        // Width should be less than height (portrait orientation)
        Assert.True(Constants.DefaultCardWidthMm < Constants.DefaultCardHeightMm);
    }
}
