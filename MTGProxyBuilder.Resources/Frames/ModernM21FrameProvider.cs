namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernM21FrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.ModernM21.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernM21Frame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernM21Frame{color}");
        }

        public Stream? GetPowerToughness(string color)
        {
            return GetImage($"modernM21PT{color}");
        }

        public byte[]? GetPowerToughnessBytes(string color)
        {
            return GetImageBytes($"modernM21PT{color}");
        }

        public Stream? GetFire()
        {
            return GetImage("modernM21Fire");
        }

        public byte[]? GetFireBytes()
        {
            return GetImageBytes("modernM21Fire");
        }

        public Stream? GetStamp()
        {
            return GetImage("modernM21Stamp");
        }

        public byte[]? GetStampBytes()
        {
            return GetImageBytes("modernM21Stamp");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernM21Frame"))
                .ToList();
        }

        public IReadOnlyList<string> GetPowerToughnessNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernM21PT"))
                .ToList();
        }
    }
}
