namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernGenericShowcaseFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernGenericShowcase.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernGenericShowcaseFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernGenericShowcaseFrame{color}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernGenericShowcaseFrame"))
                .ToList();
        }
    }
}
