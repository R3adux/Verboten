using System;
using System.Collections.Generic;
using System.IO;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// All texts players see, in the language set in the config (Lang/*.txt, built into the DLL).
    /// Missing texts fall back to English, then to the key itself – never an error in the game.
    /// Log messages stay English (for admins and bug reports).
    /// </summary>
    public static class L
    {
        private static readonly Dictionary<Language, Dictionary<string, string>> Tables = new Dictionary<Language, Dictionary<string, string>>();

        public static void Load()
        {
            foreach (Language lang in Enum.GetValues(typeof(Language)))
            {
                string resource = $"Verboten.Lang.{Code(lang)}.txt";
                try
                {
                    using (var stream = typeof(L).Assembly.GetManifestResourceStream(resource))
                    {
                        if (stream == null) { Plugin.Log.LogError($"Texts '{resource}' are missing."); continue; }
                        using (var reader = new StreamReader(stream))
                            Tables[lang] = LangTable.Parse(reader.ReadToEnd());
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Could not read the texts '{resource}': {ex.Message}");
                }
            }
            // the default name of a claim ("Bob's base") follows the language too
            Claim.DefaultName = owner => T("claim.default_name", ("owner", owner));
        }

        public static string Code(Language lang) => lang == Language.Deutsch ? "de" : "en";

        private static Language Current => Plugin.Settings?.Language.Value ?? Language.English;

        /// <summary>The text for a key, with {placeholders} filled in.</summary>
        public static string T(string key, params (string Name, object Value)[] args)
        {
            string template = null;
            if (Tables.TryGetValue(Current, out var table)) table.TryGetValue(key, out template);
            if (template == null && Tables.TryGetValue(Language.English, out var english)) english.TryGetValue(key, out template);
            return LangTable.Format(template ?? key, args);
        }

        public static string Anchor => T(Plugin.Settings.Anchor.Value == ClaimAnchor.Flag ? "anchor.flag" : "anchor.autolathe");

        public static string MaxSize => T("size.max", ("max", $"{ClaimGeometry.MaxSpan:0} x {ClaimGeometry.MaxSpan:0}"));

        /// <summary>"north", "south-east", … in the current language.</summary>
        public static string Direction(float dx, float dz)
        {
            string name = ClaimGeometry.Compass(dx, dz);
            return name.Length == 0 ? "" : T("dir." + name);
        }

        public static string Side(Side side) => T("side." + side.ToString().ToLowerInvariant());

        public static string Role(Role role) => T("role." + role.ToString().ToLowerInvariant());

        /// <summary>Why a claim does not fit (landing zone, other claim), in the current language.</summary>
        public static string Problem(ClaimGeometry.Problem p) =>
            T(p.Key, ("m", p.Meters), ("claim", p.Other), ("gap", $"{ClaimGeometry.Gap:0}"));

        /// <summary>"Move about 12 m north." – or null if there is no direction.</summary>
        public static string MoveHint(ClaimGeometry.Problem p)
        {
            string dir = Direction(p.MoveX, p.MoveZ);
            if (dir.Length == 0) return null;
            float m = (float)Math.Ceiling(Math.Sqrt(p.MoveX * p.MoveX + p.MoveZ * p.MoveZ));
            return T("hint.move", ("m", $"{m:0}"), ("dir", dir));
        }
    }
}
