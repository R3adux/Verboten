using System;
using System.Collections.Generic;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// The longer texts for players (windows, join lines), put together from the language files (Lang/*.txt).
    /// Short chat replies use <see cref="L.T"/> right where they are sent.
    /// </summary>
    public static class Texts
    {
        private static string A => L.Anchor;
        private static int ExpiryDays => Math.Max(1, Plugin.Settings.ClaimExpiryDays.Value);

        /// <summary>How far from the landing zone center no claim is possible (spawn radius + 20 m).</summary>
        private static float LandingDistance
        {
            get
            {
                var zones = World.LandingZones();
                return zones.Count > 0 ? zones[0].ProtectedRadius : 50f;
            }
        }

        /// <summary>When a player who belongs to no claim joins: two short chat lines.</summary>
        public static IEnumerable<string> JoinLines(int abandoned)
        {
            yield return L.T("join.rules", ("seconds", $"{Trespass.KickAfter:0}"));
            yield return L.T("join.commands");
            if (abandoned > 0) yield return L.T("join.abandoned", ("count", abandoned));
        }

        public static string Roles => L.T("roles");

        public static string Expiry => L.T("expiry", ("days", ExpiryDays));

        /// <summary>How the claim gets its size – depends on the settings (flags / growing / follows the base).</summary>
        private static string HowItWorks =>
            Staking.Enabled
                ? L.T("how.flags", ("flags", ClaimGeometry.FlagsNeeded),
                    ("min", $"{ClaimGeometry.StakeMinSize:0} x {ClaimGeometry.StakeMinSize:0}"), ("max", L.MaxSize))
                : ClaimActions.Growing
                    ? L.T("how.growing", ("anchor", A), ("free", Plugin.Settings.StartFreeArea.Value))
                    : L.T("how.fit", ("anchor", A), ("max", L.MaxSize));

        public static string FlagsResize => L.T("flags.resize");

        /// <summary>For players on an IP connection without a PIN: a tip to set one (null otherwise).</summary>
        public static string PinTipFor(ulong steamId)
        {
            var client = Notify.ClientOf(steamId);
            if (client == null || SenderContext.IsVerifiedConnection(client.connectionId, steamId) || Pins.HasPin(steamId)) return null;
            return L.T("pin.tip", ("max", Pin.MaxLength));
        }

        /// <summary>The window that comes with the first thing a player builds (no claim yet).</summary>
        /// <summary>Short: what this is, the verdict for this spot, where to read more.</summary>
        public static string Guide(string verdict) => Join(
            L.T("guide.title") + "\n" +
            L.T("guide.here", ("verdict", verdict)) + "\n" +
            (Staking.Enabled ? L.T("guide.next_flags", ("flags", ClaimGeometry.FlagsNeeded)) : L.T("guide.next", ("anchor", A))));

        /// <summary>Short: name and size, how to add a friend, where to read more (+ notes that matter now).</summary>
        public static string Created(Claim c, string note = null, string pinTip = null) => Join(
            L.T("created.title", ("claim", c.DisplayName)) + "\n" +
            L.T("created.size", ("w", $"{c.Width:0}"), ("d", $"{c.Depth:0}")) + "\n" +
            L.T("created.add") + "\n" +
            L.T("more"),
            note,
            pinTip);

        public const int HelpPages = 4;

        /// <summary>
        /// '/claim help [page]': four windows – 1 your property and the rules, 2 size (with a little map of your claim),
        /// 3 claim slots, donations and expiry, 4 all commands. The game's window shows TextMeshPro formatting (bold,
        /// centered, smaller, indented, highlighted), so the texts in the language files may contain those tags.
        /// </summary>
        public static string Help(Claim mine, Role myRole, bool admin, int page, ulong viewer, float x, float z)
        {
            page = Math.Max(1, Math.Min(HelpPages, page));
            var parts = new List<string> { "<align=center><b>" + L.T("help.window_title") + "</b></align>" };
            bool fields = Slots.FieldMode;
            switch (page)
            {
                case 1:
                    parts.Add(Property(mine, myRole, viewer));
                    parts.Add(Heading("help.h_roles") + "\n" + L.T("help.roles"));
                    parts.Add(L.T("help.not_permitted", ("landing", $"{LandingDistance:0}"), ("gap", $"{ClaimGeometry.Gap:0}")) +
                              (Staking.Enabled ? "" : "\n" + L.T("help.moving", ("anchor", A))));
                    break;
                case 2:
                    string size = Staking.Enabled
                        ? L.T("help.size_flags", ("flags", ClaimGeometry.FlagsNeeded), ("max", L.MaxSize))
                        : fields
                            ? L.T("help.size_fields", ("anchor", A), ("free", Plugin.Settings.StartFreeArea.Value),
                                ("perhour", $"{Plugin.Settings.EarnedAreaPerHour.Value:0.#}"))
                            : L.T("help.size_fit", ("anchor", A), ("max", L.MaxSize));
                    parts.Add(Heading("help.h_size") + "\n" + size);
                    if (fields && mine != null && mine.HasFields)
                    {
                        string map = Map(mine, x, z);
                        if (map != null) parts.Add(Heading("help.h_map") + "\n" + map);
                    }
                    break;
                case 3:
                    if (fields)
                        parts.Add(Heading("help.h_slots") + "\n" + L.T("help.slots", ("max", Slots.Max)) + "\n" +
                                  L.T("help.donate") + "\n" + L.T("help.penalty", ("hours", Slots.PenaltyMinutes / 60)));
                    parts.Add(Heading("help.h_expiry") + "\n" + L.T("help.expiry", ("days", ExpiryDays)));
                    break;
                default:
                    var commands = new List<string> { Heading("help.h_commands"), L.T("help.cmd_look") };
                    if (fields) commands.Add(L.T("help.cmd_grow"));
                    commands.Add(L.T("help.cmd_people"));
                    if (fields) commands.Add(L.T("help.cmd_slots"));
                    commands.Add(L.T("help.cmd_owning"));
                    commands.Add(L.T("help.cmd_pin"));
                    if (admin) commands.Add(L.T("help.cmd_admin"));
                    parts.Add(string.Join("\n", commands));
                    parts.Add("<align=center><i>" + L.T("help.slogan") + "</i></align>");
                    break;
            }
            parts.Add("<i>" + (page < HelpPages
                ? L.T("help.next", ("page", page), ("pages", HelpPages), ("next", page + 1))
                : L.T("help.first", ("page", page), ("pages", HelpPages))) + "</i>");
            return Window(parts);
        }

        /// <summary>Page 1: the claim the player stands in (or their only one), their slots, account and donations.</summary>
        private static string Property(Claim mine, Role myRole, ulong viewer)
        {
            var own = new List<string> { Heading("help.h_property") };
            bool fields = Slots.FieldMode;
            if (mine == null) own.Add(L.T("help.yours_none"));
            else
            {
                own.Add(L.T(mine.HasFields ? "help.yours_fields" : "help.yours", ("claim", mine.DisplayName),
                    ("w", $"{mine.Width:0}"), ("d", $"{mine.Depth:0}"), ("fields", mine.Fields.Count),
                    ("role", L.T("role_short." + myRole.ToString().ToLowerInvariant()))));
                if (fields && mine.HasFields && mine.IsActive && myRole == Role.Owner)
                {
                    var donors = new List<string>();
                    foreach (var d in mine.Donations)
                        if (d.Slots > 0) donors.Add(d.Slots > 1 ? $"{d.Name} ({d.Slots})" : d.Name);
                    own.Add(L.T(donors.Count > 0 ? "help.slots_used_donated" : "help.slots_used",
                        ("need", mine.SlotsNeeded), ("own", mine.OwnSlots), ("donors", string.Join(", ", donors)),
                        ("paid", mine.PaidSlots)));
                    if (mine.MissingSlots > 0)
                        own.Add("<b>" + L.T("help.penalty_open", ("missing", mine.MissingSlots), ("time", TimeLeft(mine))) + "</b>");
                }
                var friends = new List<string>();
                var guests = new List<string>();
                foreach (var m in mine.Members) (m.Role == Role.Guest ? guests : friends).Add(m.Name);
                own.Add(L.T("help.members", ("friends", friends.Count > 0 ? string.Join(", ", friends) : "–"),
                    ("guests", guests.Count > 0 ? string.Join(", ", guests) : "–")));
            }
            if (fields && Identity.IsValidSteamId(viewer))
            {
                own.Add(L.T("help.free_slots", ("free", Math.Max(0, Slots.Free(viewer))), ("max", Slots.Max)));
                own.Add(L.T("help.account", ("area", $"{Slots.Balance(viewer):0}")));
                foreach (var c in Plugin.Store.Index.Active)
                {
                    int n = c.DonatedBy(viewer);
                    if (n > 0) own.Add(L.T("help.you_donate", ("n", n), ("claim", c.DisplayName)));
                }
            }
            return string.Join("\n", own);
        }

        private static string TimeLeft(Claim c)
        {
            int left = c.PenaltyLeft > 0 ? c.PenaltyLeft : Slots.PenaltyMinutes;
            return $"{left / 60}:{left % 60:00} h";
        }

        // ------------------------------------------------------------------ map

        private const int MapMaxColumns = 30, MapMaxRows = 20;
        private const string MapField = "#5E9C5EFF", MapAnchor = "#E0B020FF"; // opaque: rows that touch must not darken each other
        // a non-breaking space: the window would drop ordinary spaces at the end of a line and shift the rows
        private static readonly string Blank = ((char)0xA0).ToString();

        /// <summary>
        /// A little map of a field claim: one highlighted cell per 10 x 10 m field, north at the top (as the in-game
        /// compass: east is right). The autolathe's field is yellow, "•" is where the player stands. Null if too big.
        /// </summary>
        private static string Map(Claim c, float x, float z)
        {
            int i0 = int.MaxValue, i1 = int.MinValue, j0 = int.MaxValue, j1 = int.MinValue;
            foreach (var f in c.Fields)
            {
                i0 = Math.Min(i0, f.I); i1 = Math.Max(i1, f.I);
                j0 = Math.Min(j0, f.J); j1 = Math.Max(j1, f.J);
            }
            if (i1 - i0 + 1 > MapMaxColumns || j1 - j0 + 1 > MapMaxRows) return null;
            c.FieldAt(c.AnchorX, c.AnchorZ, out int ai, out int aj);
            c.FieldAt(x, z, out int pi, out int pj);
            var rows = new List<string> { "<b>" + L.T("help.map_north") + "</b>" };
            for (int j = j0; j <= j1; j++)            // north (-Z) at the top
            {
                var row = new System.Text.StringBuilder();
                for (int i = i1; i >= i0; i--)        // west (+X) on the left, east (-X) on the right
                {
                    string mark = i == pi && j == pj ? "•" : Blank;
                    if (c.HasField(i, j))
                        row.Append("<mark=").Append(i == ai && j == aj ? MapAnchor : MapField).Append('>').Append(mark).Append("</mark>");
                    else row.Append(mark);
                }
                rows.Add(row.ToString());
            }
            string legend = L.T("help.map_legend", ("anchor", $"<mark={MapAnchor}>{Blank}</mark> " + A), ("field", $"<mark={MapField}>{Blank}</mark>"));
            return "<align=center><mspace=1.2em>" + string.Join("\n", rows) + "</mspace>\n" + legend + "</align>";
        }

        // ------------------------------------------------------------------ penalty

        /// <summary>The window when a claim is short of slots (a friend took theirs back): pay or it shrinks.</summary>
        public static string Penalty(Claim c, double steel, float built) => Join(
            "<b>" + L.T("penalty.title", ("claim", c.DisplayName)) + "</b>",
            L.T("penalty.why", ("need", c.SlotsNeeded), ("missing", c.MissingSlots)),
            L.T("penalty.pay", ("steel", $"{steel:0}"), ("built", $"{built:0}"), ("anchor", A)),
            L.T("penalty.or", ("time", TimeLeft(c)), ("anchor", A)),
            L.T("penalty.donate"));

        /// <summary>After the claim shrank to 50 x 50 m (not paid, or nothing built that would be lost).</summary>
        public static string PenaltyShrunk(Claim c, float credit, bool nothingLost) => Join(
            "<b>" + L.T("penalty.shrunk", ("claim", c.DisplayName), ("w", $"{c.Width:0}"), ("d", $"{c.Depth:0}")) + "</b>",
            L.T(nothingLost ? "penalty.shrunk_nothing" : "penalty.shrunk_unpaid", ("anchor", A)),
            credit >= 1f ? L.T("penalty.credit", ("area", $"{credit:0}")) : null);

        /// <summary>
        /// The game's window puts a big paragraph gap after every line break ("\n"). Inside a section the lines are
        /// therefore joined with LINE SEPARATOR (U+2028), which breaks the line without that gap; only the sections
        /// themselves are separated by one normal break. Result: even spacing.
        /// </summary>
        private static string Window(List<string> parts)
        {
            var sections = new List<string>();
            foreach (var p in parts)
                if (!string.IsNullOrEmpty(p)) sections.Add(p.Trim('\n').Replace('\n', LineSeparator));
            return "<size=90%><align=left>" + string.Join("\n", sections) + "</align></size>";
        }

        private static string Heading(string key) => "<b>" + L.T(key) + "</b>";

        /// <summary>The same text without formatting tags (for the server console). Placeholders like &lt;player&gt; stay.</summary>
        public static string Plain(string text) =>
            System.Text.RegularExpressions.Regex.Replace((text ?? "").Replace(LineSeparator, '\n'), "</?(b|i|align|size|indent|line-indent|mark|mspace)(=[^>]*)?>", "");

        /// <summary>U+2028 LINE SEPARATOR: a line break without the window's paragraph gap.</summary>
        private const char LineSeparator = (char)0x2028;

        public static string TakenOver(Claim c) => Join(
            L.T("takenover.title", ("claim", c.DisplayName)) + "\n" +
            L.T("takenover.size", ("w", $"{c.Width:0}"), ("d", $"{c.Depth:0}")),
            L.T("takenover.add") + "\n" +
            (c.Staked ? L.T("takenover.grow_staked") : ClaimActions.Growing ? L.T("takenover.grow_meters") : L.T("takenover.grow_fit")),
            Expiry,
            L.T("more"));

        public static string Refused(string reason, string hint) => Join(
            L.T("refused", ("anchor", A), ("reason", reason)) + (hint != null ? "\n" + hint : ""),
            L.T("refused.tip", ("anchor", A)));

        public static string StakeRefused(string reason, string hint) => Join(
            L.T("stake.refused", ("reason", reason)) + (hint != null ? "\n" + hint : ""),
            L.T("stake.refused.tip"));

        public static string Ended(Claim c) => L.T("ended", ("claim", c.DisplayName), ("anchor", A));

        public static string StakeEnded(Claim c) => L.T("stake.ended", ("claim", c.DisplayName), ("flags", ClaimGeometry.FlagsNeeded));

        public static string CheatsDisabled => L.T("cheats.disabled");

        /// <summary>Paragraphs with an empty line between them.</summary>
        private static string Join(params string[] paragraphs)
        {
            var list = new List<string>();
            foreach (var p in paragraphs)
                if (!string.IsNullOrEmpty(p)) list.Add(p.TrimEnd('\n'));
            return string.Join("\n\n", list);
        }
    }
}
