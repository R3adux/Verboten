using System;
using System.Collections.Generic;

namespace Verboten.Core
{
    /// <summary>A start location where new players land, with its spawn radius (from the game data).</summary>
    public struct LandingZone
    {
        public float X;
        public float Z;
        public float SpawnRadius;
        /// <summary>No claim edge may come closer than this to the center.</summary>
        public float ProtectedRadius => SpawnRadius + ClaimGeometry.LandingMargin;
    }

    /// <summary>All size and distance rules in one place (design section 0.1/0.2).</summary>
    public static class ClaimGeometry
    {
        public const float StartReach = 5f;     // a new claim reaches 5 m in every direction
        public const float MinReach = 5f;       // a side never comes closer than 5 m to the anchor
        public const float MaxReach = 45f;      // a side never reaches further than 45 m from the anchor
        public const float MaxSpan = 50f;       // width and depth at most 50 m
        public const float Gap = 10f;           // edge to edge between claims
        public const float Zone = 10f;          // approach zone around every claim
        public const float LandingMargin = 20f; // spawn radius + this = protected landing zone
        public const float BaseMargin = 5f;     // growing switched off: a claim covers its base plus this
        public const int FlagsNeeded = 4;       // anchor "Flag": a flag at each corner stakes out a claim
        public const float StakeRange = 50f;    // flags this close to the newest one count together
        public const float StakeMinSize = 10f;  // a staked claim is at least 10 x 10 m

        /// <summary>
        /// The rectangle around staked flags: their outermost positions, but at least <see cref="StakeMinSize"/> in each
        /// direction (widened evenly), so flags in a row still give a usable claim.
        /// </summary>
        public static void StakeBounds(IEnumerable<(float X, float Z)> flags, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = minZ = float.MaxValue;
            maxX = maxZ = float.MinValue;
            foreach (var f in flags)
            {
                minX = Math.Min(minX, f.X); maxX = Math.Max(maxX, f.X);
                minZ = Math.Min(minZ, f.Z); maxZ = Math.Max(maxZ, f.Z);
            }
            Widen(ref minX, ref maxX);
            Widen(ref minZ, ref maxZ);
        }

        private static void Widen(ref float min, ref float max)
        {
            float missing = StakeMinSize - (max - min);
            if (missing <= 0f) return;
            min -= missing / 2f;
            max += missing / 2f;
        }

        // ------------------------------------------------------------------ placement

        public sealed class Problem
        {
            /// <summary>English, for the log and tests. Players get <see cref="Key"/> in their language.</summary>
            public string Reason;
            /// <summary>Text key: "problem.landing" (needs Meters) or "problem.gap" (Meters, Other).</summary>
            public string Key;
            public int Meters;
            public string Other;
            /// <summary>Suggested move (meters along X = east, Z = north) to fix it; 0/0 if none.</summary>
            public float MoveX;
            public float MoveZ;
        }

        /// <summary>
        /// Checks a claim against other ACTIVE claims (gap) and landing zones. Abandoned claims are not passed in:
        /// they give way (see <see cref="ShrinkToClear"/>). Returns null if the claim is fine.
        /// </summary>
        public static Problem Check(Claim claim, IEnumerable<Claim> activeOthers, IEnumerable<LandingZone> zones)
        {
            foreach (var z in zones)
            {
                float d = claim.DistanceTo(z.X, z.Z);
                if (d >= z.ProtectedRadius) continue;
                float need = z.ProtectedRadius - d;
                Away(claim.AnchorX - z.X, claim.AnchorZ - z.Z, need, out float mx, out float mz);
                return new Problem
                {
                    Reason = $"{Round(need)} m too close to the landing zone",
                    Key = "problem.landing", Meters = Round(need),
                    MoveX = mx, MoveZ = mz,
                };
            }
            foreach (var other in activeOthers)
            {
                if (other == null || other.Id == claim.Id) continue;
                // the same owner's claims may touch each other, they just must not overlap
                if (other.OwnerId == claim.OwnerId && claim.OwnerId != 0)
                {
                    if (!claim.Overlaps(other)) continue;
                    return new Problem
                    {
                        Reason = $"overlaps {other.DisplayName}",
                        Key = "problem.gap", Meters = 1, Other = other.DisplayName,
                    };
                }
                if (claim.Bounds.GapTo(other.Bounds) >= Gap) continue; // far apart: no need to compare fields
                float gap = claim.GapTo(other);
                if (gap >= Gap && !claim.Overlaps(other)) continue;
                float need = Gap - gap;
                // move along the axis where the claims are already apart (the cheaper way out)
                float cx = (claim.MinX + claim.MaxX) / 2f - (other.MinX + other.MaxX) / 2f;
                float cz = (claim.MinZ + claim.MaxZ) / 2f - (other.MinZ + other.MaxZ) / 2f;
                float overlapX = Math.Min(claim.MaxX, other.MaxX) - Math.Max(claim.MinX, other.MinX) + Gap;
                float overlapZ = Math.Min(claim.MaxZ, other.MaxZ) - Math.Max(claim.MinZ, other.MinZ) + Gap;
                float mx = 0f, mz = 0f;
                if (overlapX <= overlapZ) { need = Math.Max(need, overlapX); mx = cx >= 0 ? need : -need; }
                else { need = Math.Max(need, overlapZ); mz = cz >= 0 ? need : -need; }
                return new Problem
                {
                    Reason = $"{Round(need)} m too close to {other.DisplayName} (claims need {Gap:0} m between them)",
                    Key = "problem.gap", Meters = Round(need), Other = other.DisplayName,
                    MoveX = mx, MoveZ = mz,
                };
            }
            return null;
        }

        private static void Away(float dx, float dz, float meters, out float mx, out float mz)
        {
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.01f) { mx = meters; mz = 0f; return; }
            mx = dx / len * meters;
            mz = dz / len * meters;
        }

        private static int Round(float meters) => Math.Max(1, (int)Math.Ceiling(meters));

        /// <summary>"north", "south-east", … for a world direction (in-game compass: north = -Z, east = -X).</summary>
        public static string Compass(float dx, float dz)
        {
            if (Math.Abs(dx) < 0.01f && Math.Abs(dz) < 0.01f) return "";
            double angle = Math.Atan2(-dx, -dz) * 180.0 / Math.PI; // 0 = north, 90 = east
            if (angle < 0) angle += 360.0;
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return names[(int)Math.Round(angle / 45.0) % 8];
        }

        // ------------------------------------------------------------------ growing

        /// <summary>
        /// Checks the per-claim limits after moving a side to <paramref name="reach"/>. Null = fine, otherwise a text key:
        /// "reach.min", "reach.max" or "reach.span".
        /// </summary>
        public static string CheckReach(Claim claim, Side side, float reach)
        {
            if (reach < MinReach) return "reach.min";
            if (reach > MaxReach) return "reach.max";
            var probe = claim.Clone();
            probe.SetReach(side, reach);
            if (probe.Width > MaxSpan + 0.001f || probe.Depth > MaxSpan + 0.001f) return "reach.span";
            return null;
        }

        /// <summary>
        /// Steel for <paramref name="extra"/> m² on top of a claim that already has <paramref name="startArea"/> m².
        /// The price per m² rises with the area: perSquareMeter × (1 + increase × area / 100), summed up exactly.
        /// </summary>
        public static double SteelForArea(double startArea, double extra, double perSquareMeter, double increasePer100SquareMeters)
        {
            if (extra <= 0) return 0;
            double total = perSquareMeter * (extra + increasePer100SquareMeters / 100.0 * (startArea * extra + extra * extra / 2.0));
            return Math.Ceiling(total - 1e-9);
        }

        /// <summary>The claim side a player faces (look direction on the ground; in-game compass: north = -Z, east = -X).</summary>
        public static Side Facing(float forwardX, float forwardZ)
        {
            if (Math.Abs(forwardZ) >= Math.Abs(forwardX)) return forwardZ < 0 ? Side.North : Side.South;
            return forwardX < 0 ? Side.East : Side.West;
        }

        // ------------------------------------------------------------------ growing switched off: claims follow the base

        /// <summary>
        /// Moves the edges of <paramref name="claim"/> outwards – meter by meter and in turns – until it covers the
        /// rectangle plus <see cref="BaseMargin"/>, as far as the size limits, other ACTIVE claims, landing zones and
        /// <paramref name="blocked"/> spots (structures of other active players) allow. Edges never move inwards.
        /// Blocked spots stay out of the claim's approach zone (<see cref="Zone"/>), so nobody's own building ends up
        /// where they may no longer touch it; spots that are already that close only have to stay outside.
        /// Returns true if the claim changed.
        /// </summary>
        public static bool GrowToCover(Claim claim, float minX, float maxX, float minZ, float maxZ,
            IReadOnlyList<Claim> activeOthers, IReadOnlyList<LandingZone> zones, IReadOnlyList<(float X, float Z)> blocked)
        {
            var keep = new List<(float X, float Z, float Distance)>();
            if (blocked != null)
                foreach (var b in blocked)
                    keep.Add((b.X, b.Z, claim.Near(b.X, b.Z, Zone) ? 0f : Zone));

            // world edges: 0 = high X, 1 = low X, 2 = high Z, 3 = low Z
            float[] want =
            {
                Math.Min(Math.Max(claim.MaxX, maxX + BaseMargin), claim.AnchorX + MaxReach),
                Math.Max(Math.Min(claim.MinX, minX - BaseMargin), claim.AnchorX - MaxReach),
                Math.Min(Math.Max(claim.MaxZ, maxZ + BaseMargin), claim.AnchorZ + MaxReach),
                Math.Max(Math.Min(claim.MinZ, minZ - BaseMargin), claim.AnchorZ - MaxReach),
            };
            bool changed = false, progress = true;
            while (progress)
            {
                progress = false;
                for (int edge = 0; edge < 4; edge++)
                {
                    float current = GetEdge(claim, edge);
                    bool up = edge % 2 == 0;
                    float left = up ? want[edge] - current : current - want[edge];
                    if (left <= 0.001f) continue;
                    float step = Math.Min(1f, left);
                    SetEdge(claim, edge, up ? current + step : current - step);
                    if (Fits(claim, activeOthers, zones, keep)) { changed = progress = true; continue; }
                    SetEdge(claim, edge, current);
                    want[edge] = current; // stuck: something is in the way
                }
            }
            return changed;
        }

        private static bool Fits(Claim c, IReadOnlyList<Claim> activeOthers, IReadOnlyList<LandingZone> zones,
            List<(float X, float Z, float Distance)> keep)
        {
            if (c.Width > MaxSpan + 0.001f || c.Depth > MaxSpan + 0.001f) return false;
            foreach (var b in keep)
                if (c.Near(b.X, b.Z, b.Distance)) return false;
            return Check(c, activeOthers, zones) == null;
        }

        private static float GetEdge(Claim c, int edge) =>
            edge == 0 ? c.MaxX : edge == 1 ? c.MinX : edge == 2 ? c.MaxZ : c.MinZ;

        private static void SetEdge(Claim c, int edge, float value)
        {
            switch (edge)
            {
                case 0: c.MaxX = value; break;
                case 1: c.MinX = value; break;
                case 2: c.MaxZ = value; break;
                default: c.MinZ = value; break;
            }
        }

        // ------------------------------------------------------------------ abandoned claims give way

        /// <summary>
        /// Cuts an abandoned claim back so it keeps <paramref name="gap"/> meters from the given rectangle
        /// (use gap 0 and a zero-size rectangle for a single structure). Picks the cut that loses the least area.
        /// Returns false if no side can be cut without coming closer than <see cref="MinReach"/> to the anchor –
        /// then the abandoned claim should be dissolved.
        /// </summary>
        public static bool ShrinkToClear(Claim abandoned, float minX, float maxX, float minZ, float maxZ, float gap)
        {
            if (abandoned.HasFields)
            {
                // field claims lose the fields in the way (and whatever no longer hangs together with the anchor)
                var r = new Box(minX, maxX, minZ, maxZ);
                abandoned.KeepFields((i, j) =>
                {
                    var f = abandoned.FieldRect(i, j);
                    return gap > 0f ? f.GapTo(r) >= gap && !f.Overlaps(r) : !TouchesRect(f, r);
                });
                return abandoned.HasFields && abandoned.KeepAnchorPart();
            }
            // already clear?
            bool clear = gap > 0f ? abandoned.GapTo(minX, maxX, minZ, maxZ) >= gap && !OverlapsRect(abandoned, minX, maxX, minZ, maxZ)
                                  : !TouchesRect(abandoned, minX, maxX, minZ, maxZ);
            if (clear) return true;

            // Work on the four world edges directly (independent of what the compass calls them).
            float extra = gap > 0f ? 0f : 0.01f;
            int best = -1;
            float bestValue = 0f, bestLoss = float.MaxValue;
            for (int edge = 0; edge < 4; edge++)
            {
                float current, value, reach, oldReach, length;
                switch (edge)
                {
                    case 0: // the high-X edge moves down to just before the obstacle
                        current = abandoned.MaxX; value = minX - gap - extra;
                        reach = value - abandoned.AnchorX; oldReach = current - abandoned.AnchorX; length = abandoned.Depth; break;
                    case 1: // the low-X edge moves up
                        current = abandoned.MinX; value = maxX + gap + extra;
                        reach = abandoned.AnchorX - value; oldReach = abandoned.AnchorX - current; length = abandoned.Depth; break;
                    case 2: // the high-Z edge
                        current = abandoned.MaxZ; value = minZ - gap - extra;
                        reach = value - abandoned.AnchorZ; oldReach = current - abandoned.AnchorZ; length = abandoned.Width; break;
                    default: // the low-Z edge
                        current = abandoned.MinZ; value = maxZ + gap + extra;
                        reach = abandoned.AnchorZ - value; oldReach = abandoned.AnchorZ - current; length = abandoned.Width; break;
                }
                if (reach >= oldReach || reach < MinReach) continue; // cutting here would not help / too small
                float loss = (oldReach - reach) * length;
                if (loss < bestLoss) { best = edge; bestValue = value; bestLoss = loss; }
            }
            if (best < 0) return false;
            switch (best)
            {
                case 0: abandoned.MaxX = bestValue; break;
                case 1: abandoned.MinX = bestValue; break;
                case 2: abandoned.MaxZ = bestValue; break;
                default: abandoned.MinZ = bestValue; break;
            }
            // one cut may not be enough for a rectangle diagonal to the corner – try again
            return ShrinkToClear(abandoned, minX, maxX, minZ, maxZ, gap);
        }

        /// <summary>
        /// Cuts an abandoned claim back so it keeps <paramref name="gap"/> meters from another claim – from its actual
        /// fields, not the rectangle around them (an L must not cut away what lies next to its empty corner).
        /// </summary>
        public static bool ShrinkToClear(Claim abandoned, Claim other)
        {
            float gap = GapBetween(abandoned, other);
            foreach (var p in other.Parts())
            {
                bool clear = true;
                foreach (var a in abandoned.Parts())
                    if (!Apart(a, p, gap)) { clear = false; break; }
                if (clear) continue;
                if (abandoned.HasFields)
                {
                    abandoned.KeepFields((i, j) => Apart(abandoned.FieldRect(i, j), p, gap));
                    if (!abandoned.HasFields || !abandoned.KeepAnchorPart()) return false;
                }
                else if (!ShrinkToClear(abandoned, p.MinX, p.MaxX, p.MinZ, p.MaxZ, Math.Max(gap, 0.001f))) return false;
            }
            return true;
        }

        /// <summary>The gap two claims need: none between claims of the same owner (they may touch), else <see cref="Gap"/>.</summary>
        public static float GapBetween(Claim a, Claim b) => a.OwnerId == b.OwnerId && a.OwnerId != 0 ? 0f : Gap;

        /// <summary>Do two claims keep the gap they need (<see cref="GapBetween"/>)?</summary>
        public static bool Apart(Claim a, Claim b)
        {
            float gap = GapBetween(a, b);
            if (!a.Bounds.Overlaps(b.Bounds) && a.Bounds.GapTo(b.Bounds) >= gap) return true; // the rectangles around already keep it
            return !a.Overlaps(b) && (gap <= 0f || a.GapTo(b) >= gap);
        }

        private static bool Apart(Box a, Box b, float gap) => !a.Overlaps(b) && (gap <= 0f || a.GapTo(b) >= gap);

        /// <summary>
        /// A new field claim uses the grid of the nearest other field claim of the same owner, so all their claims fit
        /// together and can touch without a strip in between. The anchor stays inside the first field.
        /// </summary>
        public static void AlignToOwnClaims(Claim candidate, IEnumerable<Claim> claims)
        {
            if (!candidate.HasFields) return;
            Claim best = null;
            float bestGap = float.MaxValue;
            foreach (var o in claims)
            {
                if (o == null || !o.IsActive || !o.HasFields || o.Id == candidate.Id || o.OwnerId != candidate.OwnerId) continue;
                float g = o.GapTo(candidate);
                if (g < bestGap) { best = o; bestGap = g; }
            }
            if (best == null) return;
            best.FieldAt(candidate.AnchorX, candidate.AnchorZ, out int i, out int j);
            candidate.StartFields(best.GridX + i * Claim.FieldSize, best.GridZ + j * Claim.FieldSize);
        }

        private static bool OverlapsRect(Claim c, float minX, float maxX, float minZ, float maxZ) =>
            c.MinX < maxX && minX < c.MaxX && c.MinZ < maxZ && minZ < c.MaxZ;

        private static bool TouchesRect(Claim c, float minX, float maxX, float minZ, float maxZ) =>
            c.MinX <= maxX && minX <= c.MaxX && c.MinZ <= maxZ && minZ <= c.MaxZ;

        private static bool TouchesRect(Box a, Box b) =>
            a.MinX <= b.MaxX && b.MinX <= a.MaxX && a.MinZ <= b.MaxZ && b.MinZ <= a.MaxZ;

        // ------------------------------------------------------------------ fields

        /// <summary>One field step in the direction of a side (north = -Z, east = -X).</summary>
        public static void Step(Side side, out int di, out int dj)
        {
            di = side == Side.East ? -1 : side == Side.West ? 1 : 0;
            dj = side == Side.North ? -1 : side == Side.South ? 1 : 0;
        }

        /// <summary>
        /// '/claim grow': the field to add – from the field the player stands in (or the claim's field nearest to
        /// them) straight on in the direction they look, the first field that is not part of the claim yet.
        /// </summary>
        public static void GrowField(Claim c, float x, float z, Side side, out int i, out int j)
        {
            Start(c, x, z, out i, out j);
            Step(side, out int di, out int dj);
            while (c.HasField(i, j)) { i += di; j += dj; }
        }

        /// <summary>
        /// '/claim shrink' (admins): the outermost field of the claim in the direction the admin looks, on their row.
        /// False if there is none.
        /// </summary>
        public static bool ShrinkField(Claim c, float x, float z, Side side, out int i, out int j)
        {
            Start(c, x, z, out i, out j);
            Step(side, out int di, out int dj);
            if (!c.HasField(i, j)) return false;
            while (c.HasField(i + di, j + dj)) { i += di; j += dj; }
            return true;
        }

        private static void Start(Claim c, float x, float z, out int i, out int j)
        {
            c.FieldAt(x, z, out i, out j);
            if (c.HasField(i, j)) return;
            int bi = i, bj = j, best = int.MaxValue;
            foreach (var f in c.Fields)
            {
                int d = Math.Abs(f.I - bi) + Math.Abs(f.J - bj);
                if (d < best) { best = d; i = f.I; j = f.J; }
            }
        }

        /// <summary>The fields within 50 x 50 m (5 x 5 fields) around the anchor's field: what is left after a penalty is not paid.</summary>
        public static bool InPenaltySquare(Claim c, int i, int j)
        {
            c.FieldAt(c.AnchorX, c.AnchorZ, out int ai, out int aj);
            return Math.Abs(i - ai) <= 2 && Math.Abs(j - aj) <= 2;
        }

        /// <summary>
        /// The fields a claim keeps when it shrinks for an unpaid penalty: those within 5 x 5 fields (50 x 50 m) around
        /// the anchor's field that still hang together with it. (Works on a copy; the claim is not changed.)
        /// </summary>
        public static Claim PenaltyRest(Claim c)
        {
            var rest = c.Clone();
            rest.KeepFields((i, j) => InPenaltySquare(rest, i, j));
            if (!rest.HasFields || !rest.KeepAnchorPart())
            {
                // the anchor's own field is not part of the claim (should not happen): keep just that field
                rest.Fields.Clear();
                rest.FieldAt(rest.AnchorX, rest.AnchorZ, out int ai, out int aj);
                rest.AddField(ai, aj);
            }
            return rest;
        }

        // ------------------------------------------------------------------ outline (claims of one owner that touch)

        /// <summary>
        /// The outer corners of the area covered by <paramref name="parts"/> (rectangles that may touch): an L has 6,
        /// a + has 12. Points where the parts only meet inside the area are left out.
        /// </summary>
        public static List<(float X, float Z)> OutlineCorners(IList<Box> parts)
        {
            var result = new List<(float X, float Z)>();
            var seen = new HashSet<(int, int)>();
            const float e = 0.05f;
            foreach (var p in parts)
                foreach (var (x, z) in new[] { (p.MinX, p.MinZ), (p.MaxX, p.MinZ), (p.MaxX, p.MaxZ), (p.MinX, p.MaxZ) })
                {
                    if (!seen.Add(((int)Math.Round(x * 10f), (int)Math.Round(z * 10f)))) continue;
                    bool a = Covered(parts, x - e, z - e), b = Covered(parts, x + e, z - e);
                    bool c = Covered(parts, x + e, z + e), d = Covered(parts, x - e, z + e);
                    int n = (a ? 1 : 0) + (b ? 1 : 0) + (c ? 1 : 0) + (d ? 1 : 0);
                    // 1 or 3 quarters covered: a corner; 2 diagonal quarters: two corners meeting
                    if (n == 1 || n == 3 || (n == 2 && a == c)) result.Add((x, z));
                }
            return result;
        }

        /// <summary>Points every <paramref name="spacing"/> m along the outer edge of the area covered by <paramref name="parts"/>.</summary>
        public static List<(float X, float Z)> OutlinePoints(IList<Box> parts, float spacing)
        {
            var result = new List<(float X, float Z)>();
            const float e = 0.05f;
            foreach (var p in parts)
            {
                AddEdge(parts, result, p.MinX, p.MaxX, p.MinZ, true, -e, spacing);  // low-Z edge, outside is -Z
                AddEdge(parts, result, p.MinX, p.MaxX, p.MaxZ, true, e, spacing);   // high-Z edge
                AddEdge(parts, result, p.MinZ, p.MaxZ, p.MinX, false, -e, spacing); // low-X edge
                AddEdge(parts, result, p.MinZ, p.MaxZ, p.MaxX, false, e, spacing);  // high-X edge
            }
            return result;
        }

        private static void AddEdge(IList<Box> parts, List<(float X, float Z)> result, float from, float to, float at,
            bool alongX, float outside, float spacing)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((to - from) / spacing));
            for (int s = 0; s < steps; s++)
            {
                float v = from + (to - from) * (s + 0.5f) / steps;
                float x = alongX ? v : at, z = alongX ? at : v;
                // an edge between two parts is inside the area: skip it
                if (Covered(parts, alongX ? x : x + outside, alongX ? z + outside : z)) continue;
                result.Add((x, z));
            }
        }

        private static bool Covered(IList<Box> parts, float x, float z)
        {
            foreach (var p in parts)
                if (x > p.MinX && x < p.MaxX && z > p.MinZ && z < p.MaxZ) return true;
            return false;
        }

        /// <summary>Groups of claims that touch each other and belong to the same owner (for showing them as one area).</summary>
        public static List<List<Claim>> TouchingGroups(IList<Claim> claims)
        {
            var groups = new List<List<Claim>>();
            var done = new HashSet<string>();
            foreach (var c in claims)
            {
                if (!done.Add(c.Id)) continue;
                var group = new List<Claim> { c };
                for (int k = 0; k < group.Count; k++)
                    foreach (var o in claims)
                        if (!done.Contains(o.Id) && o.OwnerId == group[k].OwnerId && group[k].GapTo(o) <= 0.01f)
                        {
                            done.Add(o.Id);
                            group.Add(o);
                        }
                groups.Add(group);
            }
            return groups;
        }
    }
}
