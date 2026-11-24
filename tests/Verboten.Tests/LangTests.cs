using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Verboten.Core;
using Xunit;

namespace Verboten.Tests
{
    /// <summary>The language files (src/Lang/*.txt): complete, consistent, and every text the code asks for exists.</summary>
    public class LangTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Verboten.csproj"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        private static Dictionary<string, string> Load(string code) =>
            LangTable.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lang", code + ".txt")));

        [Fact]
        public void Parse_BlocksCommentsAndIndentation()
        {
            var t = LangTable.Parse("# comment\n[a]\nOne line\n\n[b.c]\n  indented\nsecond\n\n\n[d-e]\n x {name} y\n");
            Assert.Equal("One line", t["a"]);
            Assert.Equal("  indented\nsecond", t["b.c"]);
            Assert.Equal(" x {name} y", t["d-e"]);
            Assert.Equal(" x Bob y", LangTable.Format(t["d-e"], ("name", "Bob")));
            Assert.Equal("{unknown} stays", LangTable.Format("{unknown} stays", ("name", "Bob")));
        }

        [Fact]
        public void German_HasExactlyTheEnglishKeys_WithTheSamePlaceholders()
        {
            var en = Load("en");
            var de = Load("de");
            Assert.Empty(en.Keys.Except(de.Keys));
            Assert.Empty(de.Keys.Except(en.Keys));
            foreach (var key in en.Keys)
                Assert.True(LangTable.PlaceholdersOf(en[key]).SetEquals(LangTable.PlaceholdersOf(de[key])),
                    $"[{key}] uses other placeholders in German: {string.Join(",", LangTable.PlaceholdersOf(de[key]))}");
            foreach (var kv in de) Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"[{kv.Key}] is empty in German");
        }

        [Fact]
        public void EveryTextTheCodeAsksFor_Exists()
        {
            var en = Load("en");
            var used = new SortedSet<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\\bT\\(\"([a-z0-9_.\\-]+)\""))
                    if (!m.Groups[1].Value.EndsWith(".")) used.Add(m.Groups[1].Value); // "dir." + name: listed below
            // keys put together at run time
            foreach (var d in new[] { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" }) used.Add("dir." + d);
            foreach (var s in new[] { "north", "east", "south", "west" }) used.Add("side." + s);
            foreach (var r in new[] { "owner", "friend", "guest", "none" }) { used.Add("role." + r); used.Add("role_short." + r); }
            foreach (var w in new[] { "attack", "drop", "build", "take", "dig", "use", "other" }) { used.Add("denied." + w); used.Add("incident." + w); }
            foreach (var k in new[] { "reach.min", "reach.max", "reach.span", "problem.landing", "problem.gap", "guide.verdict.ok", "guide.verdict.ok_flags" }) used.Add(k);
            var missing = used.Where(k => !en.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, "Missing texts: " + string.Join(", ", missing));
        }

        [Fact]
        public void CompassAndReachCodes_MatchTheTexts()
        {
            var en = Load("en");
            foreach (var (dx, dz) in new[] { (0f, -1f), (-1f, -1f), (-1f, 0f), (-1f, 1f), (0f, 1f), (1f, 1f), (1f, 0f), (1f, -1f) })
                Assert.True(en.ContainsKey("dir." + ClaimGeometry.Compass(dx, dz)));
            var c = Make.Claim(0, 0, 5);
            foreach (var reach in new[] { 4f, 46f })
                Assert.True(en.ContainsKey(ClaimGeometry.CheckReach(c, Side.North, reach)));
        }
    }
}
