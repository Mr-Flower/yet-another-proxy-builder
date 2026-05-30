namespace MTGProxyBuilder.Core.Models
{
    /// <summary>
    /// Where a card's artwork originally came from. Used to apply image adjustments
    /// in bulk, filtered by macro-source (Scryfall / MPCFill).
    /// Fork-specific.
    /// </summary>
    public enum CardSource
    {
        Unknown = 0,
        Scryfall = 1,
        MpcFill = 2
    }
}
