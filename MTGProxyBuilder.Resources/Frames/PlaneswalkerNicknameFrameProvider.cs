namespace MTGProxyBuilder.Resources.Frames
{
    public class PlaneswalkerNicknameFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.PlaneswalkerNickname.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"planeswalkerNicknameFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"planeswalkerNicknameFrame{color}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("planeswalkerNicknameFrame"))
                .ToList();
        }
    }
}
