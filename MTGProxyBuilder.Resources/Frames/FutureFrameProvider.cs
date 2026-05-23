namespace MTGProxyBuilder.Resources.Frames
{
    public class FutureFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.Future.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"futureFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"futureFrame{color}");
        }

        public Stream? GetMask(string cardType)
        {
            return GetImage($"futureMask{cardType}");
        }

        public byte[]? GetMaskBytes(string cardType)
        {
            return GetImageBytes($"futureMask{cardType}");
        }

        public Stream? GetPowerToughness(string color)
        {
            return GetImage($"futurePT{color}");
        }

        public byte[]? GetPowerToughnessBytes(string color)
        {
            return GetImageBytes($"futurePT{color}");
        }

        public Stream? GetOverlay(string name)
        {
            return GetImage($"future{name}");
        }

        public byte[]? GetOverlayBytes(string name)
        {
            return GetImageBytes($"future{name}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("futureFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("futureMask"))
                .ToList();
        }

        public IReadOnlyList<string> GetPowerToughnessNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("futurePT"))
                .ToList();
        }
    }
}
