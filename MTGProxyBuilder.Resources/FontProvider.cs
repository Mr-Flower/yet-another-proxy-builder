using System.Reflection;

namespace MTGProxyBuilder.Resources
{
    public static class FontProvider
    {
        private static readonly Assembly ResourceAssembly = typeof(FontProvider).Assembly;
        private const string Prefix = "MTGProxyBuilder.Resources.Fonts.";

        public static Stream? GetFont(string name)
        {
            var resourceName = name.Contains('.') ? name : FindResourceName(name);
            if (resourceName is null) return null;
            return ResourceAssembly.GetManifestResourceStream(Prefix + resourceName);
        }

        public static byte[]? GetFontBytes(string name)
        {
            using var stream = GetFont(name);
            if (stream is null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        public static IReadOnlyList<string> GetAllFontNames()
        {
            return ResourceAssembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix) && IsFontExtension(n))
                .Select(n => StripExtension(n[Prefix.Length..]))
                .OrderBy(n => n)
                .ToList();
        }

        public static IReadOnlyList<string> GetAllFontFileNames()
        {
            return ResourceAssembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix) && IsFontExtension(n))
                .Select(n => n[Prefix.Length..])
                .OrderBy(n => n)
                .ToList();
        }

        private static bool IsFontExtension(string name)
            => name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase);

        private static string StripExtension(string fileName)
            => fileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ? fileName[..^4]
             : fileName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ? fileName[..^4]
             : fileName;

        private static string? FindResourceName(string baseName)
        {
            var ttf = baseName + ".ttf";
            if (ResourceAssembly.GetManifestResourceInfo(Prefix + ttf) is not null)
                return ttf;
            var otf = baseName + ".otf";
            if (ResourceAssembly.GetManifestResourceInfo(Prefix + otf) is not null)
                return otf;
            return null;
        }
    }
}
