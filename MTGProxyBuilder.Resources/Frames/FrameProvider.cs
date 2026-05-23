using System.Reflection;

namespace MTGProxyBuilder.Resources.Frames
{
    public abstract class FrameProvider
    {
        private static readonly Assembly ResourceAssembly = typeof(FrameProvider).Assembly;

        protected abstract string ResourcePrefix { get; }

        protected abstract string FileExtension { get; }

        public Stream? GetImage(string fileName)
        {
            var resourceName = fileName.Contains('.')
                ? fileName
                : $"{fileName}{FileExtension}";
            return ResourceAssembly.GetManifestResourceStream(ResourcePrefix + resourceName);
        }

        public byte[]? GetImageBytes(string fileName)
        {
            using var stream = GetImage(fileName);
            if (stream is null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        public IReadOnlyList<string> GetAllImageNames()
        {
            var ext = FileExtension;
            return ResourceAssembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(ResourcePrefix) && n.EndsWith(ext))
                .Select(n => n[ResourcePrefix.Length..^ext.Length])
                .OrderBy(n => n)
                .ToList();
        }
    }
}
