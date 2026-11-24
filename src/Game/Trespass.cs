using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Objects.Entities;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Strangers (not owner, friend or guest) in and near claims (design 0.3):
    ///  - approaching (10 m zone): one message per hour and claim;
    ///  - inside: countdown, disconnected after 3 s (10 s if the claim came to them: created/grown around them or
    ///    rights removed while inside);
    ///  - 3 disconnects within 30 min: joining blocked for 30 min;
    ///  - characters of players who are offline are moved just outside (the server controls them then).
    /// Host, verified admins, unconscious and dead players are left alone.
    /// </summary>
    public static class Trespass
    {
        private const float Interval = 0.5f;
        public const float KickAfter = 3f;
        private const float Grace = 10f;
        private const float ApproachRepeat = 3600f;
        private const int KicksBeforeBlock = 3;
        private const float KickWindow = 1800f;
        private const float BlockFor = 1800f;
        private const float OfflineCheckEvery = 5f;

        private sealed class Inside
        {
            public string ClaimId;
            public float Deadline;
            /// <summary>When they stepped out (0 = inside). Coming back soon continues the same countdown.</summary>
            public float LeftAt;
        }

        private const float CountdownMemory = 10f;

        private static readonly Dictionary<ulong, Inside> Countdowns = new Dictionary<ulong, Inside>();
        private static readonly Dictionary<ulong, Vector3> LastPosition = new Dictionary<ulong, Vector3>();
        private static readonly Dictionary<(ulong, string), float> LastApproach = new Dictionary<(ulong, string), float>();
        private static HashSet<(ulong, string)> _inZone = new HashSet<(ulong, string)>();
        private static readonly Dictionary<ulong, List<float>> Kicks = new Dictionary<ulong, List<float>>();
        private static readonly Dictionary<ulong, float> BlockedUntil = new Dictionary<ulong, float>();
        private static float _next, _nextOffline;

        /// <summary>Join check: is this player blocked right now? (Used by the sender check.)</summary>
        public static bool IsBlocked(ulong steamId, out int minutesLeft)
        {
            minutesLeft = 0;
            if (!BlockedUntil.TryGetValue(steamId, out float until)) return false;
            float left = until - Time.unscaledTime;
            if (left <= 0f) { BlockedUntil.Remove(steamId); return false; }
            minutesLeft = Mathf.CeilToInt(left / 60f);
            return true;
        }

        public static void Tick()
        {
            if (!GameManager.RunSimulation || GameManager.GameState != GameState.Running) return;
            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + Interval;

            var index = Plugin.Store.Index;
            if (index.Count == 0)
            {
                Countdowns.Clear();
                LastPosition.Clear();
                _inZone.Clear();
                return;
            }

            var stillInside = new HashSet<ulong>();
            var inZone = new HashSet<(ulong, string)>();
            foreach (var human in Human.AllHumans)
            {
                if (human == null || human.State != EntityState.Alive) continue;
                if (!GameManager.IsBatchMode && human == Human.LocalHuman) continue; // the host is an admin
                ulong id = Identity.SteamIdOf(human);
                if (!Identity.IsValidSteamId(id) || !Online.Contains(id)) continue;
                if (IsVerifiedAdmin(id)) continue;
                if (Pins.IsLocked(id)) continue; // may stand in their base while it waits for the PIN (can do nothing there)

                var p = human.Position;
                LastPosition.TryGetValue(id, out var before);
                bool hadBefore = LastPosition.ContainsKey(id);
                LastPosition[id] = p;

                var claim = index.At(p.x, p.z);
                if (claim != null && (claim.PermissionsOf(id) & Permission.Entry) == 0)
                {
                    stillInside.Add(id);
                    HandleInside(human, id, claim, hadBefore && claim.Contains(before.x, before.z), now);
                    continue;
                }
                if (claim != null) continue; // in a claim they may be in: no warnings about neighbours
                var near = index.FirstBlockingNear(p.x, p.z, ClaimGeometry.Zone, id, Permission.Entry);
                if (near == null) continue;
                Approach(id, near, now);
                // every time they walk into the zone: show the border, so they see it instead of only reading it
                if (inZone.Add((id, near.Id)) && !_inZone.Contains((id, near.Id)) && !BorderDisplay.IsShowing(id))
                    BorderDisplay.ShowClaim(id, near, BorderDisplay.Duration);
            }
            _inZone = inZone;

            // A countdown survives short trips outside: stepping out for a moment does not start it again.
            foreach (var kv in new List<KeyValuePair<ulong, Inside>>(Countdowns))
            {
                if (stillInside.Contains(kv.Key)) { kv.Value.LeftAt = 0f; continue; }
                if (kv.Value.LeftAt <= 0f) kv.Value.LeftAt = now;
                else if (now - kv.Value.LeftAt > CountdownMemory) Countdowns.Remove(kv.Key);
            }

            if (now >= _nextOffline)
            {
                _nextOffline = now + OfflineCheckEvery;
                MoveOfflineStrangersOut(index);
            }
        }

        private static void Approach(ulong id, Claim claim, float now)
        {
            var key = (id, claim.Id);
            if (LastApproach.TryGetValue(key, out float last) && now - last < ApproachRepeat) return;
            LastApproach[key] = now;
            Notify.Chat(id, L.T("trespass.approach", ("claim", claim.DisplayName), ("seconds", $"{KickAfter:0}")));
        }

        /// <param name="claimCameToThem">They stood here already before (claim created/grown around them, or rights removed).</param>
        private static void HandleInside(Human human, ulong id, Claim claim, bool claimCameToThem, float now)
        {
            if (!Countdowns.TryGetValue(id, out var c) || c.ClaimId != claim.Id)
            {
                // a new countdown (an old one for the same claim continues, see Tick)
                float wait = claimCameToThem ? Grace : KickAfter;
                Countdowns[id] = new Inside { ClaimId = claim.Id, Deadline = now + wait };
                Notify.Chat(id, L.T("trespass.inside", ("claim", claim.DisplayName), ("seconds", $"{wait:0}")));
                Incidents.Add(claim, L.T("incident.entered", ("player", human.DisplayName)));
                return;
            }
            if (now < c.Deadline) return;
            Countdowns.Remove(id);
            Kick(id, human.DisplayName, claim, now);
        }

        private static void Kick(ulong id, string name, Claim claim, float now)
        {
            var client = Notify.ClientOf(id);
            if (client == null) return;

            if (!Kicks.TryGetValue(id, out var times)) Kicks[id] = times = new List<float>();
            times.RemoveAll(t => now - t > KickWindow);
            times.Add(now);
            bool block = times.Count >= KicksBeforeBlock;
            if (block)
            {
                BlockedUntil[id] = now + BlockFor;
                times.Clear();
            }

            RemoteChat.SendPrivate(client, block
                ? L.T("trespass.blocked", ("claim", claim.DisplayName), ("minutes", $"{BlockFor / 60f:0}"))
                : L.T("trespass.kicked", ("claim", claim.DisplayName)));
            Plugin.Log.LogInfo($"Disconnected {name} ({id}): stayed in {claim.DisplayName}." + (block ? " Blocked for 30 min." : ""));
            SenderContext.Later(0.5f, () =>
            {
                try { client.Disconnect(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"Could not disconnect {name}: {ex.Message}"); }
            });
        }

        /// <summary>Characters of offline players standing in a claim without permission: put them outside.</summary>
        private static void MoveOfflineStrangersOut(ClaimIndex index)
        {
            foreach (var human in Human.AllHumans)
            {
                if (human == null || human.State == EntityState.Dead) continue;
                ulong id = Identity.SteamIdOf(human);
                if (!Identity.IsValidSteamId(id) || Online.Contains(id)) continue;
                var p = human.Position;
                var claim = index.At(p.x, p.z);
                if (claim == null || (claim.PermissionsOf(id) & Permission.Entry) != 0) continue;
                var exit = World.ExitPoint(claim, p);
                World.Teleport(human, exit);
                Plugin.Log.LogInfo($"Moved the character of {human.DisplayName} ({id}) out of {claim.DisplayName} (player offline).");
            }
        }

        private static bool IsVerifiedAdmin(ulong id)
        {
            var client = Notify.ClientOf(id);
            return client != null && SenderContext.IsVerifiedConnection(client.connectionId, id) && Identity.IsAdminId(id);
        }

        public static void Reset()
        {
            Countdowns.Clear();
            LastPosition.Clear();
            LastApproach.Clear();
            _inZone.Clear();
        }
    }
}
