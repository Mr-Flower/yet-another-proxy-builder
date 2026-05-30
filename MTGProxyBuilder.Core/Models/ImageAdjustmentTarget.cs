namespace MTGProxyBuilder.Core.Models
{
    /// <summary>
    /// Which cards an image adjustment should be applied to, chosen in the
    /// adjustment dialog. Fork-specific.
    /// </summary>
    public enum ImageAdjustmentTarget
    {
        ThisCard = 0,
        AllScryfall = 1,
        AllMpcFill = 2,
        AllBoth = 3
    }
}
