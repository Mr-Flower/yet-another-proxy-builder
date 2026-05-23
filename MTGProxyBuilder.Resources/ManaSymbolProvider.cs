using System.Reflection;

namespace MTGProxyBuilder.Resources
{
    public static class ManaSymbolProvider
    {
        private static readonly Assembly ResourceAssembly = typeof(ManaSymbolProvider).Assembly;
        private const string Prefix = "MTGProxyBuilder.Resources.ManaSymbols.";

        public static Stream? GetSymbol(string name)
        {
            var resourceName = name.EndsWith(".svg") ? name : $"{name}.svg";
            return ResourceAssembly.GetManifestResourceStream(Prefix + resourceName);
        }

        public static string? GetSymbolSvg(string name)
        {
            using var stream = GetSymbol(name);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public static IReadOnlyList<string> GetAllSymbolNames()
        {
            return ResourceAssembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix) && n.EndsWith(".svg"))
                .Select(n => n[Prefix.Length..^4]) // strip prefix and .svg
                .OrderBy(n => n)
                .ToList();
        }
    }
}
