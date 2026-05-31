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
        /// This is a Magic: The Gathering proxy builder, so the only card size is the standard
        /// 63 x 88 mm. (Custom dimensions can still be set via the Width/Height fields.)
        /// </summary>
        public static readonly List<CardSizePreset> BuiltInPresets = new()
        {
            new("Magic: The Gathering", 63f, 88f),
        };
    }
}
