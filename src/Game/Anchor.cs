using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Entities;
using HarmonyLib;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// The claim anchor (autolathe, or flag per config):
    ///  - building one outside all claims creates a claim (5 m in every direction + free meters; with growing switched
    ///    off: the base around it), or takes over an abandoned claim it stands in;
    ///  - building one inside your own claim makes it a backup anchor;
    ///  - removing the anchor hands the claim to another anchor inside it, or ends the claim.
    /// With growing switched off, every structure the owner or a friend builds at the edge of their claim moves the edge.
    /// The builder is the verified sender (or the host), never what the client wrote into the message.
    /// </summary>
    public static class Anchor
    {
        private static bool _validated;

        public static void Register()
        {
            var t = typeof(Anchor);
            Hooks.Add(Hooks.Kind.Feature, "Claim by building an autolathe/flag", "Structure.SetStructureData",
                () => AccessTools.Method(typeof(Structure), "SetStructureData",
                    new[] { typeof(Quaternion), typeof(ulong), typeof(Grid3), typeof(int) }),
                t, postfix: nameof(StructureCreatedPostfix));
            Hooks.Add(Hooks.Kind.Feature, "Hand over or end a claim when its autolathe/flag is removed", "OnServer.Destroy",
                () => AccessTools.Method(typeof(OnServer), "Destroy", new[] { typeof(Thing) }),
                t, prefix: nameof(DestroyPrefix));
        }

        public static string AnchorName => Plugin.Settings.Anchor.Value == ClaimAnchor.Flag ? "flag" : "autolathe";

        public static bool IsAnchor(Thing thing) =>
            Plugin.Settings.Anchor.Value == ClaimAnchor.Flag ? thing is Flag : thing is Autolathe;

        private static bool InWorld =>
            GameManager.RunSimulation &&
            (GameManager.GameState == GameState.Running || GameManager.GameState == GameState.Paused);

        // ------------------------------------------------------------------ built

        private static void StructureCreatedPostfix(Structure __instance, ulong __1)
        {
            try
            {
                if (!InWorld || __instance == null) return;
                if (!TryGetBuilder(__1, out ulong builder, out string name)) return;
                if (Pins.IsLocked(builder)) return; // joined from a new address: claim rights wait for the PIN

                var p = __instance.Position;
                bool follow = !ClaimActions.Growing && !Staking.Enabled; // growing switched off: claims follow their base
                if (!IsAnchor(__instance))
                {
                    if (follow) ClaimActions.FollowBase(builder, p);
                    BuildGuide.OnBuilt(builder, name, p);
                    return;
                }
                if (Staking.Enabled)
                {
                    Staking.OnFlagBuilt(builder, name, __instance);
                    return;
                }

                var index = Plugin.Store.Index;
                var inside = index.At(p.x, p.z);
                if (inside != null)
                {
                    // an ordinary machine in an active claim; for its people it is a backup anchor
                    if (inside.RoleOf(builder) == Role.Owner || inside.RoleOf(builder) == Role.Friend)
                        Notify.Chat(builder, L.T("anchor.backup", ("anchor", L.Anchor), ("claim", inside.DisplayName)));
                    if (follow) ClaimActions.FollowBase(builder, p);
                    return;
                }
                if (!Slots.CanFound(builder))
                {
                    if (follow) ClaimActions.FollowBase(builder, p);
                    return;
                }

                var result = ClaimActions.CreateFromAnchor(builder, name, p, __instance.ReferenceId);
                Plugin.Log.LogInfo($"{AnchorName} built by {name} ({builder}): {result.Message}");
                if (!result.Success)
                {
                    Notify.Popup(builder, Texts.Refused(result.Message, result.Hint));
                    BorderDisplay.ShowNear(builder, BorderDisplay.Duration);
                    return;
                }
                Notify.Popup(builder, result.TookOver ? Texts.TakenOver(result.Claim) : Texts.Created(result.Claim, result.Note, Texts.PinTipFor(builder)));
                BorderDisplay.ShowNewClaim(builder, result.Claim);
                BuildGuide.MarkShown(builder);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Creating a claim for a new {AnchorName} failed: {ex}");
            }
        }

        /// <summary>Who built it: the verified network sender, or the host for local actions.</summary>
        private static bool TryGetBuilder(ulong claimedOwner, out ulong steamId, out string name)
        {
            steamId = 0;
            name = null;
            switch (SenderContext.CurrentOrigin)
            {
                case SenderContext.Origin.Network:
                    if (!SenderContext.TryGetSender(out var sender) || sender.SteamId != claimedOwner) return false;
                    steamId = sender.SteamId;
                    name = World.NameOf(steamId);
                    return true;
                case SenderContext.Origin.Local:
                    var host = GameManager.IsBatchMode ? null : Human.LocalHuman;
                    if (host == null) return false;
                    steamId = Identity.SteamIdOf(host);
                    name = host.DisplayName;
                    return Identity.IsValidSteamId(steamId) && steamId == claimedOwner;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ removed

        private static void DestroyPrefix(Thing __0)
        {
            try
            {
                if (!InWorld || __0 == null || !IsAnchor(__0)) return;
                if (Staking.Enabled && Staking.OnFlagRemoved(__0)) return; // a corner flag of a staked claim
                var claim = Plugin.Store.Index.ByAnchor(__0.ReferenceId);
                if (claim == null) return;
                AnchorGone(claim, __0.ReferenceId);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Handling a removed {AnchorName} failed: {ex}");
            }
        }

        private static void AnchorGone(Claim claim, long anchorId)
        {
            if (!claim.IsActive)
            {
                Plugin.Store.Update(claim.Id, c => c.AnchorId = 0); // can still be taken over with a new anchor
                return;
            }
            var next = ClaimActions.HandOver(claim, anchorId);
            if (next != null)
            {
                Plugin.Log.LogInfo($"{claim.DisplayName}: anchor moved to #{next.ReferenceId}.");
                Notify.Chat(claim.OwnerId, L.T("anchor.handover", ("claim", claim.DisplayName), ("anchor", L.Anchor),
                    ("x", $"{next.Position.x:0}"), ("z", $"{next.Position.z:0}")));
                return;
            }
            Plugin.Log.LogInfo($"{claim.DisplayName} ended: its {AnchorName} was removed or destroyed.");
            Notify.Popup(claim.OwnerId, Texts.Ended(claim));
        }

        /// <summary>After loading: anchors that no longer exist are handed over or the claim ends.</summary>
        public static void ValidateAfterLoad()
        {
            if (_validated || !GameManager.RunSimulation) return;
            _validated = true;
            try
            {
                foreach (var claim in new List<Claim>(Plugin.Store.Index.Claims))
                {
                    if (claim.Staked) continue; // saved with its border; only taking flags down changes it
                    if (claim.AnchorId == 0) continue;
                    var anchor = Thing.Find(claim.AnchorId);
                    if (anchor != null && !anchor.BeingDestroyed) continue;
                    Plugin.Log.LogWarning($"{claim.DisplayName}: its {AnchorName} (#{claim.AnchorId}) no longer exists.");
                    AnchorGone(claim, claim.AnchorId);
                }
                Markers.Update();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Checking claim anchors failed: {ex}");
            }
        }

        public static void OnWorldLeft() => _validated = false;
    }

    /// <summary>Windows with an OK button (the game's server announcement) and private chat lines.</summary>
    public static class Notify
    {
        public static bool IsHost(ulong steamId) =>
            !GameManager.IsBatchMode && Human.LocalHuman != null && Identity.SteamIdOf(Human.LocalHuman) == steamId;

        public static Client ClientOf(ulong steamId)
        {
            foreach (var client in NetworkBase.Clients)
                if (client != null && !client.IsHost && client.ClientId == steamId) return client;
            return null;
        }

        public static void Popup(ulong steamId, string text)
        {
            try
            {
                if (IsHost(steamId)) { AnnounceMessage.Display(text); return; }
                var client = ClientOf(steamId);
                if (client != null)
                    NetworkServer.SendToClient(new AnnounceMessage { AnnounceText = text }, NetworkChannel.GeneralTraffic, client);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not show a message to {steamId}: {ex.Message}");
            }
        }

        public static void Chat(ulong steamId, string text)
        {
            if (IsHost(steamId)) { Identity.PrintLocal(text); return; }
            RemoteChat.SendPrivate(ClientOf(steamId), text);
        }
    }
}
