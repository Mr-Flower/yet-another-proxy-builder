namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernInventionClassicFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernInventionClassic.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernFrame{color}InventionClassic");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernFrame{color}InventionClassic");
        }

        public Stream? GetGoldTrim()
        {
            return GetImage("modernFrameInventionClassicGoldTrim");
        }

        public byte[]? GetGoldTrimBytes()
        {
            return GetImageBytes("modernFrameInventionClassicGoldTrim");
        }

        public Stream? GetMask()
        {
            return GetImage("modernMaskInventionClassicFrame");
        }

        public byte[]? GetMaskBytes()
        {
            return GetImageBytes("modernMaskInventionClassicFrame");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernFrame") && n.Contains("InventionClassic") && !n.Contains("GoldTrim"))
                .ToList();
        }
    }
}
