namespace MTGProxyBuilder.Core
{
    public static class Constants
    {
        public const float DefaultCardWidthMm = 63f;
        public const float DefaultCardHeightMm = 88f;
        // Default trim margin for new projects. The cut line sits on the card edge; this is just how
        // much bleed is kept around it (0 .. MpcBleedMm). Tunable per-project from the Layout panel.
        public const float DefaultBleedMm = 1f;
        public const int DefaultDpi = 300;

        /// <summary>
        /// MakePlayingCards full-bleed standard: 1/8 inch of bleed per side. On MPC's poker template
        /// (822×1122 px @ 300 dpi, trimmed card 750×1050 px = 2.5×3.5") that is 36 px per side =
        /// 36/300" = 0.12" ≈ 3.048 mm. MPCFill art is authored to this template, so every bleed cell
        /// is sized to it: MPCFill is drawn whole and Scryfall scans are edge-extended to the SAME
        /// 1/8", giving both sources identical card zoom and a matching cut line. Fixed (not
        /// user-tunable) because it must equal MPCFill's baked-in margin.
        /// </summary>
        public const float MpcBleedMm = 3.048f;
    }
}
