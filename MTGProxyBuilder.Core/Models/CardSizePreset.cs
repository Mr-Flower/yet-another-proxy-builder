namespace MTGProxyBuilder.Core.Models
{
    public class CardSizePreset
    {
        public string Name { get; }
        public float WidthMm { get; }
        public float HeightMm { get; }

        public CardSizePreset(string name, float widthMm, float heightMm)
        {
            Name = name;
            WidthMm = widthMm;
            HeightMm = heightMm;
        }

        public override string ToString() => $"{Name}  ({WidthMm} x {HeightMm} mm)";

        /// <summary>
        /// Card-size presets covering the supported games, picked from the layout's size dropdown.
        /// The 63 x 88 (Magic/poker) and 59 x 86 (Yu-Gi-Oh!/Japanese) sizes are also applied
        /// automatically when the Add-Cards game selector switches between Magic and Yu-Gi-Oh!.
        /// </summary>
        public static readonly List<CardSizePreset> BuiltInPresets = new()
        {
            new("Magic / Poker (Pokémon, Lorcana…)", 63f, 88f),
            new("Yu-Gi-Oh! / Japanese (Vanguard…)", 59f, 86f),
            new("Bridge", 57f, 89f),
            new("Mini American", 41f, 63f),
            new("Mini European", 44f, 68f),
            new("Tarot", 70f, 120f),
            new("Oversized (Commander)", 89f, 127f),
        };
    }
}
