namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernTextlessGenericShowcaseFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernTextlessGenericShowcase.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernTextlessGenericShowcaseFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernTextlessGenericShowcaseFrame{color}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernTextlessGenericShowcaseFrame"))
                .ToList();
        }
    }
}
