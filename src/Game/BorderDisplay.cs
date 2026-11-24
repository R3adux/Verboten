using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects.Entities;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Floating sparks along claim borders and landing zones, for a few seconds, at the player's height:
    /// on '/claim show', when a stranger walks into the zone around a claim, when a claim is created, and when
    /// someone builds where no claim is possible. Uses the game's own spark effect, so players need no mod;
    /// only that player sees them, and nothing is left in the world.
    /// </summary>
    public static class BorderDisplay
    {
        public const float Range = 60f;      // claims whose border is this close are shown
        public const float Duration = 30f;
        public const float NewClaimDuration = 30f;
        private const float Every = 0.25f;   // seconds between bursts
        private const float Spacing = 1.5f;  // meters between spark points along a border
        private const float Height = 1.5f;   // above the ground
        // The game draws all sparks with ONE particle effect that has a fixed maximum. Asking for more than that per
        // burst only shows the first points (that is why only two corners lit up). So every burst stays within this
        // budget and is spread evenly: all points get sparks, or – on long borders – the points take turns.
        private const int SparkBudget = 75;  // per burst (4 bursts a second)
        private const int MaxPerPoint = 6;

        private sealed class Show
        {
            public long Connection;          // 0 = the host's own screen
            public List<Vector3> Points;
            public float End;
            public float Next;
            public int Offset;               // where the next round starts when the points take turns
        }

        private static readonly List<Show> Active = new List<Show>();

        /// <summary>'/claim show': claims and landing zones near the actor; returns how many borders are shown.</summary>
        public static int Start(Actor actor)
        {
            if (!actor.HasPosition) return 0;
            if (!Flares.Enabled) return ShowNear(actor.IsLocalHost ? 0 : actor.Connection, actor.Position, Duration);

            // flares, in the colours of this player's view: own claims grey, others khaki, landing zones red
            var here = actor.Position;
            int shown = 0;
            var near = new List<Claim>();
            foreach (var c in Plugin.Store.Index.Claims)
                if (c.Near(here.x, here.z, Range)) near.Add(c);
            foreach (var group in ClaimGeometry.TouchingGroups(near))
            {
                shown++;
                Flares.AtCorners(group, group[0].RoleOf(actor.SteamId) != Role.None, Duration);
            }
            var zones = World.LandingZones();
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                float r = zone.ProtectedRadius;
                float d = Mathf.Sqrt((here.x - zone.X) * (here.x - zone.X) + (here.z - zone.Z) * (here.z - zone.Z));
                if (Mathf.Abs(d - r) > Range) continue;
                shown++;
                Flares.AroundLandingZone(zone, i, here.y, Duration);
            }
            return shown;
        }

        /// <summary>After the owner made their claim bigger: show the new border.</summary>
        public static void ShowOwn(Actor actor, Claim claim)
        {
            if (Identity.IsValidSteamId(actor.SteamId)) ShowOwnClaim(actor.SteamId, claim);
            else if (Flares.Enabled) Flares.AtCorners(GroupOf(claim), true, Duration);
        }

        /// <summary>A claim changed (grown, shrunk): show its border to its owner.</summary>
        public static void ShowOwnClaim(ulong steamId, Claim claim)
        {
            if (claim == null) return;
            if (Flares.Enabled) Flares.AtCorners(GroupOf(claim), true, Duration);
            else ShowClaim(steamId, claim, Duration);
        }

        /// <summary>The claim and the claims of the same owner that touch it (shown as one area).</summary>
        public static List<Claim> GroupOf(Claim claim)
        {
            var mine = new List<Claim>();
            foreach (var c in Plugin.Store.Index.Claims)
                if (c.OwnerId == claim.OwnerId && c.IsActive == claim.IsActive) mine.Add(c.Id == claim.Id ? claim : c);
            if (!mine.Exists(c => c.Id == claim.Id)) mine.Add(claim);
            foreach (var group in ClaimGeometry.TouchingGroups(mine))
                if (group.Exists(c => c.Id == claim.Id)) return group;
            return new List<Claim> { claim };
        }

        /// <summary>Claims and landing zones near a player, for some seconds. Returns how many borders are shown.</summary>
        public static int ShowNear(ulong steamId, float seconds)
        {
            return TryTarget(steamId, out long connection, out var here) ? ShowNear(connection, here, seconds) : 0;
        }

        /// <summary>A claim was just created or taken over: sparks along its border and burning flares at its corners.</summary>
        public static void ShowNewClaim(ulong steamId, Claim claim)
        {
            if (Flares.Enabled) Flares.AtCorners(GroupOf(claim), true, NewClaimDuration);
            else ShowClaim(steamId, claim, NewClaimDuration);
        }

        /// <summary>Just one claim (e.g. the one that was just created), for some seconds.</summary>
        public static void ShowClaim(ulong steamId, Claim claim, float seconds)
        {
            if (claim == null || !TryTarget(steamId, out long connection, out var here)) return;
            var points = new List<Vector3>();
            AddOutline(GroupOf(claim), here.y + 1f, points);
            Begin(connection, here, points, seconds);
        }

        /// <summary>Where the sparks go: the host's own screen (0) or a client connection, and where the player stands.</summary>
        private static bool TryTarget(ulong steamId, out long connection, out Vector3 here)
        {
            connection = 0;
            here = default;
            if (Notify.IsHost(steamId)) { here = Human.LocalHuman.Position; return true; }
            var client = Notify.ClientOf(steamId);
            var human = SenderContext.CharacterOf(steamId);
            if (client == null || human == null) return false;
            connection = client.connectionId;
            here = human.Position;
            return true;
        }

        private static int ShowNear(long connection, Vector3 here, float seconds)
        {
            float y = here.y + 1f;
            var points = new List<Vector3>();
            int shown = 0;
            var near = new List<Claim>();
            foreach (var c in Plugin.Store.Index.Claims)
                if (c.Near(here.x, here.z, Range)) near.Add(c);
            foreach (var group in ClaimGeometry.TouchingGroups(near))
            {
                shown++;
                AddOutline(group, y, points);
            }
            foreach (var zone in World.LandingZones())
            {
                float r = zone.ProtectedRadius;
                float d = Mathf.Sqrt((here.x - zone.X) * (here.x - zone.X) + (here.z - zone.Z) * (here.z - zone.Z));
                if (Mathf.Abs(d - r) > Range) continue;
                shown++;
                AddCircle(zone.X, zone.Z, r, y, points);
            }
            if (shown == 0) return 0;
            Begin(connection, here, points, seconds);
            return shown;
        }

        private static void Begin(long connection, Vector3 here, List<Vector3> points, float seconds)
        {
            // 1.5 m above the ground (hills!) – found once, not every burst
            var placed = new List<Vector3>(points.Count);
            foreach (var p in points) placed.Add(new Vector3(p.x, GroundAt(p.x, p.z, here.y) + Height, p.z));
            Active.RemoveAll(s => s.Connection == connection);
            float now = Time.unscaledTime;
            Active.Add(new Show { Connection = connection, Points = placed, End = now + seconds, Next = now });
        }

        /// <summary>Height of the ground (or whatever stands there) at x/z; the player's height if nothing is found.</summary>
        public static float GroundAt(float x, float z, float fallback)
        {
            var from = new Vector3(x, fallback + 60f, z);
            return Physics.Raycast(from, Vector3.down, out var hit, 200f) ? hit.point.y : fallback;
        }

        /// <summary>Is something being shown to this player right now? (So repeated triggers do not restart it.)</summary>
        public static bool IsShowing(ulong steamId)
        {
            if (!TryTarget(steamId, out long connection, out _)) return false;
            float now = Time.unscaledTime;
            foreach (var s in Active)
                if (s.Connection == connection && now < s.End) return true;
            return false;
        }

        /// <summary>Spark points along the outer edge of claims (several touching claims of one owner: one outline).</summary>
        private static void AddOutline(List<Claim> group, float y, List<Vector3> points)
        {
            var parts = new List<Box>();
            foreach (var c in group) parts.AddRange(c.Parts());
            foreach (var (x, z) in ClaimGeometry.OutlinePoints(parts, Spacing)) points.Add(new Vector3(x, y, z));
        }

        private static void AddCircle(float cx, float cz, float r, float y, List<Vector3> points)
        {
            int steps = Math.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / Spacing));
            for (int i = 0; i < steps; i++)
            {
                float a = 2f * Mathf.PI * i / steps;
                points.Add(new Vector3(cx + r * Mathf.Cos(a), y, cz + r * Mathf.Sin(a)));
            }
        }

        public static void Tick()
        {
            if (Active.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var s = Active[i];
                if (now >= s.End) { Active.RemoveAt(i); continue; }
                if (now < s.Next) continue;
                s.Next = now + Every;
                if (!Emit(s)) Active.RemoveAt(i);
            }
        }

        private static bool Emit(Show s)
        {
            try
            {
                Client client = null;
                if (s.Connection == 0) { if (GameManager.IsBatchMode) return false; }
                else
                {
                    client = Client.Find(s.Connection);
                    if (client == null || client.IsHost) return false;
                }
                int n = s.Points.Count;
                if (n == 0) return false;
                // within the budget: every point at once, or a share of the points per burst, in turns
                int perPoint = Math.Max(1, Math.Min(MaxPerPoint, SparkBudget / n));
                int count = Math.Min(n, SparkBudget / perPoint);
                for (int i = 0; i < count; i++)
                {
                    // spread this burst's points over the whole border; the offset moves on each burst
                    var p = s.Points[(s.Offset + (int)((long)i * n / count)) % n];
                    if (client == null) WorldManager.Spark(p, perPoint, false);
                    else NetworkServer.SendToClient(new EmitSparksMessage { Position = p, Quantity = perPoint, IsGravity = false },
                        NetworkChannel.GeneralTraffic, client);
                }
                s.Offset = (s.Offset + 1) % n;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Border display failed: {ex.Message}");
                return false;
            }
        }

        public static void Reset() => Active.Clear();
    }
}
