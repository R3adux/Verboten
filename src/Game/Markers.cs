using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Abandoned bases are shown on every tablet tracker: the server puts a fixed beacon named
    /// "Abandoned: Bob's base" just OUTSIDE the claim (3 m past the edge facing the landing zone), so whoever
    /// follows it walks into the base and gets the "take it over" message. Beacons are trackable without power.
    /// The beacon is protected (nobody but admins can change or remove it, explosions do nothing to it) and is
    /// removed when the claim is taken over, reactivated or dissolved. Checked once per server minute.
    /// </summary>
    public static class Markers
    {
        private const float OutsideBy = 3f;
        private const float MaxDistance = 8f;

        private static readonly HashSet<long> Ids = new HashSet<long>();

        public static bool IsMarker(long referenceId) => referenceId != 0 && Ids.Contains(referenceId);

        public static void Reset() => Ids.Clear();

        /// <summary>Called every server minute (and after loading): add, move or remove beacons as needed.</summary>
        public static void Update()
        {
            Ids.Clear();
            foreach (var c in Plugin.Store.Index.Claims)
                if (c.MarkerId != 0) Ids.Add(c.MarkerId);

            foreach (var claim in new List<Claim>(Plugin.Store.Index.Claims))
            {
                try
                {
                    var marker = claim.MarkerId != 0 ? Thing.Find(claim.MarkerId) as FixedBeacon : null;
                    if (claim.IsActive)
                    {
                        if (claim.MarkerId != 0) Remove(claim, marker);
                        continue;
                    }
                    if (marker != null && !marker.BeingDestroyed && claim.DistanceTo(marker.Position.x, marker.Position.z) <= MaxDistance)
                        continue; // in place
                    if (marker != null) Remove(claim, marker);
                    Place(claim);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Tracker beacon for {claim.DisplayName} failed: {ex.Message}");
                }
            }
        }

        /// <summary>Destroy a beacon without touching the claim store (safe inside a store batch).</summary>
        public static void DestroyBeacon(long id)
        {
            if (id == 0) return;
            Ids.Remove(id);
            var thing = Thing.Find(id);
            if (thing != null && !thing.BeingDestroyed) OnServer.Destroy(thing);
        }

        /// <summary>A claim was taken over or deleted: remove its beacon right away.</summary>
        public static void RemoveFor(Claim claim)
        {
            if (claim == null || claim.MarkerId == 0) return;
            Remove(claim, Thing.Find(claim.MarkerId) as FixedBeacon);
        }

        private static void Remove(Claim claim, FixedBeacon marker)
        {
            long id = claim.MarkerId;
            Ids.Remove(id);
            Plugin.Store.Update(claim.Id, c => c.MarkerId = 0);
            if (marker != null && !marker.BeingDestroyed) OnServer.Destroy(marker);
        }

        private static void Place(Claim claim)
        {
            var prefab = FindPrefab();
            if (prefab == null) return;
            var spot = Spot(claim);
            if (spot == null) return;

            var grid = GridController.World.WorldToLocal(spot.Value);
            var structure = Constructor.SpawnConstruct(new CreateStructureInstance(prefab, grid, Quaternion.identity, 0UL));
            if (structure == null) return;

            string name = L.T("marker.name", ("claim", claim.DisplayName));
            OnServer.SetCustomName(structure, name);
            try { NetworkServer.SendToClients(new ThingRenameMessage { ThingId = structure.ReferenceId, ThingName = name }, NetworkChannel.GeneralTraffic, -1L); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Could not send the beacon name: {ex.Message}"); }

            Ids.Add(structure.ReferenceId);
            Plugin.Store.Update(claim.Id, c => c.MarkerId = structure.ReferenceId);
            Plugin.Log.LogInfo($"Tracker beacon placed for abandoned {claim.DisplayName} at ({spot.Value.x:0}, {spot.Value.z:0}).");
        }

        /// <summary>3 m outside the middle of the edge that faces the nearest landing zone (else any free edge).</summary>
        private static Vector3? Spot(Claim claim)
        {
            float cx = (claim.MinX + claim.MaxX) / 2f, cz = (claim.MinZ + claim.MaxZ) / 2f;
            var candidates = new List<Vector3>
            {
                new Vector3(claim.MaxX + OutsideBy, claim.AnchorY, cz),
                new Vector3(claim.MinX - OutsideBy, claim.AnchorY, cz),
                new Vector3(cx, claim.AnchorY, claim.MaxZ + OutsideBy),
                new Vector3(cx, claim.AnchorY, claim.MinZ - OutsideBy),
            };
            var zones = World.LandingZones();
            if (zones.Count > 0)
            {
                var z = zones[0];
                candidates.Sort((a, b) => Flat(a, z.X, z.Z).CompareTo(Flat(b, z.X, z.Z)));
            }
            var index = Plugin.Store.Index;
            foreach (var c in candidates)
            {
                // not inside or in the zone of an active claim
                if (index.FirstBlockingNear(c.x, c.z, ClaimGeometry.Zone, 0, Permission.Entry) != null) continue;
                var ground = SpawnPoint.GetSafePoint(c, Vector3.zero, avoidStructure: true) - new Vector3(0f, 4f, 0f);
                return ground;
            }
            return null;
        }

        private static float Flat(Vector3 a, float x, float z) => (a.x - x) * (a.x - x) + (a.z - z) * (a.z - z);

        private static Structure _prefab;

        private static Structure FindPrefab()
        {
            if (_prefab != null) return _prefab;
            foreach (var s in Structure.AllStructurePrefabs)
                if (s is FixedBeacon) return _prefab = s;
            Plugin.Log.LogWarning("No beacon prefab found – abandoned bases are not shown on trackers.");
            return null;
        }
    }
}
