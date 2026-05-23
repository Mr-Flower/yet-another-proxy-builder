namespace MTGProxyBuilder.Resources.Frames
{
    public class StorybookFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.Storybook.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"storybookFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"storybookFrame{color}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"storybookMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"storybookMask{name}");
        }

        public Stream? GetPowerToughness(string color)
        {
            return GetImage($"storybookPT{color}");
        }

        public byte[]? GetPowerToughnessBytes(string color)
        {
            return GetImageBytes($"storybookPT{color}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("storybookFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("storybookMask"))
                .ToList();
        }

        public IReadOnlyList<string> GetPowerToughnessNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("storybookPT"))
                .ToList();
        }
    }
}
