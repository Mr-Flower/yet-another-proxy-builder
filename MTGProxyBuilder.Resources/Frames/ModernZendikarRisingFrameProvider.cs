namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernZendikarRisingFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernZendikarRising.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernZendikarRisingFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernZendikarRisingFrame{color}");
        }

        public Stream? GetCrown(string color)
        {
            return GetImage($"modernZendikarRisingCrown{color}");
        }

        public byte[]? GetCrownBytes(string color)
        {
            return GetImageBytes($"modernZendikarRisingCrown{color}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"modernZendikarRisingMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"modernZendikarRisingMask{name}");
        }

        public Stream? GetPowerToughness(string color)
        {
            return GetImage($"modernZendikarRisingPT{color}");
        }

        public byte[]? GetPowerToughnessBytes(string color)
        {
            return GetImageBytes($"modernZendikarRisingPT{color}");
        }

        public Stream? GetTitle(string color)
        {
            return GetImage($"modernZendikarRisingTitle{color}");
        }

        public byte[]? GetTitleBytes(string color)
        {
            return GetImageBytes($"modernZendikarRisingTitle{color}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernZendikarRisingFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetCrownNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernZendikarRisingCrown"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernZendikarRisingMask"))
                .ToList();
        }

        public IReadOnlyList<string> GetPowerToughnessNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernZendikarRisingPT"))
                .ToList();
        }

        public IReadOnlyList<string> GetTitleNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernZendikarRisingTitle"))
                .ToList();
        }
    }
}
