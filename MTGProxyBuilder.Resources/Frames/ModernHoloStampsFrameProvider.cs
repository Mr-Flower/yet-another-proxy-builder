namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernHoloStampsFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernHoloStamps.";

        protected override string FileExtension => ".png";

        public Stream? GetHoloStamp(string color)
        {
            return GetImage($"modernHoloStamp{color}");
        }

        public byte[]? GetHoloStampBytes(string color)
        {
            return GetImageBytes($"modernHoloStamp{color}");
        }

        public IReadOnlyList<string> GetHoloStampNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernHoloStamp"))
                .ToList();
        }
    }
}
