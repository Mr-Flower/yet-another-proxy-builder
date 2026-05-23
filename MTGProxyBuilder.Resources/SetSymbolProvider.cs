using System.Reflection;

namespace MTGProxyBuilder.Resources
{
    public static class SetSymbolProvider
    {
        private static readonly Assembly ResourceAssembly = typeof(SetSymbolProvider).Assembly;
        private const string Prefix = "MTGProxyBuilder.Resources.SetSymbols.";

        public static Stream? GetSymbol(string fileName)
        {
            return ResourceAssembly.GetManifestResourceStream(Prefix + fileName);
        }

        public static Stream? GetSymbol(string setCode, string rarity)
        {
            return GetSymbol($"{setCode}-{rarity}.svg");
        }

        public static string? GetSymbolSvg(string setCode, string rarity)
        {
            using var stream = GetSymbol(setCode, rarity);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public static IReadOnlyList<string> GetAllSymbolNames()
        {
            return ResourceAssembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix) && n.EndsWith(".svg"))
                .Select(n => n[Prefix.Length..])
                .OrderBy(n => n)
                .ToList();
        }

        public static IReadOnlyList<string> GetSetCodes()
        {
            return GetAllSymbolNames()
                .Select(n => n.Split('-')[0])
                .Distinct()
                .OrderBy(c => c)
                .ToList();
        }

        public static IReadOnlyList<string> GetRarities(string setCode)
        {
            var prefix = setCode + "-";
            return GetAllSymbolNames()
                .Where(n => n.StartsWith(prefix))
                .Select(n => n[prefix.Length..^4]) // strip prefix and .svg
                .OrderBy(r => r)
                .ToList();
        }
    }
}
