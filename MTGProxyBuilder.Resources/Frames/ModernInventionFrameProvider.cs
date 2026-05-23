namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernInventionFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernInvention.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernInventionFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernInventionFrame{color}");
        }

        public Stream? GetPowerToughness()
        {
            return GetImage("inventionPT");
        }

        public byte[]? GetPowerToughnessBytes()
        {
            return GetImageBytes("inventionPT");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernInventionFrame"))
                .ToList();
        }
    }
}
