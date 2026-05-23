namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernNicknameFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernNickname.";

        protected override string FileExtension => ".png";

        public Stream? GetMaskTrueName()
        {
            return GetImage("modernNicknameMaskTrueName");
        }

        public byte[]? GetMaskTrueNameBytes()
        {
            return GetImageBytes("modernNicknameMaskTrueName");
        }
    }
}
