namespace MTGProxyBuilder.Resources.Frames
{
    public class ModalFrameProvider : FrameProvider
    {
        protected override string ResourcePrefix => "MTGProxyBuilder.Resources.Frames.Modal.";

        protected override string FileExtension => ".png";

        public Stream? GetFrame(string color)
        {
            return GetImage($"modalFrame{color}");
        }

        public byte[]? GetFrameBytes(string color)
        {
            return GetImageBytes($"modalFrame{color}");
        }

        public Stream? GetMask(string name)
        {
            return GetImage($"modalMask{name}");
        }

        public byte[]? GetMaskBytes(string name)
        {
            return GetImageBytes($"modalMask{name}");
        }

        public Stream? GetIcon(string name)
        {
            return GetImage($"modalIcon{name}");
        }

        public byte[]? GetIconBytes(string name)
        {
            return GetImageBytes($"modalIcon{name}");
        }

        public IReadOnlyList<string> GetFrameNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modalFrame"))
                .ToList();
        }

        public IReadOnlyList<string> GetMaskNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modalMask"))
                .ToList();
        }

        public IReadOnlyList<string> GetIconNames()
        {
            return GetAllImageNames()
                .Where(n => n.StartsWith("modalIcon"))
                .ToList();
        }
    }
}
