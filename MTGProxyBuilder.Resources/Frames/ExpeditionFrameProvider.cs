namespace MTGProxyBuilder.Resources.Frames
{
    public class ExpeditionFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.Expedition.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"expeditionFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"expeditionFrame{color}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"expeditionMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"expeditionMask{name}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("expeditionFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("expeditionMask"))
                .ToList();
        }
    }
}
