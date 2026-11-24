using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Entities;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>World-wide helpers: server clock, landing zones, structures in an area, safe places outside claims.</summary>
    public static class World
    {
        /// <summary>Minutes this world has run on the server (saved with the world).</summary>
        public static long Minute;

        public static bool IsCreative
        {
            get
            {
                try { return WorldManager.Instance != null && WorldManager.Instance.GameMode == GameMode.Creative; }
                catch { return false; }
            }
        }

        // ------------------------------------------------------------------ landing zones

        public static List<LandingZone> LandingZones()
        {
            var list = new List<LandingZone>();
            var data = WorldSetting.Current?.StartLocationData;
            if (data is RoundRobinStartLocationData rr && rr.Locations != null)
            {
                foreach (var loc in rr.Locations) Add(loc, list);
            }
            else
            {
                Add(data, list);
            }
            return list;
        }

        private static void Add(StartLocationData loc, List<LandingZone> list)
        {
            if (loc?.Position == null) return;
            var p = loc.WorldPosition();
            list.Add(new LandingZone { X = p.x, Z = p.z, SpawnRadius = loc.GetSpawnRadius() });
        }

        // ------------------------------------------------------------------ players

        /// <summary>True if a player counts as active: online, owns an active claim, or was online within the expiry time.</summary>
        public static bool IsActivePlayer(ulong steamId)
        {
            if (!Identity.IsValidSteamId(steamId)) return false;
            if (Online.Contains(steamId)) return true;
            if (Plugin.Store.Index.OwnedBy(steamId).Count > 0) return true;
            long? away = Plugin.Players.MinutesAway(steamId, Minute);
            return away.HasValue && away.Value <= Math.Max(1, Plugin.Settings.ClaimExpiryDays.Value) * 1440L;
        }

        // ------------------------------------------------------------------ structures

        /// <summary>
        /// Structures in a rectangle that belong to another ACTIVE player (not <paramref name="isOurs"/>). Ruins of
        /// inactive players and things the world itself created (no builder) do not count.
        /// Expensive (all structures) – only for rare events.
        /// </summary>
        public static List<Structure> ForeignActiveStructures(float minX, float maxX, float minZ, float maxZ, Func<ulong, bool> isOurs)
        {
            var result = new List<Structure>();
            foreach (var s in UnityEngine.Object.FindObjectsOfType<Structure>())
            {
                ulong owner = s.OwnerClientId;
                if (!Identity.IsValidSteamId(owner) || isOurs(owner)) continue;
                var p = s.Position;
                if (p.x < minX || p.x > maxX || p.z < minZ || p.z > maxZ) continue;
                if (IsActivePlayer(owner)) result.Add(s);
            }
            return result;
        }

        /// <summary>Position and builder of every structure with a known builder in a rectangle. Expensive – rare events only.</summary>
        public static List<(Vector3 Position, ulong Owner)> StructuresIn(float minX, float maxX, float minZ, float maxZ)
        {
            var result = new List<(Vector3, ulong)>();
            foreach (var s in UnityEngine.Object.FindObjectsOfType<Structure>())
            {
                ulong owner = s.OwnerClientId;
                if (!Identity.IsValidSteamId(owner)) continue;
                var p = s.Position;
                if (p.x < minX || p.x > maxX || p.z < minZ || p.z > maxZ) continue;
                result.Add((p, owner));
            }
            return result;
        }

        public static string NameOf(ulong steamId)
        {
            var human = SenderContext.CharacterOf(steamId);
            if (human != null) return human.DisplayName;
            var known = Plugin.Players.Get(steamId);
            return !string.IsNullOrEmpty(known?.Name) ? known.Name : steamId.ToString();
        }

        /// <summary>Anchor objects (autolathes or flags, per config) inside a claim, except one.</summary>
        public static List<Structure> AnchorsIn(Claim claim, long exceptId)
        {
            var result = new List<Structure>();
            IEnumerable<Structure> candidates = Plugin.Settings.Anchor.Value == ClaimAnchor.Flag
                ? (IEnumerable<Structure>)UnityEngine.Object.FindObjectsOfType<Flag>()
                : UnityEngine.Object.FindObjectsOfType<Autolathe>();
            foreach (var s in candidates)
            {
                if (s == null || s.ReferenceId == exceptId || s.BeingDestroyed) continue;
                var p = s.Position;
                if (claim.Contains(p.x, p.z)) result.Add(s);
            }
            return result;
        }

        // ------------------------------------------------------------------ moving things out

        /// <summary>A safe spot just outside the given claim (2 m past the nearest edge), not inside another claim.</summary>
        public static Vector3 ExitPoint(Claim claim, Vector3 from)
        {
            var index = Plugin.Store.Index;
            var candidates = new List<Vector3>
            {
                new Vector3(claim.MaxX + 2f, from.y, from.z),
                new Vector3(claim.MinX - 2f, from.y, from.z),
                new Vector3(from.x, from.y, claim.MaxZ + 2f),
                new Vector3(from.x, from.y, claim.MinZ - 2f),
            };
            candidates.Sort((a, b) => Flat(a, from).CompareTo(Flat(b, from)));
            foreach (var c in candidates)
            {
                var safe = SpawnPoint.GetSafePoint(c, Vector3.zero, avoidStructure: true);
                if (index.At(safe.x, safe.z) == null) return safe;
            }
            // everything around is claimed: go to the first landing zone
            var zones = LandingZones();
            var fallback = zones.Count > 0 ? new Vector3(zones[0].X, from.y, zones[0].Z) : candidates[0];
            return SpawnPoint.GetSafePoint(fallback, Vector3.zero, avoidStructure: true);
        }

        private static float Flat(Vector3 a, Vector3 b) => (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);

        /// <summary>Move a thing the server controls (offline character, robot) to a position.</summary>
        public static void Teleport(DynamicThing thing, Vector3 position)
        {
            if (thing == null) return;
            thing.ThingTransformPosition = position;
            var body = thing.RigidBody;
            if (body != null)
            {
                body.position = position;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}
