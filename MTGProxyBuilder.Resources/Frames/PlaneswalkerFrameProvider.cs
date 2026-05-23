namespace MTGProxyBuilder.Resources.Frames
{
    public class PlaneswalkerFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.Planeswalker.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"planeswalkerFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"planeswalkerFrame{color}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"planeswalkerMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"planeswalkerMask{name}");
        }

        public Stream? GetAbilityLine(string variant)
        {
            return GetImage($"abilityLine{variant}");
        }

        public byte[]? GetAbilityLineBytes(string variant)
        {
            return GetImageBytes($"abilityLine{variant}");
        }

        public Stream? GetLoyaltyIcon(string type)
        {
            return GetImage($"planeswalker{type}");
        }

        public byte[]? GetLoyaltyIconBytes(string type)
        {
            return GetImageBytes($"planeswalker{type}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("planeswalkerFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("planeswalkerMask"))
                .ToList();
        }

        public IReadOnlyList<string> GetAbilityLineNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("abilityLine"))
                .ToList();
        }

        public IReadOnlyList<string> GetLoyaltyIconNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("planeswalker") && !n.StartsWith("planeswalkerFrame") && !n.StartsWith("planeswalkerMask"))
                .ToList();
        }
    }
}
