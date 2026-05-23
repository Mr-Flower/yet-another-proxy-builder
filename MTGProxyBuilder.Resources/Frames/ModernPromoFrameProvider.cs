namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernPromoFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernPromo.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernPromoFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernPromoFrame{color}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"modernPromoMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"modernPromoMask{name}");
        }

        public Stream? GetNicknameFrame(string color)
        {
            return GetImage($"modernPromoNicknameFrame{color}");
        }

        public byte[]? GetNicknameFrameBytes(string color)
        {
            return GetImageBytes($"modernPromoNicknameFrame{color}");
        }

        public Stream? GetTextboxes()
        {
            return GetImage("modernPromoTextboxes");
        }

        public byte[]? GetTextboxesBytes()
        {
            return GetImageBytes("modernPromoTextboxes");
        }

        public Stream? GetNicknameTextboxes()
        {
            return GetImage("modernPromoNicknameTextboxes");
        }

        public byte[]? GetNicknameTextboxesBytes()
        {
            return GetImageBytes("modernPromoNicknameTextboxes");
        }

        public Stream? GetPinlineOutline()
        {
            return GetImage("modernPromoPinlineOutline");
        }

        public byte[]? GetPinlineOutlineBytes()
        {
            return GetImageBytes("modernPromoPinlineOutline");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernPromoFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernPromoMask"))
                .ToList();
        }

        public IReadOnlyList<string> GetNicknameFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernPromoNicknameFrame"))
                .ToList();
        }
    }
}
