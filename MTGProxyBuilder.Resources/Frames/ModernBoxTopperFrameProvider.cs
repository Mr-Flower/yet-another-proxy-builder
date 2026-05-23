namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernBoxTopperFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernBoxTopper.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernBoxTopperFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernBoxTopperFrame{color}");
        }

        public Stream? GetNickname(string name)
        {
            return GetImage($"modernBoxTopperNickname{name}");
        }

        public byte[]? GetNicknameBytes(string name)
        {
            return GetImageBytes($"modernBoxTopperNickname{name}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernBoxTopperFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetNicknameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernBoxTopperNickname"))
                .ToList();
        }
    }
}
