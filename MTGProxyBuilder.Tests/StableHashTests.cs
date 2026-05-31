using MTGProxyBuilder.Core;

namespace MTGProxyBuilder.Tests;

public class StableHashTests
{
    [Fact]
    public void Hex_IsDeterministic_AndDistinct()
    {
        // Must be stable for the same input (so on-disk caches match across app launches) and
        // distinguish different inputs.
        Assert.Equal(StableHash.Hex("/cache/mpc_abc.jpg"), StableHash.Hex("/cache/mpc_abc.jpg"));
        Assert.NotEqual(StableHash.Hex("a"), StableHash.Hex("b"));
        Assert.Equal(8, StableHash.Hex("anything").Length);
    }
}
