namespace MTGProxyBuilder.Resources.Frames
{
    public class ExpeditionNewFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ExpeditionNew.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"expeditionNewFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"expeditionNewFrame{color}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"expeditionNewMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"expeditionNewMask{name}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("expeditionNewFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("expeditionNewMask"))
                .ToList();
        }
    }
}
