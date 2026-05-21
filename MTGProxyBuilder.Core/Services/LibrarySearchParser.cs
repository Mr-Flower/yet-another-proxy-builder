using System.Text.RegularExpressions;
using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Parses Scryfall-inspired search syntax against art library entries.
    ///
    /// Supported syntax (modeled after https://scryfall.com/docs/syntax):
    ///
    ///   NAMES
    ///     lightning bolt       bare words match Name (AND, case-insensitive, partial)
    ///     "exact phrase"       Name contains exact phrase
    ///     !fire                Name equals exactly (case-insensitive)
    ///     !"Black Lotus"       exact name with spaces
    ///
    ///   TEXT FIELD FILTERS (prefix:value)
    ///     name:text  n:text             match Name
    ///     source:text  src:text          match Source/contributor
    ///     type:text  t:text              match TypeLine (e.g. t:creature, t:instant)
    ///     oracle:text  o:text            match Oracle text
    ///     keyword:text  kw:text          match Keywords (e.g. kw:flying)
    ///     artist:text  a:text            match Artist
    ///     set:code  s:code  e:code       match Set code or name
    ///     rarity:value  r:value          match Rarity (common, uncommon, rare, mythic)
    ///     color:wubrg  c:wubrg           match Colors (w/u/b/r/g, c for colorless)
    ///     id:wubrg  identity:wubrg       match Color Identity
    ///     mana:text  m:text              match Mana Cost string
    ///     cn:number  number:number       match Collector Number
    ///
    ///   NUMERIC COMPARISONS
    ///     cmc&gt;3  mv=5                 mana value comparison
    ///     pow&gt;=4  power&lt;2           power comparison
    ///     tou&gt;3  toughness&lt;=5       toughness comparison
    ///     loy=3  loyalty&gt;=4            loyalty comparison
    ///     r&gt;uncommon                  rarity comparison (common&lt;uncommon&lt;rare&lt;mythic)
    ///
    ///   DATE COMPARISONS
    ///     date&gt;2025-01-01             added after date
    ///     date&lt;2025-06-01             added before date
    ///     date=2025-03-15               added on exact date
    ///
    ///   REGEX
    ///     name:/pattern/                regex match on Name
    ///     type:/pattern/                regex match on TypeLine
    ///     oracle:/pattern/              regex match on Oracle text
    ///
    ///   LOGIC
    ///     term1 term2                   AND (all must match)
    ///     term1 OR term2                OR  (either matches)
    ///     -term                         negation (exclude matches)
    ///     (group1) (group2)             parenthetical grouping
    /// </summary>
    public static class LibrarySearchParser
    {
        public static Func<BackArtEntry, bool> Parse(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return _ => true;

            return ParseExpression(query.Trim());
        }

        // ================================================================
        //  EXPRESSION PARSER (handles OR and parentheses)
        // ================================================================

        private static Func<BackArtEntry, bool> ParseExpression(string input)
        {
            var orGroups = SplitTopLevelOr(input);
            if (orGroups.Count == 1)
                return ParseAndGroup(orGroups[0]);

            var predicates = orGroups.Select(ParseAndGroup).ToList();
            return entry => predicates.Any(p => p(entry));
        }

        private static List<string> SplitTopLevelOr(string input)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '(') depth++;
                else if (input[i] == ')') depth--;
                else if (depth == 0 && i + 4 <= input.Length)
                {
                    // Check for " OR " or " or " at top level
                    if ((input[i] == ' ') &&
                        (i + 3 < input.Length) &&
                        (input[i + 1] == 'O' || input[i + 1] == 'o') &&
                        (input[i + 2] == 'R' || input[i + 2] == 'r') &&
                        (input[i + 3] == ' '))
                    {
                        parts.Add(input[start..i].Trim());
                        i += 3;
                        start = i + 1;
                    }
                }
            }
            parts.Add(input[start..].Trim());
            return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        }

        private static Func<BackArtEntry, bool> ParseAndGroup(string group)
        {
            var tokens = Tokenize(group);
            var predicates = tokens.Select(ParseToken).ToList();
            return entry => predicates.All(p => p(entry));
        }

        // ================================================================
        //  TOKENIZER
        // ================================================================

        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < input.Length)
            {
                if (char.IsWhiteSpace(input[i])) { i++; continue; }

                // Parenthetical group
                if (input[i] == '(')
                {
                    int depth = 1;
                    int start = i + 1;
                    i++;
                    while (i < input.Length && depth > 0)
                    {
                        if (input[i] == '(') depth++;
                        else if (input[i] == ')') depth--;
                        if (depth > 0) i++;
                    }
                    tokens.Add("(" + input[start..i] + ")");
                    if (i < input.Length) i++; // skip closing ')'
                    continue;
                }

                // Negation with paren: -(group)
                if (input[i] == '-' && i + 1 < input.Length && input[i + 1] == '(')
                {
                    int depth = 1;
                    int start = i + 2;
                    i += 2;
                    while (i < input.Length && depth > 0)
                    {
                        if (input[i] == '(') depth++;
                        else if (input[i] == ')') depth--;
                        if (depth > 0) i++;
                    }
                    tokens.Add("-(" + input[start..i] + ")");
                    if (i < input.Length) i++;
                    continue;
                }

                // Quoted string: "..."  or !"..."
                if (input[i] == '"' || (input[i] == '!' && i + 1 < input.Length && input[i + 1] == '"'))
                {
                    int start = i;
                    if (input[i] == '!') i++;
                    i++; // skip opening quote
                    while (i < input.Length && input[i] != '"') i++;
                    if (i < input.Length) i++; // skip closing quote
                    tokens.Add(input[start..i]);
                    continue;
                }

                // Negation with quoted: -"..."
                if (input[i] == '-' && i + 1 < input.Length && input[i + 1] == '"')
                {
                    int start = i;
                    i += 2;
                    while (i < input.Length && input[i] != '"') i++;
                    if (i < input.Length) i++;
                    tokens.Add(input[start..i]);
                    continue;
                }

                // Prefixed with quoted value: prefix:"quoted value" or -prefix:"quoted value"
                {
                    int peek = i;
                    if (input[peek] == '-') peek++;
                    // Check if there's a word followed by : or operator then "
                    while (peek < input.Length && (char.IsLetterOrDigit(input[peek]) || input[peek] == '_')) peek++;
                    if (peek < input.Length && (input[peek] == ':' || input[peek] == '>' || input[peek] == '<' || input[peek] == '=' || input[peek] == '!'))
                    {
                        while (peek < input.Length && (input[peek] == ':' || input[peek] == '>' || input[peek] == '<' || input[peek] == '=' || input[peek] == '!')) peek++;
                        if (peek < input.Length && input[peek] == '"')
                        {
                            // Found prefix:operator"...", consume through closing quote
                            peek++;
                            while (peek < input.Length && input[peek] != '"') peek++;
                            if (peek < input.Length) peek++; // closing quote
                            tokens.Add(input[i..peek]);
                            i = peek;
                            continue;
                        }
                    }
                }

                // Regular token (until next whitespace or opening paren)
                {
                    int start = i;
                    while (i < input.Length && !char.IsWhiteSpace(input[i]) && input[i] != '(') i++;
                    tokens.Add(input[start..i]);
                }
            }
            return tokens;
        }

        // ================================================================
        //  TOKEN PARSER
        // ================================================================

        private static Func<BackArtEntry, bool> ParseToken(string token)
        {
            // Parenthetical group (recursive)
            if (token.StartsWith('(') && token.EndsWith(')'))
                return ParseExpression(token[1..^1]);

            // Negated parenthetical group
            if (token.StartsWith("-(") && token.EndsWith(')'))
            {
                var inner = ParseExpression(token[2..^1]);
                return e => !inner(e);
            }

            // Negation
            if (token.StartsWith('-') && token.Length > 1 && token[1] != '(')
            {
                var inner = ParseToken(token[1..]);
                return e => !inner(e);
            }

            // Exact name: !word or !"phrase"
            if (token.StartsWith('!') && token.Length > 1)
            {
                string exactName = token[1..].Trim('"');
                return e => e.Name.Equals(exactName, StringComparison.OrdinalIgnoreCase);
            }

            // Quoted exact phrase -> name contains
            if (token.StartsWith('"') && token.EndsWith('"') && token.Length > 2)
            {
                string phrase = token[1..^1];
                return e => e.Name.Contains(phrase, StringComparison.OrdinalIgnoreCase);
            }

            // Prefixed filters with regex: field:/pattern/
            var regexMatch = Regex.Match(token, @"^(\w+):/(.+)/$");
            if (regexMatch.Success)
            {
                string field = regexMatch.Groups[1].Value.ToLowerInvariant();
                string pattern = regexMatch.Groups[2].Value;
                try
                {
                    var rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    return field switch
                    {
                        "name" or "n" => e => rx.IsMatch(e.Name),
                        "source" or "src" => e => rx.IsMatch(e.Source),
                        "type" or "t" => e => rx.IsMatch(e.TypeLine),
                        "oracle" or "o" => e => rx.IsMatch(e.OracleText),
                        "artist" or "a" => e => rx.IsMatch(e.Artist),
                        "set" or "e" => e => rx.IsMatch(e.SetCode) || rx.IsMatch(e.SetName),
                        "keyword" or "kw" => e => rx.IsMatch(e.Keywords),
                        _ => e => rx.IsMatch(e.Name)
                    };
                }
                catch { return _ => true; }
            }

            // Prefixed filters: field:value, field>=value, etc.
            var prefixMatch = Regex.Match(token, @"^(\w+)([:<>=!]+)(.+)$");
            if (prefixMatch.Success)
            {
                string field = prefixMatch.Groups[1].Value.ToLowerInvariant();
                string op = prefixMatch.Groups[2].Value;
                string value = prefixMatch.Groups[3].Value.Trim('"');

                return field switch
                {
                    // Text fields
                    "name" or "n" => TextFilter(e => e.Name, value),
                    "source" or "src" => TextFilter(e => e.Source, value),
                    "type" or "t" => TextFilter(e => e.TypeLine, value),
                    "oracle" or "o" => TextFilter(e => e.OracleText, value),
                    "keyword" or "kw" => TextFilter(e => e.Keywords, value),
                    "artist" or "a" => TextFilter(e => e.Artist, value),
                    "set" or "s" or "e" or "edition" => SetFilter(value),
                    "rarity" or "r" => RarityFilter(op, value),
                    "color" or "c" => ColorFilter(e => e.Colors, value),
                    "id" or "identity" => ColorFilter(e => e.ColorIdentity, value),
                    "mana" or "m" => TextFilter(e => e.ManaCost, value),
                    "cn" or "number" => TextFilter(e => e.CollectorNumber, value),

                    // Numeric fields
                    "cmc" or "mv" or "manavalue" => NumericFilter(e => e.CMC, op, value),
                    "pow" or "power" => NumericFilter(e => ParseNumeric(e.Power), op, value),
                    "tou" or "toughness" => NumericFilter(e => ParseNumeric(e.Toughness), op, value),
                    "loy" or "loyalty" => NumericFilter(e => ParseNumeric(e.Loyalty), op, value),

                    // Date
                    "date" or "added" or "d" or "year" => DateFilter(op, value),

                    // Entry ID
                    "entryid" => TextFilter(e => e.Id, value),

                    _ => NameFilter(token)
                };
            }

            // Bare text -> name contains
            return NameFilter(token);
        }

        // ================================================================
        //  FIELD MATCHERS
        // ================================================================

        private static Func<BackArtEntry, bool> NameFilter(string value)
            => e => e.Name.Contains(value, StringComparison.OrdinalIgnoreCase);

        private static Func<BackArtEntry, bool> TextFilter(Func<BackArtEntry, string> field, string value)
            => e => field(e).Contains(value, StringComparison.OrdinalIgnoreCase);

        private static Func<BackArtEntry, bool> SetFilter(string value)
            => e => e.SetCode.Equals(value, StringComparison.OrdinalIgnoreCase)
                 || e.SetName.Contains(value, StringComparison.OrdinalIgnoreCase);

        private static Func<BackArtEntry, bool> RarityFilter(string op, string value)
        {
            // Support both comparison and text match
            if (op == ":" || op == "=")
                return e => e.Rarity.Equals(value, StringComparison.OrdinalIgnoreCase);
            if (op == "!=")
                return e => !e.Rarity.Equals(value, StringComparison.OrdinalIgnoreCase);

            // Numeric comparison: common=1, uncommon=2, rare=3, mythic=4
            int targetRank = RarityRank(value);
            return op switch
            {
                ">" => e => RarityRank(e.Rarity) > targetRank,
                ">=" => e => RarityRank(e.Rarity) >= targetRank,
                "<" => e => RarityRank(e.Rarity) < targetRank,
                "<=" => e => RarityRank(e.Rarity) <= targetRank,
                _ => e => e.Rarity.Equals(value, StringComparison.OrdinalIgnoreCase)
            };
        }

        private static int RarityRank(string rarity) => rarity.ToLowerInvariant() switch
        {
            "common" => 1, "uncommon" => 2, "rare" => 3, "mythic" => 4,
            "special" => 5, "bonus" => 6, _ => 0
        };

        private static Func<BackArtEntry, bool> ColorFilter(Func<BackArtEntry, string> field, string value)
        {
            string lower = value.ToLowerInvariant();

            // Handle "colorless" or single "c" first (before letter parsing)
            if (lower is "c" or "colorless")
                return e => string.IsNullOrEmpty(field(e));
            if (lower is "m" or "multicolor")
                return e => field(e).Length > 1;

            // Expand color names/letters to single letters for matching
            var targetColors = new HashSet<char>();
            foreach (char c in lower)
            {
                switch (c)
                {
                    case 'w': targetColors.Add('W'); break;
                    case 'u': targetColors.Add('U'); break;
                    case 'b': targetColors.Add('B'); break;
                    case 'r': targetColors.Add('R'); break;
                    case 'g': targetColors.Add('G'); break;
                }
            }

            if (targetColors.Count == 0)
                return e => field(e).Contains(value, StringComparison.OrdinalIgnoreCase);

            return e =>
            {
                var entryColors = field(e).ToUpperInvariant().ToHashSet();
                return targetColors.All(tc => entryColors.Contains(tc));
            };
        }

        private static Func<BackArtEntry, bool> NumericFilter(Func<BackArtEntry, float> field, string op, string value)
        {
            if (!float.TryParse(value, out var target))
                return _ => true;

            return op switch
            {
                ">" => e => field(e) > target,
                ">=" or ":>=" => e => field(e) >= target,
                "<" => e => field(e) < target,
                "<=" or ":<=" => e => field(e) <= target,
                "=" or ":" => e => Math.Abs(field(e) - target) < 0.001f,
                "!=" => e => Math.Abs(field(e) - target) >= 0.001f,
                _ => _ => true
            };
        }

        private static float ParseNumeric(string value)
        {
            if (float.TryParse(value, out var f)) return f;
            // Handle "*" power/toughness as 0
            return 0;
        }

        private static Func<BackArtEntry, bool> DateFilter(string op, string value)
        {
            if (!DateTime.TryParse(value, out var date))
                return _ => true;

            return op switch
            {
                ">" => e => e.AddedDate > date,
                ">=" => e => e.AddedDate >= date,
                "<" => e => e.AddedDate < date,
                "<=" => e => e.AddedDate <= date,
                "=" or ":" => e => e.AddedDate.Date == date.Date,
                "!=" => e => e.AddedDate.Date != date.Date,
                _ => _ => true
            };
        }
    }
}
