namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernCommanderLegendsFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernCommanderLegends.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernCommanderLegendsFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernCommanderLegendsFrame{color}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"modernCommanderLegendsMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"modernCommanderLegendsMask{name}");
        }

        public Stream? GetPowerToughness(string color)
        {
            return GetImage($"modernCommanderLegendsPT{color}");
        }

        public byte[]? GetPowerToughnessBytes(string color)
        {
            return GetImageBytes($"modernCommanderLegendsPT{color}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernCommanderLegendsFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernCommanderLegendsMask"))
                .ToList();
        }

        public IReadOnlyList<string> GetPowerToughnessNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernCommanderLegendsPT"))
                .ToList();
        }
    }
}
