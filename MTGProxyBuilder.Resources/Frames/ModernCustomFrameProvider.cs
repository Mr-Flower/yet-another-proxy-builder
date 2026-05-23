namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernCustomFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernCustom.";

        protected override string FileExtension => ".png";

        public Stream? GetCustomPowerToughnessInnerFill()
        {
            return GetImage("modernCustomPTInnerFill");
        }

        public byte[]? GetCustomPowerToughnessInnerFillBytes()
        {
            return GetImageBytes("modernCustomPTInnerFill");
        }
    }
}
