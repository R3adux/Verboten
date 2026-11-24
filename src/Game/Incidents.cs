using System;
using System.Collections.Generic;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Remembers what strangers tried in a claim (saved with the claim, last 20) and tells the owner the next
    /// time they join: "While you were away: Bob entered without permission (3x), …".
    /// </summary>
    public static class Incidents
    {
        private const long RepeatMinutes = 10;
        private static readonly Dictionary<(string, string), long> Last = new Dictionary<(string, string), long>();

        public static void Add(Claim claim, string text)
        {
            if (claim == null || !claim.IsActive) return;
            var key = (claim.Id, text);
            if (Last.TryGetValue(key, out long minute) && World.Minute - minute < RepeatMinutes) return;
            Last[key] = World.Minute;
            Plugin.Store.Update(claim.Id, c => c.AddIncident(World.Minute, text));
        }

        /// <summary>Summary of incidents in the player's claims since <paramref name="sinceMinute"/>, or null.</summary>
        public static string SummaryFor(ulong owner, long sinceMinute)
        {
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (var c in Plugin.Store.Index.OwnedBy(owner))
                foreach (var i in c.Incidents)
                {
                    if (i.Minute < sinceMinute) continue;
                    string line = $"{c.DisplayName}: {i.Text}";
                    if (!counts.ContainsKey(line)) { counts[line] = 0; order.Add(line); }
                    counts[line]++;
                }
            if (order.Count == 0) return null;
            var parts = new List<string>();
            foreach (var line in order) parts.Add(counts[line] > 1 ? L.T("away.repeat", ("line", line), ("count", counts[line])) : line);
            return L.T("away", ("list", string.Join("; ", parts)));
        }

        public static void Reset() => Last.Clear();
    }
}
