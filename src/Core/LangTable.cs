using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Verboten.Core
{
    /// <summary>
    /// The texts of one language, read from a simple file that translators can edit without knowing C#:
    /// <code>
    /// # comment
    /// [key]
    /// Text, may span several lines, with {placeholders}.
    /// </code>
    /// A text runs from its [key] line to the next [key] line; empty lines at its end are dropped, leading spaces
    /// are kept (indented command lists).
    /// </summary>
    public static class LangTable
    {
        private static readonly Regex KeyLine = new Regex(@"^\[([a-z0-9_.\-]+)\]\s*$", RegexOptions.Compiled);
        private static readonly Regex Placeholder = new Regex(@"\{([a-z0-9_]+)\}", RegexOptions.Compiled);

        public static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>();
            string key = null;
            var lines = new List<string>();
            foreach (var raw in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                var m = KeyLine.Match(raw);
                if (m.Success)
                {
                    Store(result, key, lines);
                    key = m.Groups[1].Value;
                    lines.Clear();
                    continue;
                }
                if (key == null || raw.StartsWith("#", StringComparison.Ordinal)) continue;
                lines.Add(raw.TrimEnd());
            }
            Store(result, key, lines);
            return result;
        }

        private static void Store(Dictionary<string, string> result, string key, List<string> lines)
        {
            if (key == null) return;
            int end = lines.Count;
            while (end > 0 && lines[end - 1].Length == 0) end--;
            result[key] = string.Join("\n", lines.GetRange(0, end));
        }

        /// <summary>Replaces {name} with the values given; unknown placeholders stay as they are.</summary>
        public static string Format(string template, params (string Name, object Value)[] args)
        {
            if (string.IsNullOrEmpty(template) || args == null || args.Length == 0) return template;
            return Placeholder.Replace(template, m =>
            {
                foreach (var a in args)
                    if (a.Name == m.Groups[1].Value) return Convert.ToString(a.Value, CultureInfo.InvariantCulture) ?? "";
                return m.Value;
            });
        }

        /// <summary>The placeholder names in a text (for checking translations).</summary>
        public static SortedSet<string> PlaceholdersOf(string template)
        {
            var set = new SortedSet<string>();
            foreach (Match m in Placeholder.Matches(template ?? "")) set.Add(m.Groups[1].Value);
            return set;
        }
    }
}
