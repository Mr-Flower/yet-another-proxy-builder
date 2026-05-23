namespace MTGProxyBuilder.Resources.Frames
{
    public class IxalanFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.Ixalan.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"ixalanFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"ixalanFrame{color}");
        }

        public Stream? GetIcon(string cardType)
        {
            return GetImage($"ixalanIcon{cardType}");
        }

        public byte[]? GetIconBytes(string cardType)
        {
            return GetImageBytes($"ixalanIcon{cardType}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("ixalanFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetIconNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("ixalanIcon"))
                .ToList();
        }
    }
}
