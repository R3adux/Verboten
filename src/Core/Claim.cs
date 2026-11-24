using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Verboten.Core
{
    public sealed class ClaimMember
    {
        [XmlAttribute] public ulong SteamId;
        [XmlAttribute] public Role Role = Role.Friend;
        /// <summary>Last known display name. Only for messages, never for identification.</summary>
        [XmlAttribute] public string Name;
    }

    /// <summary>One 10 x 10 m field of a claim, counted from the field around the grid origin (0/0).</summary>
    public sealed class ClaimField
    {
        [XmlAttribute] public int I;
        [XmlAttribute] public int J;
    }

    /// <summary>A friend gives some of their claim slots to this claim.</summary>
    public sealed class Donation
    {
        [XmlAttribute] public ulong SteamId;
        [XmlAttribute] public string Name;
        [XmlAttribute] public int Slots;
    }

    /// <summary>A rectangle on the ground (X/Z).</summary>
    public struct Box
    {
        public float MinX, MaxX, MinZ, MaxZ;
        public Box(float minX, float maxX, float minZ, float maxZ) { MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ; }

        public bool Near(float x, float z, float buffer) =>
            x >= MinX - buffer && x <= MaxX + buffer && z >= MinZ - buffer && z <= MaxZ + buffer;

        public bool Overlaps(Box o) => MinX < o.MaxX && o.MinX < MaxX && MinZ < o.MaxZ && o.MinZ < MaxZ;

        /// <summary>Edge-to-edge gap: the larger of the gaps along X and Z (0 if they overlap or touch).</summary>
        public float GapTo(Box o)
        {
            float dx = Math.Max(0f, Math.Max(o.MinX - MaxX, MinX - o.MaxX));
            float dz = Math.Max(0f, Math.Max(o.MinZ - MaxZ, MinZ - o.MaxZ));
            return Math.Max(dx, dz);
        }

        public float DistanceTo(float x, float z)
        {
            float dx = Math.Max(0f, Math.Max(MinX - x, x - MaxX));
            float dz = Math.Max(0f, Math.Max(MinZ - z, z - MaxZ));
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>Something that happened in a claim, for the "while you were away" summary.</summary>
    public sealed class Incident
    {
        [XmlAttribute] public long Minute;
        [XmlAttribute] public string Text;
    }

    public enum ClaimState
    {
        Active,
        /// <summary>Expired: not protected, but kept so someone can take it over.</summary>
        Abandoned,
    }

    /// <summary>
    /// Directions as the in-game compass shows them. The HUD shows heading = (yaw + 180) % 360, so 0 (north)
    /// is when facing -Z: North = -Z, South = +Z, East = -X, West = +X (Unity world axes).
    /// </summary>
    public enum Side { North, East, South, West }

    /// <summary>
    /// A rectangular column in the world (fixed corners, unlimited height and depth) with an anchor
    /// (autolathe or flag) somewhere inside. The four sides are measured from the anchor.
    /// No Unity types here so the core stays testable outside the game.
    /// </summary>
    public sealed class Claim
    {
        public const int MaxNameLength = 32;
        public const int MaxIncidents = 20;

        [XmlAttribute] public string Id = Guid.NewGuid().ToString("N");
        [XmlAttribute] public string Name;
        [XmlAttribute] public ulong OwnerId;
        [XmlAttribute] public string OwnerName;
        [XmlAttribute] public ClaimState State = ClaimState.Active;

        /// <summary>ReferenceId of the anchor (autolathe/flag); 0 = none.</summary>
        [XmlAttribute] public long AnchorId;
        /// <summary>ReferenceId of the tracker beacon placed outside an abandoned claim; 0 = none.</summary>
        [XmlAttribute] public long MarkerId;
        /// <summary>Staked out with flags (anchor setting "Flag"): the outermost flags inside it are the border.</summary>
        [XmlAttribute] public bool Staked;
        [XmlAttribute] public float AnchorX;
        [XmlAttribute] public float AnchorY;
        [XmlAttribute] public float AnchorZ;

        [XmlAttribute] public float MinX;
        [XmlAttribute] public float MaxX;
        [XmlAttribute] public float MinZ;
        [XmlAttribute] public float MaxZ;

        /// <summary>
        /// Field claims (owners grow them themselves): the claim is made of 10 x 10 m fields on a grid whose field
        /// (0/0) is centered on <see cref="GridX"/>/<see cref="GridZ"/> (where the first anchor stood). Min/Max are
        /// then the rectangle around all fields. No fields = the whole rectangle is the claim (flags, follows the base).
        /// </summary>
        [XmlAttribute] public float GridX;
        [XmlAttribute] public float GridZ;
        [XmlElement("Field")] public List<ClaimField> Fields = new List<ClaimField>();

        /// <summary>Claim slots: how many of the owner's own slots this claim uses (at least 1).</summary>
        [XmlAttribute] public int OwnSlots = 1;
        /// <summary>Slots bought with a penalty after a friend took theirs back.</summary>
        [XmlAttribute] public int PaidSlots;
        /// <summary>Slots the claim is short of right now (penalty pending; it cannot grow meanwhile).</summary>
        [XmlAttribute] public int MissingSlots;
        /// <summary>Minutes the owner still has to pay the penalty, counted only while they are online (0 = not told yet).</summary>
        [XmlAttribute] public int PenaltyLeft;
        [XmlElement("Donation")] public List<Donation> Donations = new List<Donation>();

        /// <summary>Unused (earned area is now added every minute).</summary>
        [XmlAttribute] public int OnlineMinutes;
        /// <summary>Server minutes since owner or a friend was last online (expiry).</summary>
        [XmlAttribute] public int AbsentMinutes;

        [XmlAttribute] public long CreatedUtcTicks = DateTime.UtcNow.Ticks;

        [XmlElement("Member")] public List<ClaimMember> Members = new List<ClaimMember>();
        [XmlElement("Incident")] public List<Incident> Incidents = new List<Incident>();

        // ------------------------------------------------------------------ format 1 (square around X/Z)

        [XmlAttribute("X")] public float LegacyX;
        [XmlAttribute("Y")] public float LegacyY;
        [XmlAttribute("Z")] public float LegacyZ;
        [XmlAttribute("HalfSize")] public float LegacyHalfSize;
        public bool ShouldSerializeLegacyX() => false;
        public bool ShouldSerializeLegacyY() => false;
        public bool ShouldSerializeLegacyZ() => false;
        public bool ShouldSerializeLegacyHalfSize() => false;

        /// <summary>Converts claims saved by older versions (square around a center).</summary>
        public void Upgrade()
        {
            if (MaxX > MinX || LegacyHalfSize <= 0f) return;
            AnchorX = LegacyX; AnchorY = LegacyY; AnchorZ = LegacyZ;
            MinX = LegacyX - LegacyHalfSize; MaxX = LegacyX + LegacyHalfSize;
            MinZ = LegacyZ - LegacyHalfSize; MaxZ = LegacyZ + LegacyHalfSize;
            LegacyHalfSize = 0f;
        }

        // ------------------------------------------------------------------ shape

        public const float FieldSize = 10f;
        /// <summary>One claim slot covers up to 50 m in each direction.</summary>
        public const float SlotSpan = 50f;

        public bool IsActive => State == ClaimState.Active;
        public float Width => MaxX - MinX;
        public float Depth => MaxZ - MinZ;
        public bool HasFields => Fields.Count > 0;
        public float Area => HasFields ? Fields.Count * FieldSize * FieldSize : Width * Depth;

        /// <summary>Slots the claim needs: the rectangle around it in 50 m pieces (rounded up), at least 1.</summary>
        public int SlotsNeeded => HasFields ? Spans(Width) * Spans(Depth) : 1;

        private static int Spans(float meters) => Math.Max(1, (int)Math.Ceiling(meters / SlotSpan - 0.001));

        public int Donated
        {
            get
            {
                int n = 0;
                foreach (var d in Donations) n += Math.Max(0, d.Slots);
                return n;
            }
        }

        public int DonatedBy(ulong steamId)
        {
            foreach (var d in Donations)
                if (d.SteamId == steamId) return Math.Max(0, d.Slots);
            return 0;
        }

        // ------------------------------------------------------------------ fields

        [XmlIgnore] private HashSet<long> _fieldSet;
        [XmlIgnore] private List<ClaimField> _fieldSetOf;

        private static long FieldKey(int i, int j) => ((long)i << 32) ^ (uint)j;

        private HashSet<long> FieldSet
        {
            get
            {
                // claims in the index are read from several places: build the set aside and swap it in whole
                var set = _fieldSet;
                if (set == null || !ReferenceEquals(_fieldSetOf, Fields) || set.Count != Fields.Count)
                {
                    set = new HashSet<long>();
                    foreach (var f in Fields) set.Add(FieldKey(f.I, f.J));
                    _fieldSetOf = Fields;
                    _fieldSet = set;
                }
                return set;
            }
        }

        /// <summary>The field a point lies in.</summary>
        public void FieldAt(float x, float z, out int i, out int j)
        {
            i = (int)Math.Floor((x - GridX) / FieldSize + 0.5f);
            j = (int)Math.Floor((z - GridZ) / FieldSize + 0.5f);
        }

        public bool HasField(int i, int j) => HasFields && FieldSet.Contains(FieldKey(i, j));

        public Box FieldRect(int i, int j) => new Box(
            GridX + (i - 0.5f) * FieldSize, GridX + (i + 0.5f) * FieldSize,
            GridZ + (j - 0.5f) * FieldSize, GridZ + (j + 0.5f) * FieldSize);

        /// <summary>Make this a field claim with just the field around its anchor.</summary>
        public void StartFields() => StartFields(AnchorX, AnchorZ);

        /// <summary>Make this a field claim with just the field around its anchor, on a grid through <paramref name="gridX"/>/<paramref name="gridZ"/>.</summary>
        public void StartFields(float gridX, float gridZ)
        {
            GridX = gridX;
            GridZ = gridZ;
            Fields.Clear();
            Fields.Add(new ClaimField());
            _fieldSet = null;
            UpdateBounds();
        }

        /// <summary>
        /// A claim without fields (older version, or growing was switched on later) becomes a field claim: every field
        /// that lies completely in it, plus the field around the anchor (never bigger, so no new conflicts).
        /// </summary>
        public void ConvertToFields()
        {
            if (HasFields) return;
            var r = Bounds;
            GridX = AnchorX;
            GridZ = AnchorZ;
            FieldAt(r.MinX, r.MinZ, out int i0, out int j0);
            FieldAt(r.MaxX, r.MaxZ, out int i1, out int j1);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    var f = FieldRect(i, j);
                    if (f.MinX >= r.MinX - 0.01f && f.MaxX <= r.MaxX + 0.01f && f.MinZ >= r.MinZ - 0.01f && f.MaxZ <= r.MaxZ + 0.01f)
                        Fields.Add(new ClaimField { I = i, J = j });
                }
            if (!Fields.Exists(f => f.I == 0 && f.J == 0)) Fields.Add(new ClaimField());
            _fieldSet = null;
            UpdateBounds();
        }

        public void AddField(int i, int j)
        {
            if (HasField(i, j)) return;
            Fields.Add(new ClaimField { I = i, J = j });
            _fieldSet = null;
            UpdateBounds();
        }

        public void RemoveField(int i, int j)
        {
            Fields.RemoveAll(f => f.I == i && f.J == j);
            _fieldSet = null;
            UpdateBounds();
        }

        /// <summary>Keep only the fields for which <paramref name="keep"/> is true (none left = no change of type!).</summary>
        public void KeepFields(Func<int, int, bool> keep)
        {
            Fields.RemoveAll(f => !keep(f.I, f.J));
            _fieldSet = null;
            UpdateBounds();
        }

        /// <summary>Min/Max = the rectangle around all fields.</summary>
        public void UpdateBounds()
        {
            if (!HasFields) return;
            int i0 = int.MaxValue, i1 = int.MinValue, j0 = int.MaxValue, j1 = int.MinValue;
            foreach (var f in Fields)
            {
                i0 = Math.Min(i0, f.I); i1 = Math.Max(i1, f.I);
                j0 = Math.Min(j0, f.J); j1 = Math.Max(j1, f.J);
            }
            MinX = GridX + (i0 - 0.5f) * FieldSize; MaxX = GridX + (i1 + 0.5f) * FieldSize;
            MinZ = GridZ + (j0 - 0.5f) * FieldSize; MaxZ = GridZ + (j1 + 0.5f) * FieldSize;
        }

        /// <summary>The claim as rectangles: one per field, or the whole rectangle.</summary>
        public List<Box> Parts()
        {
            var parts = new List<Box>();
            if (!HasFields) parts.Add(Bounds);
            else foreach (var f in Fields) parts.Add(FieldRect(f.I, f.J));
            return parts;
        }

        public Box Bounds => new Box(MinX, MaxX, MinZ, MaxZ);

        /// <summary>Do all fields hang together (side by side, not only corner to corner)?</summary>
        public bool IsConnected()
        {
            if (Fields.Count <= 1) return true;
            return ConnectedTo(Fields[0].I, Fields[0].J).Count == Fields.Count;
        }

        /// <summary>The fields that hang together with field i/j (including it, if it exists).</summary>
        public HashSet<long> ConnectedTo(int i, int j)
        {
            var seen = new HashSet<long>();
            if (!HasField(i, j)) return seen;
            var todo = new Stack<(int, int)>();
            todo.Push((i, j));
            seen.Add(FieldKey(i, j));
            while (todo.Count > 0)
            {
                var (a, b) = todo.Pop();
                foreach (var (x, y) in new[] { (a + 1, b), (a - 1, b), (a, b + 1), (a, b - 1) })
                    if (HasField(x, y) && seen.Add(FieldKey(x, y))) todo.Push((x, y));
            }
            return seen;
        }

        /// <summary>Drop every field that does not hang together with the anchor's field. False if the anchor's field is gone.</summary>
        public bool KeepAnchorPart()
        {
            if (!HasFields) return true;
            FieldAt(AnchorX, AnchorZ, out int ai, out int aj);
            var part = ConnectedTo(ai, aj);
            if (part.Count == 0) return false;
            KeepFields((i, j) => part.Contains(FieldKey(i, j)));
            return true;
        }

        /// <summary>The default name for a claim without a custom one; the game sets it for the chosen language.</summary>
        public static Func<string, string> DefaultName = owner => $"{owner}'s base";

        /// <summary>Name to show: the custom name, or "&lt;owner&gt;'s base" (in the server's language).</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DefaultName(OwnerName) : Name;

        /// <summary>How far a side reaches from the anchor.</summary>
        public float Reach(Side side)
        {
            switch (side)
            {
                case Side.North: return AnchorZ - MinZ; // north = -Z
                case Side.South: return MaxZ - AnchorZ;
                case Side.East: return AnchorX - MinX;  // east = -X
                default: return MaxX - AnchorX;
            }
        }

        /// <summary>Move a side so it reaches <paramref name="reach"/> meters from the anchor (no checks).</summary>
        public void SetReach(Side side, float reach)
        {
            switch (side)
            {
                case Side.North: MinZ = AnchorZ - reach; break;
                case Side.South: MaxZ = AnchorZ + reach; break;
                case Side.East: MinX = AnchorX - reach; break;
                default: MaxX = AnchorX + reach; break;
            }
        }

        /// <summary>Length of a side (the edge that moves when it grows).</summary>
        public float EdgeLength(Side side) => side == Side.North || side == Side.South ? Width : Depth;

        public bool Contains(float x, float z) => Near(x, z, 0f);

        /// <summary>True if the point is inside the claim grown by <paramref name="buffer"/> meters on every side.</summary>
        public bool Near(float x, float z, float buffer)
        {
            if (!Bounds.Near(x, z, buffer)) return false;
            if (!HasFields) return true;
            if (buffer <= 0f)
            {
                // fast path: the field the point lies in (points exactly on an edge may belong to the neighbour)
                FieldAt(x, z, out int i, out int j);
                if (HasField(i, j)) return true;
            }
            foreach (var f in Fields)
                if (FieldRect(f.I, f.J).Near(x, z, buffer)) return true;
            return false;
        }

        /// <summary>True if the two claims overlap (touching edges do not count).</summary>
        public bool Overlaps(Claim other)
        {
            if (!Bounds.Overlaps(other.Bounds)) return false;
            if (!HasFields && !other.HasFields) return true;
            var mine = Parts();
            foreach (var o in other.Parts())
                foreach (var m in mine)
                    if (m.Overlaps(o)) return true;
            return false;
        }

        /// <summary>
        /// Edge-to-edge gap: the larger of the gaps along X and Z (0 if they overlap or touch), between the closest
        /// parts. "Gap ≥ 10" means: grow either claim by 10 m on every side and they still do not overlap.
        /// </summary>
        public float GapTo(Claim other)
        {
            if (!HasFields && !other.HasFields) return Bounds.GapTo(other.Bounds);
            float best = float.MaxValue;
            var mine = Parts();
            foreach (var o in other.Parts())
                foreach (var m in mine)
                    best = Math.Min(best, m.GapTo(o));
            return best;
        }

        public float GapTo(float minX, float maxX, float minZ, float maxZ)
        {
            var r = new Box(minX, maxX, minZ, maxZ);
            float best = float.MaxValue;
            foreach (var m in Parts()) best = Math.Min(best, m.GapTo(r));
            return best;
        }

        /// <summary>Straight-line distance from a point to the claim (0 inside).</summary>
        public float DistanceTo(float x, float z)
        {
            float best = float.MaxValue;
            foreach (var m in Parts()) best = Math.Min(best, m.DistanceTo(x, z));
            return best;
        }

        // ------------------------------------------------------------------ people

        public ClaimMember FindMember(ulong steamId)
        {
            foreach (var m in Members)
                if (m.SteamId == steamId) return m;
            return null;
        }

        public Role RoleOf(ulong steamId)
        {
            if (steamId == 0) return Role.None;
            if (steamId == OwnerId) return Role.Owner;
            return FindMember(steamId)?.Role ?? Role.None;
        }

        /// <summary>Permissions of a player in this claim (admin bypass is NOT applied here).</summary>
        public Permission PermissionsOf(ulong steamId) => IsActive ? Roles.Of(RoleOf(steamId)) : Permission.All;

        /// <summary>True if the owner or a friend is online (guests do not count).</summary>
        public bool HasGuardianOnline(Func<ulong, bool> isOnline)
        {
            if (isOnline(OwnerId)) return true;
            foreach (var m in Members)
                if (m.Role == Role.Friend && isOnline(m.SteamId)) return true;
            return false;
        }

        public void AddIncident(long minute, string text)
        {
            Incidents.Add(new Incident { Minute = minute, Text = text });
            while (Incidents.Count > MaxIncidents) Incidents.RemoveAt(0);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A new claim: <paramref name="startReach"/> m around the anchor, or – <paramref name="fields"/> – its first field.</summary>
        public static Claim Create(ulong ownerId, string ownerName, float x, float y, float z, long anchorId,
            float startReach, bool fields = false)
        {
            var c = new Claim
            {
                OwnerId = ownerId,
                OwnerName = ownerName,
                AnchorId = anchorId,
                AnchorX = x, AnchorY = y, AnchorZ = z,
                MinX = x - startReach, MaxX = x + startReach,
                MinZ = z - startReach, MaxZ = z + startReach,
            };
            if (fields) c.StartFields();
            return c;
        }

        public Claim Clone()
        {
            var copy = (Claim)MemberwiseClone();
            copy.Members = new List<ClaimMember>();
            foreach (var m in Members)
                copy.Members.Add(new ClaimMember { SteamId = m.SteamId, Role = m.Role, Name = m.Name });
            copy.Incidents = new List<Incident>();
            foreach (var i in Incidents)
                copy.Incidents.Add(new Incident { Minute = i.Minute, Text = i.Text });
            copy.Fields = new List<ClaimField>();
            foreach (var f in Fields) copy.Fields.Add(new ClaimField { I = f.I, J = f.J });
            copy._fieldSet = null;
            copy.Donations = new List<Donation>();
            foreach (var d in Donations) copy.Donations.Add(new Donation { SteamId = d.SteamId, Name = d.Name, Slots = d.Slots });
            return copy;
        }

        /// <summary>
        /// Brings the slot numbers in order after anything changed (size, donations, the owner's free slots):
        /// a donation first covers slots bought with a penalty, then replaces own slots (they become free again);
        /// what is still missing comes from the owner's <paramref name="ownerFree"/> slots, and the rest is
        /// <see cref="MissingSlots"/> (penalty). Returns how many of the owner's free slots it took.
        /// </summary>
        public int SettleSlots(int ownerFree)
        {
            int need = SlotsNeeded;
            int donated = Donated;
            PaidSlots = Math.Max(0, Math.Min(PaidSlots, need - donated - OwnSlots));
            OwnSlots = Math.Max(1, Math.Min(OwnSlots, need - donated - PaidSlots));
            int missing = need - donated - PaidSlots - OwnSlots;
            int take = Math.Max(0, Math.Min(missing, ownerFree));
            OwnSlots += take;
            missing -= take;
            MissingSlots = Math.Max(0, missing);
            if (MissingSlots == 0) PenaltyLeft = 0;
            return take;
        }

        /// <summary>Cleans a player-chosen name. Returns null if nothing usable is left.</summary>
        public static string CleanName(string name)
        {
            if (name == null) return null;
            var chars = new System.Text.StringBuilder();
            foreach (char c in name.Trim())
                if (!char.IsControl(c) && c != '<' && c != '>') chars.Append(c);
            string result = chars.ToString().Trim();
            if (result.Length > MaxNameLength) result = result.Substring(0, MaxNameLength).TrimEnd();
            return result.Length == 0 ? null : result;
        }
    }
}
