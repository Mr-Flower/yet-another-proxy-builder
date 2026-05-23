namespace MTGProxyBuilder.Resources.Frames
{
    public class TokenFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.Token.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color, string layout)
        {
            return GetImage($"tokenFrame{color}{layout}");
        }

        public byte[]? GetFrameBytes(string color, string layout)
        {
            return GetImageBytes($"tokenFrame{color}{layout}");
        }

        public Stream? GetMask(string layout, string part)
        {
            return GetImage($"tokenMask{layout}{part}");
        }

        public byte[]? GetMaskBytes(string layout, string part)
        {
            return GetImageBytes($"tokenMask{layout}{part}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("tokenFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("tokenMask"))
                .ToList();
        }
    }
}
