using System;
using System.Collections.Generic;

namespace Verboten.Core
{
    /// <summary>
    /// Immutable lookup structure. Built once per change, read lock-free from anywhere.
    /// Claims are columns, so only X/Z are bucketed in a spatial hash. Protection lookups (At,
    /// FirstBlockingNear) only see ACTIVE claims; abandoned ones are listed separately.
    /// </summary>
    public sealed class ClaimIndex
    {
        public const float CellSize = 16f;

        public static readonly ClaimIndex Empty = new ClaimIndex(new List<Claim>(), 0);

        private readonly Claim[] _claims;
        private readonly Claim[] _active;
        private readonly Claim[] _abandoned;
        private readonly Dictionary<long, Claim[]> _cells;

        public int Version { get; }
        /// <summary>Number of ACTIVE claims (0 = nothing to protect).</summary>
        public int Count => _active.Length;
        public IReadOnlyList<Claim> Claims => _claims;
        public IReadOnlyList<Claim> Active => _active;
        public IReadOnlyList<Claim> Abandoned => _abandoned;

        public ClaimIndex(IEnumerable<Claim> claims, int version)
        {
            Version = version;
            var all = new List<Claim>(claims);
            _claims = all.ToArray();
            _active = all.FindAll(c => c.IsActive).ToArray();
            _abandoned = all.FindAll(c => !c.IsActive).ToArray();

            var cells = new Dictionary<long, List<Claim>>();
            foreach (var c in _active)
            {
                int x0 = Cell(c.MinX), x1 = Cell(c.MaxX);
                int z0 = Cell(c.MinZ), z1 = Cell(c.MaxZ);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        long key = Key(x, z);
                        if (!cells.TryGetValue(key, out var bucket)) cells[key] = bucket = new List<Claim>(1);
                        bucket.Add(c);
                    }
            }
            _cells = new Dictionary<long, Claim[]>(cells.Count);
            foreach (var kv in cells) _cells[kv.Key] = kv.Value.ToArray();
        }

        /// <summary>The ACTIVE claim containing the point, or null. O(1) for the common case.</summary>
        public Claim At(float x, float z)
        {
            if (_active.Length == 0) return null;
            if (!_cells.TryGetValue(Key(Cell(x), Cell(z)), out var bucket)) return null;
            for (int i = 0; i < bucket.Length; i++)
                if (bucket[i].Contains(x, z)) return bucket[i];
            return null;
        }

        /// <summary>The abandoned claim containing the point, or null.</summary>
        public Claim AbandonedAt(float x, float z)
        {
            foreach (var c in _abandoned)
                if (c.Contains(x, z)) return c;
            return null;
        }

        /// <summary>
        /// First ACTIVE claim within <paramref name="buffer"/> meters of the point where
        /// <paramref name="steamId"/> lacks <paramref name="permission"/>; null if none.
        /// With buffer = <see cref="ClaimGeometry.Zone"/> this is the rule for building in approach zones:
        /// you need the permission in EVERY claim whose zone reaches the spot.
        /// </summary>
        public Claim FirstBlockingNear(float x, float z, float buffer, ulong steamId, Permission permission)
        {
            if (_active.Length == 0) return null;
            if (buffer <= 0f)
            {
                var c = At(x, z);
                return c != null && (c.PermissionsOf(steamId) & permission) != permission ? c : null;
            }
            int x0 = Cell(x - buffer), x1 = Cell(x + buffer);
            int z0 = Cell(z - buffer), z1 = Cell(z + buffer);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    if (!_cells.TryGetValue(Key(cx, cz), out var bucket)) continue;
                    for (int i = 0; i < bucket.Length; i++)
                    {
                        var c = bucket[i];
                        if (c.Near(x, z, buffer) && (c.PermissionsOf(steamId) & permission) != permission)
                            return c;
                    }
                }
            return null;
        }

        /// <summary>ACTIVE claims that overlap or come closer than the minimum gap to the given claim.</summary>
        public List<Claim> TooClose(Claim probe)
        {
            var result = new List<Claim>();
            foreach (var c in _active)
                if (c.Id != probe.Id && !ClaimGeometry.Apart(c, probe)) result.Add(c);
            return result;
        }

        /// <summary>ACTIVE claims owned by a player.</summary>
        public List<Claim> OwnedBy(ulong steamId)
        {
            var result = new List<Claim>();
            foreach (var c in _active)
                if (c.OwnerId == steamId) result.Add(c);
            return result;
        }

        public Claim FindById(string id)
        {
            foreach (var c in _claims)
                if (c.Id == id) return c;
            return null;
        }

        /// <summary>Claim (active or abandoned) by display name.</summary>
        public Claim FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (var c in _claims)
                if (string.Equals(c.DisplayName, name.Trim(), StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>The claim whose anchor has this reference id, or null.</summary>
        public Claim ByAnchor(long anchorId)
        {
            if (anchorId == 0) return null;
            foreach (var c in _claims)
                if (c.AnchorId == anchorId) return c;
            return null;
        }

        private static int Cell(float v) => (int)Math.Floor(v / CellSize);
        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
