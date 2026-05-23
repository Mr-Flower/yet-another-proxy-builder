namespace MTGProxyBuilder.Resources.Frames
{
    public class SagaFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.Saga.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"sagaFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"sagaFrame{color}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"sagaMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"sagaMask{name}");
        }

        public Stream? GetChapter()
        {
            return GetImage("sagaChapter");
        }

        public byte[]? GetChapterBytes()
        {
            return GetImageBytes("sagaChapter");
        }

        public Stream? GetDivider()
        {
            return GetImage("sagaDivider");
        }

        public byte[]? GetDividerBytes()
        {
            return GetImageBytes("sagaDivider");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("sagaFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("sagaMask"))
                .ToList();
        }
    }
}
