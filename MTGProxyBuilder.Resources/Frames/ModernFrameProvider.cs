namespace MTGProxyBuilder.Resources.Frames
{
    public class ModernFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.Modern.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modernFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modernFrame{color}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"modernMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"modernMask{name}");
        }

        public Stream? GetCrown(string color)
        {
            return GetImage($"modernCrown{color}");
        }

        public byte[]? GetCrownBytes(string color)
        {
            return GetImageBytes($"modernCrown{color}");
        }

        public Stream? GetCrownFloating(string color)
        {
            return GetImage($"modernCrownFloating{color}");
        }

        public byte[]? GetCrownFloatingBytes(string color)
        {
            return GetImageBytes($"modernCrownFloating{color}");
        }

        public Stream? GetInnerCrown(string colorVariant)
        {
            return GetImage($"modernInnerCrown{colorVariant}");
        }

        public byte[]? GetInnerCrownBytes(string colorVariant)
        {
            return GetImageBytes($"modernInnerCrown{colorVariant}");
        }

        public Stream? GetPowerToughness(string color)
        {
            return GetImage($"modernPT{color}");
        }

        public byte[]? GetPowerToughnessBytes(string color)
        {
            return GetImageBytes($"modernPT{color}");
        }

        public Stream? GetNicknameFrame(string color)
        {
            return GetImage($"modernNicknameFrame{color}");
        }

        public byte[]? GetNicknameFrameBytes(string color)
        {
            return GetImageBytes($"modernNicknameFrame{color}");
        }

        public Stream? GetNicknameCrown(string color)
        {
            return GetImage($"modernNicknameCrown{color}");
        }

        public byte[]? GetNicknameCrownBytes(string color)
        {
            return GetImageBytes($"modernNicknameCrown{color}");
        }

        public Stream? GetNicknameTitle(string color)
        {
            return GetImage($"modernNicknameTitle{color}");
        }

        public byte[]? GetNicknameTitleBytes(string color)
        {
            return GetImageBytes($"modernNicknameTitle{color}");
        }

        public Stream? GetNicknamePowerToughness(string color)
        {
            return GetImage($"modernNicknamePT{color}");
        }

        public byte[]? GetNicknamePowerToughnessBytes(string color)
        {
            return GetImageBytes($"modernNicknamePT{color}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernMask"))
                .ToList();
        }

        public IReadOnlyList<string> GetCrownNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernCrown") && !n.StartsWith("modernCrownFloating"))
                .ToList();
        }

        public IReadOnlyList<string> GetCrownFloatingNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernCrownFloating"))
                .ToList();
        }

        public IReadOnlyList<string> GetInnerCrownNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernInnerCrown"))
                .ToList();
        }

        public IReadOnlyList<string> GetPowerToughnessNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modernPT"))
                .ToList();
        }
    }
}
