using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using HarmonyLib;
using TerrainSystem;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Mining robots act on their own, without network messages, so the firewall never sees them (design 0.9).
    /// The game does not store who owns one, so Verboten remembers it (saved with the world):
    ///  - owner = whoever last carried or dragged it;
    ///  - a robot without owner that first shows up inside a claim belongs to the claim's owner;
    ///  - otherwise unknown = stranger.
    /// Strangers' robots may not dig in claims and are moved out of them. Checks run once per second, and only
    /// while there is at least one robot.
    /// </summary>
    public static class Robots
    {
        private const float Interval = 1f;
        private const float NearMinedSpot = 8f;

        private static readonly Dictionary<long, ulong> Owners = new Dictionary<long, ulong>();
        private static readonly HashSet<long> Seen = new HashSet<long>();
        private static float _next;

        public static void Register()
        {
            Hooks.Add(Hooks.Kind.Protection, "Robots of strangers cannot dig in claims", "Vein.TryMineServer",
                () => AccessTools.Method(typeof(Vein), "TryMineServer", new[] { typeof(Vector3Int), typeof(Ore).MakeByRefType(), typeof(Vector3) }),
                typeof(Robots), prefix: nameof(TryMinePrefix));
        }

        // ------------------------------------------------------------------ saved data

        public static void Load(IEnumerable<RobotOwner> owners)
        {
            Owners.Clear();
            Seen.Clear();
            if (owners == null) return;
            foreach (var o in owners)
            {
                if (o == null || o.RobotId == 0) continue;
                Owners[o.RobotId] = o.SteamId;
                Seen.Add(o.RobotId);
            }
        }

        public static List<RobotOwner> CopyForSave()
        {
            var list = new List<RobotOwner>(Owners.Count);
            foreach (var kv in Owners)
                if (Thing.Find(kv.Key) != null) // robots that were destroyed are forgotten
                    list.Add(new RobotOwner { RobotId = kv.Key, SteamId = kv.Value });
            return list;
        }

        public static void Reset()
        {
            Owners.Clear();
            Seen.Clear();
        }

        public static ulong OwnerOf(RobotMining robot) =>
            robot != null && Owners.TryGetValue(robot.ReferenceId, out ulong id) ? id : 0;

        // ------------------------------------------------------------------ every second

        public static void Tick()
        {
            if (!GameManager.RunSimulation || GameManager.GameState != GameState.Running) return;
            var robots = RobotMining.AllRobots;
            if (robots == null || robots.Count == 0) return;
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + Interval;

            var index = Plugin.Store.Index;
            foreach (var robot in new List<RobotMining>(robots))
            {
                if (robot == null || robot.BeingDestroyed) continue;
                long id = robot.ReferenceId;

                var carrier = robot.RootParentHuman;
                if (carrier != null)
                {
                    ulong by = Identity.SteamIdOf(carrier);
                    if (Identity.IsValidSteamId(by) && !Pins.IsLocked(by)) Owners[id] = by;
                    Seen.Add(id);
                    continue; // being carried: never moved
                }

                var p = robot.Position;
                var claim = index.At(p.x, p.z);
                if (!Seen.Contains(id))
                {
                    Seen.Add(id);
                    if (claim != null && !Owners.ContainsKey(id)) Owners[id] = claim.OwnerId; // made inside the claim
                }
                if (claim == null || (claim.PermissionsOf(OwnerOf(robot)) & Permission.Mining) != 0) continue;

                World.Teleport(robot, World.ExitPoint(claim, p));
                Plugin.Log.LogInfo($"Moved a mining robot (#{id}, owner {OwnerOf(robot)}) out of {claim.DisplayName}.");
            }
        }

        // ------------------------------------------------------------------ digging

        private static bool TryMinePrefix(Vector3Int __0, ref Ore __1, ref bool __result)
        {
            try
            {
                var index = Plugin.Store.Index;
                if (index.Count == 0) return true;
                var claim = index.At(__0.x, __0.z);
                if (claim == null) return true;
                var robots = RobotMining.AllRobots;
                if (robots == null || robots.Count == 0) return true; // players are checked by the firewall

                // the robot doing this is the one closest to the spot
                RobotMining nearest = null;
                float best = NearMinedSpot * NearMinedSpot;
                foreach (var r in robots)
                {
                    if (r == null) continue;
                    var p = r.Position;
                    float d = (p.x - __0.x) * (p.x - __0.x) + (p.y - __0.y) * (p.y - __0.y) + (p.z - __0.z) * (p.z - __0.z);
                    if (d < best) { best = d; nearest = r; }
                }
                if (nearest == null) return true;
                if ((claim.PermissionsOf(OwnerOf(nearest)) & Permission.Mining) != 0) return true;

                __1 = null;
                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Robot mining check failed (allowed): {ex}");
                return true;
            }
        }
    }
}
