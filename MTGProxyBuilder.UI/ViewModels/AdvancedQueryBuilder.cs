using System.Collections.Generic;

namespace MTGProxyBuilder.UI.ViewModels
{
    /// <summary>
    /// Builds Scryfall advanced search query strings from individual field values.
    /// Extracted from MainViewModel for testability and reuse.
    /// </summary>
    public static class AdvancedQueryBuilder
    {
        public static string Build(
            string name, string type, string oracle, string colors, string identity,
            string cmcOp, string cmcValue, string rarity, string set, string format,
            string powOp, string powValue, string touOp, string touValue,
            string artist, string keyword, string isValue)
        {
            var parts = new List<string>();

            void Add(string prefix, string value, bool quote = false)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                value = value.Trim();
                if (quote && value.Contains(' '))
                    parts.Add($"{prefix}\"{value}\"");
                else
                    parts.Add($"{prefix}{value}");
            }

            Add("", name);
            if (!string.IsNullOrWhiteSpace(type)) Add("t:", type, true);
            if (!string.IsNullOrWhiteSpace(oracle)) Add("o:", oracle, true);
            if (!string.IsNullOrWhiteSpace(colors)) Add("c:", colors);
            if (!string.IsNullOrWhiteSpace(identity)) Add("id:", identity);
            if (!string.IsNullOrWhiteSpace(cmcValue)) parts.Add($"cmc{cmcOp}{cmcValue}");
            if (!string.IsNullOrWhiteSpace(rarity)) Add("r:", rarity);
            if (!string.IsNullOrWhiteSpace(set)) Add("s:", set);
            if (!string.IsNullOrWhiteSpace(format)) Add("f:", format);
            if (!string.IsNullOrWhiteSpace(powValue)) parts.Add($"pow{powOp}{powValue}");
            if (!string.IsNullOrWhiteSpace(touValue)) parts.Add($"tou{touOp}{touValue}");
            if (!string.IsNullOrWhiteSpace(artist)) Add("a:", artist, true);
            if (!string.IsNullOrWhiteSpace(keyword)) Add("kw:", keyword);
            if (!string.IsNullOrWhiteSpace(isValue)) Add("is:", isValue);

            return string.Join(" ", parts);
        }
    }
}
