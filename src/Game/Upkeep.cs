using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Things that happen over time:
    ///  - every server minute: clock, "last seen", earned meters, expiry (claims become abandoned);
    ///  - when a player joins: two chat lines for players who belong to no claim, notices ("your base was taken
    ///    over"), "while you were away", owner of an abandoned claim comes back → protected again;
    ///  - while playing: message when entering an abandoned base.
    /// (The window explaining claims comes with the first thing a player builds, see <see cref="BuildGuide"/>.)
    /// </summary>
    public static class Upkeep
    {
        private const float PlayerCheckEvery = 1f;

        private static float _nextMinute, _nextPlayers;
        private static readonly HashSet<ulong> OnlineBefore = new HashSet<ulong>();
        private static readonly HashSet<ulong> Greeted = new HashSet<ulong>();
        private static readonly HashSet<(ulong, string)> AbandonedHinted = new HashSet<(ulong, string)>();

        public static void Tick()
        {
            if (!GameManager.RunSimulation || GameManager.GameState != GameState.Running) return;
            float now = Time.unscaledTime;
            if (_nextMinute <= 0f) _nextMinute = now + 60f;
            if (now >= _nextMinute)
            {
                _nextMinute = now + 60f;
                World.Minute++;
                try { OnMinute(); }
                catch (Exception ex) { Plugin.Log.LogError($"Minute upkeep failed: {ex}"); }
            }
            if (now >= _nextPlayers)
            {
                _nextPlayers = now + PlayerCheckEvery;
                try { WatchPlayers(); }
                catch (Exception ex) { Plugin.Log.LogError($"Player upkeep failed: {ex}"); }
            }
        }

        public static void Reset()
        {
            _nextMinute = 0f;
            OnlineBefore.Clear();
            Greeted.Clear();
            AbandonedHinted.Clear();
        }

        // ------------------------------------------------------------------ every server minute

        private static void OnMinute()
        {
            foreach (var (id, name) in OnlinePlayers()) Plugin.Players.Seen(id, name, World.Minute);

            float perMinute = Math.Max(0f, Plugin.Settings.EarnedAreaPerHour.Value) / 60f;
            int expiry = Math.Max(1, Plugin.Settings.ClaimExpiryDays.Value) * 1440;
            var comeBack = new List<Claim>();
            Plugin.Store.Batch(list =>
            {
                bool changed = false;
                foreach (var c in list)
                {
                    if (!c.IsActive)
                    {
                        if (Online.Contains(c.OwnerId)) comeBack.Add(c); // (only if a slot is free, see WelcomeBack)
                        continue;
                    }
                    changed = true;
                    if (c.HasGuardianOnline(Online.Contains) || c.Donations.Exists(d => Online.Contains(d.SteamId)))
                        c.AbsentMinutes = 0;
                    else if (++c.AbsentMinutes >= expiry)
                    {
                        c.State = ClaimState.Abandoned;
                        c.Donations.Clear();
                        Slots.SettleOwner(list, c.OwnerId);
                        Plugin.Log.LogInfo($"{c.DisplayName} abandoned: owner and friends away for {Plugin.Settings.ClaimExpiryDays.Value} days.");
                    }
                }
                return changed;
            });
            foreach (var c in comeBack) WelcomeBack(c, joined: false);
            // area accounts: only players who own or donate to a claim earn (owners add fields themselves)
            if (Slots.FieldMode)
                foreach (var (id, _) in OnlinePlayers())
                    if (Identity.IsValidSteamId(id) && Slots.TakesPart(id)) Slots.Account(id).Area += perMinute;
            Slots.SettleShortClaims();
            Slots.CheckPenalties(Online.Contains, tick: true);
            Markers.Update();
        }

        /// <summary>The owner of an abandoned claim is online: protect it again (said once at join if no slot is free).</summary>
        private static void WelcomeBack(Claim c, bool joined)
        {
            if (!ClaimActions.Reactivate(c))
            {
                if (joined) Notify.Chat(c.OwnerId, L.T("welcome_back_no_slot", ("claim", c.DisplayName), ("max", Slots.Max)));
                return;
            }
            var now = Plugin.Store.Index.FindById(c.Id);
            Notify.Chat(c.OwnerId, now != null && now.IsActive
                ? L.T("welcome_back", ("claim", now.DisplayName))
                : L.T("welcome_back_failed", ("claim", c.DisplayName)));
        }

        // ------------------------------------------------------------------ players

        private static IEnumerable<(ulong, string)> OnlinePlayers()
        {
            if (!GameManager.IsBatchMode && Human.LocalHuman != null)
                yield return (Identity.SteamIdOf(Human.LocalHuman), Human.LocalHuman.DisplayName);
            foreach (var client in NetworkBase.Clients)
                if (client != null && !client.IsHost && Identity.IsValidSteamId(client.ClientId))
                    yield return (client.ClientId, client.name);
        }

        private static void WatchPlayers()
        {
            var now = new HashSet<ulong>();
            foreach (var (id, name) in OnlinePlayers())
            {
                if (!Identity.IsValidSteamId(id)) continue;
                now.Add(id);
                var human = SenderContext.CharacterOf(id);
                if (human == null) continue; // still joining: try again next second

                if (!Greeted.Contains(id))
                {
                    Greeted.Add(id);
                    Joined(id, name);
                }
                Hints(id, human);
            }
            foreach (var id in OnlineBefore)
                if (!now.Contains(id)) Greeted.Remove(id);
            OnlineBefore.Clear();
            OnlineBefore.UnionWith(now);
        }

        private static void Joined(ulong id, string name)
        {
            var known = Plugin.Players.Get(id);
            long since = known?.LastSeenMinute ?? World.Minute;

            if (!BelongsToAClaim(id))
                foreach (var line in Texts.JoinLines(Plugin.Store.Index.Abandoned.Count)) Notify.Chat(id, line);
            if (Pins.IsLocked(id))
                Notify.Chat(id, L.T("join.pin_locked"));

            var record = Plugin.Players.GetOrAdd(id);
            foreach (var notice in record.Notices) Notify.Popup(id, notice);
            record.Notices.Clear();
            Plugin.Players.Seen(id, name, World.Minute);

            string away = Incidents.SummaryFor(id, since);
            if (away != null) Notify.Chat(id, away);

            foreach (var c in Plugin.Store.Index.Abandoned)
                if (c.OwnerId == id) WelcomeBack(c, joined: true);
            Slots.CheckPenalties(Online.Contains, tick: false, joined: id);
        }

        private static bool BelongsToAClaim(ulong id)
        {
            foreach (var c in Plugin.Store.Index.Claims)
                if (c.RoleOf(id) != Role.None) return true;
            return false;
        }

        private static void Hints(ulong id, Human human)
        {
            var p = human.Position;
            var abandoned = Plugin.Store.Index.AbandonedAt(p.x, p.z);
            if (abandoned != null && abandoned.OwnerId != id && AbandonedHinted.Add((id, abandoned.Id)))
            {
                bool hasAnchor = abandoned.AnchorId != 0 && Thing.Find(abandoned.AnchorId) != null;
                Notify.Chat(id, hasAnchor
                    ? L.T("abandoned.enter_take", ("owner", abandoned.OwnerName))
                    : L.T("abandoned.enter_build", ("owner", abandoned.OwnerName), ("anchor", L.Anchor)));
            }
        }

    }
}
