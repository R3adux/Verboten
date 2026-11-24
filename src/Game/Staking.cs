using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Anchor setting "Flag": players stake out their claim like a plot of land (design 0.13).
    ///  - A flag at each corner: as soon as a player has <see cref="ClaimGeometry.FlagsNeeded"/> of their own flags
    ///    outside claims within 50 m, the rectangle around the outermost ones becomes their claim (10 x 10 to 50 x 50 m).
    ///  - Another flag next to the claim (owner or friend) moves the border out to it.
    ///  - Taking a flag down moves the border in; with fewer than 4 flags left the claim ends.
    ///  - A flag in an abandoned claim takes it over (like an autolathe).
    /// The game remembers who built each flag, so nothing is lost when the server restarts mid-way.
    /// </summary>
    public static class Staking
    {
        public static bool Enabled => Plugin.Settings.Anchor.Value == ClaimAnchor.Flag;

        /// <summary>A flag was built by <paramref name="builder"/> (verified).</summary>
        public static void OnFlagBuilt(ulong builder, string name, Structure flag)
        {
            var p = flag.Position;
            var index = Plugin.Store.Index;

            if (index.At(p.x, p.z) != null) return; // inside a claim: changes nothing (strangers cannot build there anyway)

            var abandoned = index.AbandonedAt(p.x, p.z);
            if (abandoned != null)
            {
                var taken = ClaimActions.TakeOver(abandoned, builder, name, flag.ReferenceId, p);
                if (!taken.Success) { Notify.Chat(builder, taken.Message); return; }
                Notify.Popup(builder, Texts.TakenOver(taken.Claim));
                BorderDisplay.ShowNewClaim(builder, taken.Claim);
                BuildGuide.MarkShown(builder);
                return;
            }

            // next to a staked claim of ours: the border moves out to this flag
            foreach (var c in index.Active)
            {
                if (!c.Staked || !IsOurs(c, builder)) continue;
                float w = Math.Max(c.MaxX, p.x) - Math.Min(c.MinX, p.x), d = Math.Max(c.MaxZ, p.z) - Math.Min(c.MinZ, p.z);
                if (w > ClaimGeometry.MaxSpan + 0.01f || d > ClaimGeometry.MaxSpan + 0.01f) continue; // too far for this one
                string why = ClaimActions.ExtendToFlag(c, p);
                if (why != null)
                {
                    Notify.Chat(builder, L.T("flag.cannot_move", ("claim", c.DisplayName), ("why", why)));
                    BorderDisplay.ShowNear(builder, BorderDisplay.Duration);
                    return;
                }
                var now = Plugin.Store.Index.FindById(c.Id);
                Notify.Chat(builder, L.T("flag.moved", ("claim", now.DisplayName), ("w", $"{now.Width:0}"), ("d", $"{now.Depth:0}")));
                BorderDisplay.ShowClaim(builder, now, BorderDisplay.Duration);
                return;
            }

            if (!Slots.CanFound(builder))
            {
                Notify.Chat(builder, L.T("flag.too_far", ("max", L.MaxSize)));
                return;
            }

            // a new stake: this player's own flags outside claims, near this one
            var flags = new List<(float X, float Z)> { (p.x, p.z) };
            foreach (var f in UnityEngine.Object.FindObjectsOfType<Flag>())
            {
                if (f == null || f.BeingDestroyed || f.ReferenceId == flag.ReferenceId || f.OwnerClientId != builder) continue;
                var fp = f.Position;
                if (Math.Abs(fp.x - p.x) > ClaimGeometry.StakeRange || Math.Abs(fp.z - p.z) > ClaimGeometry.StakeRange) continue;
                if (index.At(fp.x, fp.z) != null || index.AbandonedAt(fp.x, fp.z) != null) continue;
                flags.Add((fp.x, fp.z));
            }

            if (flags.Count < ClaimGeometry.FlagsNeeded)
            {
                Notify.Chat(builder, L.T("flag.count", ("n", flags.Count), ("flags", ClaimGeometry.FlagsNeeded)));
                BuildGuide.OnBuilt(builder, name, p); // first time: the window that explains claims
                return;
            }

            var result = ClaimActions.CreateStaked(builder, name, flags, flag.ReferenceId, p.y);
            Plugin.Log.LogInfo($"Flags of {name} ({builder}): {result.Message}");
            if (!result.Success)
            {
                Notify.Popup(builder, Texts.StakeRefused(result.Message, result.Hint));
                BorderDisplay.ShowNear(builder, BorderDisplay.Duration);
                return;
            }
            Notify.Popup(builder, Texts.Created(result.Claim, null, Texts.PinTipFor(builder)));
            BorderDisplay.ShowNewClaim(builder, result.Claim);
            BuildGuide.MarkShown(builder);
        }

        /// <summary>A flag is being removed. True if it belonged to a staked claim (handled here).</summary>
        public static bool OnFlagRemoved(Thing flag)
        {
            var p = flag.Position;
            var index = Plugin.Store.Index;
            var claim = index.At(p.x, p.z) ?? index.AbandonedAt(p.x, p.z);
            if (claim == null || !claim.Staked) return false;
            if (ClaimActions.Restake(claim, flag.ReferenceId))
            {
                var now = Plugin.Store.Index.FindById(claim.Id);
                if (now != null && (now.Width < claim.Width - 0.01f || now.Depth < claim.Depth - 0.01f))
                    Notify.Chat(claim.OwnerId, L.T("flag.removed", ("claim", now.DisplayName), ("w", $"{now.Width:0}"), ("d", $"{now.Depth:0}")));
                return true;
            }
            Plugin.Log.LogInfo($"{claim.DisplayName} ended: fewer than {ClaimGeometry.FlagsNeeded} flags left.");
            if (claim.IsActive) Notify.Popup(claim.OwnerId, Texts.StakeEnded(claim));
            return true;
        }

        private static bool IsOurs(Claim c, ulong steamId)
        {
            var role = c.RoleOf(steamId);
            return role == Role.Owner || role == Role.Friend;
        }
    }
}
