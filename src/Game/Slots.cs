using System;
using System.Collections.Generic;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Claim slots, donations, the players' area accounts and the penalty (design section 0.16):
    ///  - every player has <see cref="Max"/> slots; a claim needs one per started 50 m in each direction;
    ///  - friends donate slots to a claim (/claim donate); taking them back can leave the claim short → penalty:
    ///    pay (/claim pay) or after 3 hours the claim shrinks to 50 x 50 m around its autolathe;
    ///  - every player has an account of square meters (start credit + earned online) that pays for growing.
    /// </summary>
    public static class Slots
    {
        public const int PenaltyMinutes = 180;
        public const float PenaltyFactor = 5f;
        public const float BuiltCell = 2f; // built area is counted in 2 x 2 m cells (the game's big grid)

        public static int Max => Math.Max(1, Plugin.Settings.ClaimSlotsPerPlayer.Value);

        /// <summary>Owners add 10 x 10 m fields themselves (growing on, autolathe as anchor).</summary>
        public static bool FieldMode => ClaimActions.Growing && !Staking.Enabled;

        // ------------------------------------------------------------------ counting

        public static int OwnUsed(ulong id, IEnumerable<Claim> claims)
        {
            int n = 0;
            foreach (var c in claims)
                if (c.IsActive && c.OwnerId == id) n += Math.Max(1, c.OwnSlots);
            return n;
        }

        public static int DonatedTotal(ulong id, IEnumerable<Claim> claims)
        {
            int n = 0;
            foreach (var c in claims)
                if (c.IsActive) n += c.DonatedBy(id);
            return n;
        }

        public static int Free(ulong id, IEnumerable<Claim> claims) => Max - OwnUsed(id, claims) - DonatedTotal(id, claims);

        public static int Free(ulong id) => Free(id, Plugin.Store.Index.Claims);

        /// <summary>
        /// May this player found a claim? Yes with a free slot – or if they own none and gave all their slots away:
        /// then founding takes all their donations back.
        /// </summary>
        public static bool CanFound(ulong id)
        {
            var claims = Plugin.Store.Index.Claims;
            if (Free(id, claims) >= 1) return true;
            return OwnUsed(id, claims) == 0 && DonatedTotal(id, claims) > 0;
        }

        /// <summary>Inside a store batch: a new claim of <paramref name="id"/> is about to be added – take donations back if needed.</summary>
        public static void BeforeFounding(List<Claim> list, ulong id)
        {
            if (Free(id, list) >= 1 || OwnUsed(id, list) > 0) return;
            var touched = new HashSet<ulong>();
            foreach (var c in list)
                if (c.DonatedBy(id) > 0)
                {
                    c.Donations.RemoveAll(d => d.SteamId == id);
                    touched.Add(c.OwnerId);
                }
            foreach (var owner in touched) SettleOwner(list, owner);
        }

        /// <summary>Inside a store batch: brings the slot numbers of all claims of an owner in order.</summary>
        public static void SettleOwner(List<Claim> list, ulong owner)
        {
            foreach (var c in list)
            {
                if (!c.IsActive || c.OwnerId != owner) continue;
                int free = Free(owner, list);
                c.SettleSlots(Math.Max(0, free));
            }
        }

        /// <summary>
        /// Every minute: claims that are short of slots get another look – slots may have become free meanwhile
        /// (a claim deleted or ended, a donation taken back elsewhere).
        /// </summary>
        public static void SettleShortClaims()
        {
            Plugin.Store.Batch(list =>
            {
                var owners = new HashSet<ulong>();
                foreach (var c in list)
                    if (c.IsActive && c.MissingSlots > 0) owners.Add(c.OwnerId);
                foreach (var owner in owners) SettleOwner(list, owner);
                return owners.Count > 0;
            });
        }

        /// <summary>Inside a store batch: a player no longer donates to this claim (removed, left, claim gone).</summary>
        public static void DropDonation(List<Claim> list, Claim c, ulong id)
        {
            if (c.Donations.RemoveAll(d => d.SteamId == id) == 0) return;
            SettleOwner(list, c.OwnerId);
            SettleOwner(list, id); // the slots they get back may settle a claim of their own
        }

        // ------------------------------------------------------------------ accounts

        /// <summary>The player's account (with the start credit, given once).</summary>
        public static KnownPlayer Account(ulong id)
        {
            var p = Plugin.Players.GetOrAdd(id);
            if (!p.AreaGiven)
            {
                p.AreaGiven = true;
                p.Area += Math.Max(0, Plugin.Settings.StartFreeArea.Value);
            }
            return p;
        }

        public static float Balance(ulong id) => Account(id).Area;

        /// <summary>Who pays for growing a claim, in order: the owner, then the donors.</summary>
        public static List<ulong> Payers(Claim c)
        {
            var payers = new List<ulong> { c.OwnerId };
            foreach (var d in c.Donations)
                if (d.Slots > 0 && !payers.Contains(d.SteamId)) payers.Add(d.SteamId);
            return payers;
        }

        /// <summary>Area on the accounts of everyone who pays for this claim.</summary>
        public static float PayersBalance(Claim c)
        {
            float sum = 0f;
            foreach (var id in Payers(c)) sum += Balance(id);
            return sum;
        }

        /// <summary>Takes <paramref name="area"/> m² from the payers' accounts (owner first). Returns what was taken.</summary>
        public static float TakeArea(Claim c, float area)
        {
            float taken = 0f;
            foreach (var id in Payers(c))
            {
                if (area - taken <= 0.001f) break;
                var acc = Account(id);
                float t = Math.Min(acc.Area, area - taken);
                acc.Area -= t;
                taken += t;
            }
            return taken;
        }

        /// <summary>Does the player own a claim or donate to one (only then their account grows)?</summary>
        public static bool TakesPart(ulong id)
        {
            foreach (var c in Plugin.Store.Index.Active)
                if (c.OwnerId == id || c.DonatedBy(id) > 0) return true;
            return false;
        }

        // ------------------------------------------------------------------ penalty

        /// <summary>
        /// Built area that would end up outside if the claim shrank for the penalty (<see cref="ClaimGeometry.PenaltyRest"/>:
        /// 2 x 2 m cells with structures), and the steel to keep the claim as it is: that area x price x 5.
        /// </summary>
        public static double PenaltySteel(Claim c, out float builtArea)
        {
            var rest = ClaimGeometry.PenaltyRest(c);
            var cells = new HashSet<(int, int)>();
            foreach (var s in World.StructuresIn(c.MinX, c.MaxX, c.MinZ, c.MaxZ))
            {
                var p = s.Position;
                if (!c.Contains(p.x, p.z)) continue;
                if (rest.Contains(p.x, p.z)) continue;
                cells.Add(((int)Math.Floor(p.x / BuiltCell), (int)Math.Floor(p.z / BuiltCell)));
            }
            builtArea = cells.Count * BuiltCell * BuiltCell;
            return Math.Ceiling(builtArea * PricePerSquareMeter(c) * PenaltyFactor - 1e-9);
        }

        public static double PricePerSquareMeter(Claim c) =>
            Plugin.Settings.SteelPerSquareMeter.Value * (1.0 + Plugin.Settings.PriceIncreasePer100SquareMeters.Value * c.Area / 100.0);

        /// <summary>The penalty was paid: the claim stays as it is; the missing slots count as bought.</summary>
        public static void Paid(string claimId)
        {
            Plugin.Store.Update(claimId, c =>
            {
                c.PaidSlots += c.MissingSlots;
                c.MissingSlots = 0;
                c.PenaltyLeft = 0;
            });
        }

        /// <summary>
        /// Not paid in time (or nothing built that would be lost): the claim shrinks to 50 x 50 m around its autolathe.
        /// What the owner loses of the area their own slots allowed goes back to their account. Returns the m² credited.
        /// </summary>
        public static float ShrinkForPenalty(string claimId)
        {
            float credit = 0f;
            ulong owner = 0;
            Plugin.Store.Batch(list =>
            {
                var c = list.Find(x => x.Id == claimId);
                if (c == null || !c.HasFields) return false;
                owner = c.OwnerId;
                float before = c.Area;
                float allowed = Math.Max(1, c.OwnSlots) * Claim.SlotSpan * Claim.SlotSpan;
                c.Fields = ClaimGeometry.PenaltyRest(c).Fields;
                c.UpdateBounds();
                credit = Math.Max(0f, Math.Min(before, allowed) - c.Area);
                c.PaidSlots = 0;
                c.MissingSlots = 0;
                c.PenaltyLeft = 0;
                SettleOwner(list, c.OwnerId);
                return true;
            });
            if (credit > 0f) Account(owner).Area += credit;
            return credit;
        }

        /// <summary>
        /// Every minute (<paramref name="tick"/>) and when a player joins: penalties of claims whose owner is online.
        /// The first time: a window, and the 3 hours start – counted only while the owner is online. If nothing built
        /// would be lost, the claim shrinks at once. At the end it shrinks.
        /// </summary>
        public static void CheckPenalties(Func<ulong, bool> isOnline, bool tick, ulong joined = 0)
        {
            foreach (var c in Plugin.Store.Index.Active)
            {
                if (c.MissingSlots <= 0 || !c.HasFields || !isOnline(c.OwnerId)) continue;
                if (c.PenaltyLeft <= 0)
                {
                    double steel = PenaltySteel(c, out float built);
                    if (built <= 0f)
                    {
                        Shrink(c, nothingLost: true);
                        continue;
                    }
                    Plugin.Store.Update(c.Id, live => live.PenaltyLeft = PenaltyMinutes);
                    Notify.Popup(c.OwnerId, Texts.Penalty(Plugin.Store.Index.FindById(c.Id) ?? c, steel, built));
                    continue;
                }
                if (tick)
                {
                    if (c.PenaltyLeft <= 1) { Shrink(c, nothingLost: false); continue; }
                    Plugin.Store.Update(c.Id, live => live.PenaltyLeft--);
                }
                if (joined == c.OwnerId)
                {
                    double steel = PenaltySteel(c, out float built);
                    Notify.Popup(c.OwnerId, Texts.Penalty(Plugin.Store.Index.FindById(c.Id) ?? c, steel, built));
                }
            }
        }

        /// <summary>Shrink a claim for its penalty now and tell the owner.</summary>
        public static void Shrink(Claim c, bool nothingLost)
        {
            float credit = ShrinkForPenalty(c.Id);
            var now = Plugin.Store.Index.FindById(c.Id);
            Notify.Popup(c.OwnerId, Texts.PenaltyShrunk(now ?? c, credit, nothingLost));
            BorderDisplay.ShowOwnClaim(c.OwnerId, now);
        }
    }
}
