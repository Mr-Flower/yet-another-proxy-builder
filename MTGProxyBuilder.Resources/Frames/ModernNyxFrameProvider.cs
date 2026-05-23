namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernNyxFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernNyx.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernFrame{color}NyxSL");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernFrame{color}NyxSL");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernFrame") && n.EndsWith("NyxSL"))
                .ToList();
        }
    }
}
