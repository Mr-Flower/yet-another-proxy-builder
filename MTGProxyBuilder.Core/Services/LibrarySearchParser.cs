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
    ///   FIELD FILTERS (prefix:value)
    ///     name:text  n:text    match Name
    ///     source:text  src:text  s:text   match Source/contributor
    ///     id:text              match entry Id
    ///
    ///   DATE COMPARISONS
    ///     date&gt;2025-01-01   added after date
    ///     date&lt;2025-06-01   added before date
    ///     date=2025-03-15     added on exact date
    ///     date&gt;=  date&lt;=  date!=   also supported
    ///     added: / d:         aliases for date
    ///
    ///   REGEX
    ///     name:/pattern/       regex match on Name
    ///     source:/pattern/     regex match on Source
    ///
    ///   LOGIC
    ///     term1 term2          AND (all must match)
    ///     term1 OR term2       OR  (either matches, case-insensitive "or")
    ///     term1 or term2       same
    ///     -term                negation (exclude matches)
    ///     -source:Chilli       negate any prefixed filter
    ///     (group1) (group2)    parenthetical grouping
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
                        "source" or "src" or "s" => e => rx.IsMatch(e.Source),
                        _ => e => rx.IsMatch(e.Name)
                    };
                }
                catch { return _ => true; } // invalid regex, don't filter
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
                    "name" or "n" => NameFilter(value),
                    "source" or "src" or "s" => SourceFilter(value),
                    "id" => e => e.Id.Contains(value, StringComparison.OrdinalIgnoreCase),
                    "date" or "added" or "d" => DateFilter(op, value),
                    _ => NameFilter(token) // unknown prefix, treat whole thing as name search
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

        private static Func<BackArtEntry, bool> SourceFilter(string value)
            => e => e.Source.Contains(value, StringComparison.OrdinalIgnoreCase);

        private static Func<BackArtEntry, bool> DateFilter(string op, string value)
        {
            if (!DateTime.TryParse(value, out var date))
                return _ => true; // invalid date, don't filter

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
