namespace MTGProxyBuilder.Resources.Frames
{
    public class ShortModalFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ShortModal.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color, string side)
        {
            return GetImage($"shortModalFrame{color}{side}");
        }

        public byte[]? GetFrameBytes(string color, string side)
        {
            return GetImageBytes($"shortModalFrame{color}{side}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"shortModalMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"shortModalMask{name}");
        }

        public Stream? GetNicknameTitle(string color)
        {
            return GetImage($"shortModalNicknameTitle{color}");
        }

        public byte[]? GetNicknameTitleBytes(string color)
        {
            return GetImageBytes($"shortModalNicknameTitle{color}");
        }

        public Stream? GetNicknameMaskTitle()
        {
            return GetImage("shortModalNicknameMaskTitle");
        }

        public byte[]? GetNicknameMaskTitleBytes()
        {
            return GetImageBytes("shortModalNicknameMaskTitle");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("shortModalFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("shortModalMask"))
                .ToList();
        }

        public IReadOnlyList<string> GetNicknameTitleNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("shortModalNicknameTitle"))
                .ToList();
        }
    }
}
