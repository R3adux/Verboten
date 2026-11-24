using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Everything that creates, changes or ends claims. All rules from design section 0 live here or in
    /// <see cref="ClaimGeometry"/>; commands, the anchor hooks and the upkeep only call these.
    /// </summary>
    public static class ClaimActions
    {
        public sealed class Result
        {
            public bool Success;
            public Claim Claim;
            public string Message;
            /// <summary>Set when the player should move: suggested direction and distance.</summary>
            public string Hint;
            /// <summary>An abandoned claim was taken over (instead of a new one created).</summary>
            public bool TookOver;
            /// <summary>Growing switched off: whose structures kept the claim from covering the whole base.</summary>
            public string Note;

            public static Result Fail(string message, string hint = null) => new Result { Message = message, Hint = hint };
        }

        /// <summary>Owners make their claims bigger themselves (setting). Off: claims follow their base.</summary>
        public static bool Growing => Plugin.Settings.GrowingEnabled.Value;

        // ------------------------------------------------------------------ create / check

        /// <summary>Would an anchor built here create a claim? (Used by '/claim check' and by the anchor hook.)</summary>
        public static Result Evaluate(ulong owner, string ownerName, Vector3 at, long anchorId, bool ignoreLimit = false)
        {
            var index = Plugin.Store.Index;
            var inside = index.At(at.x, at.z);
            if (inside != null) return Result.Fail(L.T("eval.inside", ("claim", inside.DisplayName)));
            if (!ignoreLimit && !Slots.CanFound(owner))
                return Result.Fail(L.T("eval.limit", ("max", Slots.Max)));

            var abandoned = index.AbandonedAt(at.x, at.z);
            if (abandoned != null)
                return new Result { Success = true, Claim = abandoned, Message = L.T("eval.abandoned", ("owner", abandoned.OwnerName)) };

            var candidate = Claim.Create(owner, ownerName, at.x, at.y, at.z, anchorId, ClaimGeometry.StartReach, Slots.FieldMode);
            ClaimGeometry.AlignToOwnClaims(candidate, index.Active); // next to an own claim: seamless
            var problem = ClaimGeometry.Check(candidate, index.Active, World.LandingZones());
            if (problem != null) return Result.Fail(L.Problem(problem) + ".", L.MoveHint(problem));
            var foreign = World.ForeignActiveStructures(candidate.MinX, candidate.MaxX, candidate.MinZ, candidate.MaxZ, id => id == owner);
            if (foreign.Count > 0)
                return Result.Fail(L.T("eval.foreign", ("player", World.NameOf(foreign[0].OwnerClientId))));
            string note = null;
            if (!Growing && !Staking.Enabled) FitToBase(candidate, out note);
            return new Result { Success = true, Claim = candidate, Note = note, Message = L.T("eval.ok") };
        }

        /// <summary>
        /// Cheap version of <see cref="Evaluate"/> for every structure a player without a claim builds: only distances
        /// (landing zone, other claims), no search through all structures. Why no claim is possible here, or null.
        /// </summary>
        public static string QuickProblem(ulong owner, Vector3 at)
        {
            var index = Plugin.Store.Index;
            if (index.At(at.x, at.z) != null || index.AbandonedAt(at.x, at.z) != null) return null;
            var candidate = Claim.Create(owner, "", at.x, at.y, at.z, 0, ClaimGeometry.StartReach);
            var problem = ClaimGeometry.Check(candidate, index.Active, World.LandingZones());
            if (problem == null) return null;
            string hint = L.MoveHint(problem);
            return L.Problem(problem) + "." + (hint != null ? " " + hint : "");
        }

        /// <summary>An anchor was built: create the claim (or take over an abandoned one).</summary>
        public static Result CreateFromAnchor(ulong owner, string ownerName, Vector3 at, long anchorId)
        {
            var eval = Evaluate(owner, ownerName, at, anchorId);
            if (!eval.Success) return eval;
            if (!eval.Claim.IsActive) return TakeOver(eval.Claim, owner, ownerName, anchorId, at);

            var claim = eval.Claim;
            Plugin.Store.Batch(list =>
            {
                Slots.BeforeFounding(list, owner);
                CutBackAbandoned(list, claim);
                list.Add(claim);
                return true;
            });
            return new Result { Success = true, Claim = claim, Note = eval.Note, Message = L.T("eval.created", ("claim", claim.DisplayName)) };
        }

        // ------------------------------------------------------------------ growing switched off: claims follow the base

        private static bool IsOurs(Claim c, ulong steamId)
        {
            var role = c.RoleOf(steamId);
            return role == Role.Owner || role == Role.Friend;
        }

        /// <summary>
        /// Grows <paramref name="c"/> over the base around its anchor – everything its owner and friends built within
        /// reach – as far as the rules allow. <paramref name="note"/> names other players whose structures were in the way.
        /// </summary>
        public static bool FitToBase(Claim c, out string note)
        {
            note = null;
            // other players' buildings matter up to the 10 m zone beyond the largest possible claim
            float reach = ClaimGeometry.MaxReach, look = reach + ClaimGeometry.Zone;
            float minX = c.AnchorX, maxX = c.AnchorX, minZ = c.AnchorZ, maxZ = c.AnchorZ;
            var blocked = new List<(float X, float Z)>();
            var blockers = new List<(Vector3 Position, ulong Owner)>();
            foreach (var s in World.StructuresIn(c.AnchorX - look, c.AnchorX + look, c.AnchorZ - look, c.AnchorZ + look))
            {
                var p = s.Position;
                if (IsOurs(c, s.Owner))
                {
                    if (Math.Abs(p.x - c.AnchorX) > reach || Math.Abs(p.z - c.AnchorZ) > reach) continue;
                    minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
                    minZ = Math.Min(minZ, p.z); maxZ = Math.Max(maxZ, p.z);
                }
                else if (World.IsActivePlayer(s.Owner))
                {
                    blocked.Add((p.x, p.z));
                    blockers.Add(s);
                }
            }
            bool changed = ClaimGeometry.GrowToCover(c, minX, maxX, minZ, maxZ, Plugin.Store.Index.Active, World.LandingZones(), blocked);

            // whose buildings kept it from covering the whole base (they are in the base or within 10 m of it)
            float m = ClaimGeometry.BaseMargin, near = m + ClaimGeometry.Zone;
            bool covered = c.MinX <= minX - m + 0.01f && c.MaxX >= maxX + m - 0.01f && c.MinZ <= minZ - m + 0.01f && c.MaxZ >= maxZ + m - 0.01f;
            var names = new List<string>();
            foreach (var b in covered ? new List<(Vector3 Position, ulong Owner)>() : blockers)
            {
                var p = b.Position;
                if (p.x < minX - near || p.x > maxX + near || p.z < minZ - near || p.z > maxZ + near) continue;
                string name = World.NameOf(b.Owner);
                if (!names.Contains(name) && names.Count < 3) names.Add(name);
            }
            if (names.Count > 0)
                note = L.T("fit.note", ("players", string.Join(", ", names)));
            return changed;
        }

        /// <summary>After a friend was added: the claim also covers their part of the base. True if it grew.</summary>
        public static bool RefitBase(string claimId)
        {
            var live = Plugin.Store.Index.FindById(claimId);
            if (live == null || !live.IsActive || live.Staked || live.HasFields) return false;
            var probe = live.Clone();
            if (!FitToBase(probe, out _)) return false;
            SetBounds(claimId, probe);
            return true;
        }

        /// <summary>
        /// The owner or a friend built something near the edge of their claim: the claim follows, so the base keeps
        /// <see cref="ClaimGeometry.BaseMargin"/> to the edge. Cheap unless the claim really has to grow.
        /// </summary>
        public static void FollowBase(ulong builder, Vector3 p)
        {
            var index = Plugin.Store.Index;
            Claim claim = null;
            foreach (var c in index.Active)
                if (c.Near(p.x, p.z, ClaimGeometry.BaseMargin) && IsOurs(c, builder)) { claim = c; break; }
            if (claim == null || claim.Staked || claim.HasFields) return; // staked claims: the flags decide; field claims: the owner
            float m = ClaimGeometry.BaseMargin;
            if (p.x - m >= claim.MinX && p.x + m <= claim.MaxX && p.z - m >= claim.MinZ && p.z + m <= claim.MaxZ) return;

            var zones = World.LandingZones();
            var probe = claim.Clone();
            if (!ClaimGeometry.GrowToCover(probe, p.x, p.x, p.z, p.z, index.Active, zones, null)) return;
            // only now look for other players' structures in the way (expensive); they matter up to the 10 m zone
            var blocked = new List<(float X, float Z)>();
            float zone = ClaimGeometry.Zone;
            foreach (var s in World.StructuresIn(probe.MinX - zone, probe.MaxX + zone, probe.MinZ - zone, probe.MaxZ + zone))
                if (!IsOurs(claim, s.Owner) && World.IsActivePlayer(s.Owner)) blocked.Add((s.Position.x, s.Position.z));
            if (blocked.Count > 0)
            {
                probe = claim.Clone();
                if (!ClaimGeometry.GrowToCover(probe, p.x, p.x, p.z, p.z, index.Active, zones, blocked)) return;
            }
            SetBounds(claim.Id, probe);
            Plugin.Log.LogDebug($"{claim.DisplayName} grew with its base to {probe.Width:0} x {probe.Depth:0} m.");
        }

        private static void SetBounds(string claimId, Claim bounds)
        {
            Plugin.Store.Batch(list =>
            {
                var live = list.Find(x => x.Id == claimId);
                if (live == null) return false;
                live.MinX = bounds.MinX; live.MaxX = bounds.MaxX; live.MinZ = bounds.MinZ; live.MaxZ = bounds.MaxZ;
                CutBackAbandoned(list, live);
                return true;
            });
        }

        /// <summary>Abandoned claims give way: cut them back so they keep the gap to <paramref name="active"/>.</summary>
        private static void CutBackAbandoned(List<Claim> live, Claim active)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var c = live[i];
                if (c.IsActive || c.Id == active.Id) continue;
                if (ClaimGeometry.Apart(c, active)) continue;
                if (!ClaimGeometry.ShrinkToClear(c, active))
                {
                    Plugin.Log.LogInfo($"Abandoned {c.DisplayName} dissolved: no room left next to {active.DisplayName}.");
                    Markers.DestroyBeacon(c.MarkerId);
                    live.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------------------ abandoned

        public static void Abandon(Claim claim)
        {
            Plugin.Store.Update(claim.Id, c => { c.State = ClaimState.Abandoned; c.Donations.Clear(); });
            Plugin.Log.LogInfo($"{claim.DisplayName} abandoned (owner and friends away for {Plugin.Settings.ClaimExpiryDays.Value} days).");
        }

        /// <summary>
        /// The owner came back before anyone took it: it is theirs again – if they still have a free claim slot
        /// (they may have founded other claims meanwhile). False if it stays abandoned for that reason.
        /// </summary>
        public static bool Reactivate(Claim claim)
        {
            if (Slots.Free(claim.OwnerId) < 1) return false;
            Plugin.Store.Batch(list =>
            {
                var c = list.Find(x => x.Id == claim.Id);
                if (c == null) return false;
                // the land around may have been claimed meanwhile: cut back where needed
                foreach (var other in list)
                {
                    if (!other.IsActive || other.Id == c.Id) continue;
                    if (ClaimGeometry.Apart(c, other)) continue;
                    if (!ClaimGeometry.ShrinkToClear(c, other))
                    {
                        Markers.DestroyBeacon(c.MarkerId);
                        list.Remove(c);
                        return true;
                    }
                }
                // while it was open, other active players may have built in it: their buildings stay theirs
                foreach (var s in World.ForeignActiveStructures(c.MinX, c.MaxX, c.MinZ, c.MaxZ, id => IsOurs(c, id)))
                {
                    var p = s.Position;
                    if (!ClaimGeometry.ShrinkToClear(c, p.x, p.x, p.z, p.z, 0f))
                    {
                        Markers.DestroyBeacon(c.MarkerId);
                        list.Remove(c);
                        return true;
                    }
                }
                c.State = ClaimState.Active;
                c.AbsentMinutes = 0;
                Markers.DestroyBeacon(c.MarkerId);
                c.MarkerId = 0;
                // its donations ended when it was abandoned: count again what it needs (may mean a penalty)
                c.OwnSlots = 1;
                Slots.SettleOwner(list, c.OwnerId);
                return true;
            });
            return true;
        }

        /// <summary>
        /// Take over an abandoned claim: new owner, friends cleared, sides cut back where active players built
        /// or other claims came close. <paramref name="anchorId"/> 0 = keep the old anchor (must still exist).
        /// </summary>
        public static Result TakeOver(Claim abandoned, ulong owner, string ownerName, long anchorId, Vector3 anchorAt)
        {
            if (!Slots.CanFound(owner))
                return Result.Fail(L.T("eval.limit", ("max", Slots.Max)));

            var c = abandoned.Clone();
            ulong oldOwner = c.OwnerId;
            string oldName = c.DisplayName;
            c.OwnerId = owner;
            c.OwnerName = ownerName;
            c.Name = null;
            c.Members.Clear();
            c.Incidents.Clear();
            c.State = ClaimState.Active;
            c.AbsentMinutes = 0;
            c.OnlineMinutes = 0;
            if (anchorId != 0)
            {
                c.AnchorId = anchorId;
                c.AnchorX = anchorAt.x; c.AnchorY = anchorAt.y; c.AnchorZ = anchorAt.z;
            }

            foreach (var s in World.ForeignActiveStructures(c.MinX, c.MaxX, c.MinZ, c.MaxZ, id => id == owner))
            {
                var p = s.Position;
                if (!ClaimGeometry.ShrinkToClear(c, p.x, p.x, p.z, p.z, 0f))
                    return Result.Fail(L.T("takeover.foreign", ("player", World.NameOf(s.OwnerClientId))));
            }
            foreach (var other in Plugin.Store.Index.Active)
            {
                if (ClaimGeometry.Apart(c, other)) continue;
                if (!ClaimGeometry.ShrinkToClear(c, other))
                    return Result.Fail(L.T("takeover.too_close", ("claim", other.DisplayName)));
            }
            var problem = ClaimGeometry.Check(c, Plugin.Store.Index.Active, World.LandingZones());
            if (problem != null) return Result.Fail(L.Problem(problem) + ".");

            Markers.DestroyBeacon(abandoned.MarkerId);
            Plugin.Store.Batch(list =>
            {
                var live = list.Find(x => x.Id == c.Id);
                if (live == null) return false;
                Slots.BeforeFounding(list, owner);
                live.MarkerId = 0;
                live.OwnerId = c.OwnerId; live.OwnerName = c.OwnerName; live.Name = null;
                live.Members.Clear(); live.Incidents.Clear();
                live.State = ClaimState.Active; live.AbsentMinutes = 0; live.OnlineMinutes = 0;
                live.AnchorId = c.AnchorId; live.AnchorX = c.AnchorX; live.AnchorY = c.AnchorY; live.AnchorZ = c.AnchorZ;
                live.MinX = c.MinX; live.MaxX = c.MaxX; live.MinZ = c.MinZ; live.MaxZ = c.MaxZ;
                live.Fields = c.Fields; live.UpdateBounds();
                live.Donations.Clear(); live.OwnSlots = 1; live.PaidSlots = 0; live.MissingSlots = 0; live.PenaltyLeft = 0;
                Slots.SettleOwner(list, owner);
                return true;
            });
            Plugin.Players.GetOrAdd(oldOwner).Notices.Add(
                L.T("takeover.notice", ("claim", oldName), ("player", ownerName), ("days", Plugin.Settings.ClaimExpiryDays.Value)));
            Plugin.Log.LogInfo($"{oldName} taken over by {ownerName} ({owner}).");
            return new Result { Success = true, TookOver = true, Claim = Plugin.Store.Index.FindById(c.Id), Message = L.T("takeover.done", ("claim", c.DisplayName)) };
        }

        // ------------------------------------------------------------------ growing

        public sealed class GrowPlan
        {
            public Claim Result;
            public double Steel;
            public string Error;
        }

        /// <summary>Admins: move sides of a claim without fields by <paramref name="meters"/> each (free), without changing anything yet.</summary>
        public static GrowPlan PlanGrow(Claim claim, IList<Side> sides, int meters, bool admin)
        {
            var plan = new GrowPlan();
            var c = claim.Clone();
            foreach (var side in sides)
            {
                float reach = c.Reach(side) + meters;
                string limit = ClaimGeometry.CheckReach(c, side, reach);
                if (limit != null)
                {
                    float m = limit == "reach.min" ? ClaimGeometry.MinReach : ClaimGeometry.MaxReach;
                    plan.Error = L.T("grow.side_limit", ("side", L.Side(side)),
                        ("limit", L.T(limit, ("m", $"{m:0}"), ("anchor", L.Anchor), ("max", L.MaxSize))));
                    return plan;
                }
                c.SetReach(side, reach);
            }
            if (meters > 0)
            {
                var problem = ClaimGeometry.Check(c, Plugin.Store.Index.Active, World.LandingZones());
                if (problem != null) { plan.Error = L.Problem(problem) + "."; return plan; }
                string foreignProblem = ForeignProblem(claim, c);
                if (foreignProblem != null) { plan.Error = foreignProblem; return plan; }
            }
            plan.Result = c;
            return plan;
        }

        public sealed class FieldPlan
        {
            public Claim Result;
            public bool Add;
            public int I, J;
            /// <summary>m² taken from the accounts of owner and donors.</summary>
            public float FromAccounts;
            public double Steel;
            public string Error;
        }

        /// <summary>
        /// '/claim grow' (add the next 10 x 10 m field in the direction the player looks) or, for admins,
        /// '/claim shrink' (take away the outermost field in that direction). Changes nothing yet.
        /// <paramref name="free"/>: an admin on someone else's claim (no cost, no slot limit).
        /// </summary>
        public static FieldPlan PlanField(Claim claim, Vector3 at, Side side, bool grow, bool free)
        {
            var plan = new FieldPlan { Add = grow };
            var c = claim.Clone();
            int i, j;
            if (grow)
            {
                if (claim.MissingSlots > 0 && !free) { plan.Error = L.T("field.frozen"); return plan; }
                ClaimGeometry.GrowField(c, at.x, at.z, side, out i, out j);
                c.AddField(i, j);
                {
                    // slots bought with a penalty do not count for growing (admins cannot exceed them either: no surprise penalty)
                    int need = c.SlotsNeeded;
                    int can = claim.OwnSlots + Math.Max(0, Slots.Free(claim.OwnerId)) + claim.Donated;
                    if (need > can) { plan.Error = L.T("field.no_slot", ("need", need), ("have", can)); return plan; }
                }
                var problem = ClaimGeometry.Check(c, Plugin.Store.Index.Active, World.LandingZones());
                if (problem != null) { plan.Error = L.Problem(problem) + "."; return plan; }
                string foreignProblem = ForeignProblem(claim, c);
                if (foreignProblem != null) { plan.Error = foreignProblem; return plan; }
                if (!free)
                {
                    float area = Claim.FieldSize * Claim.FieldSize;
                    plan.FromAccounts = Math.Min(area, Slots.PayersBalance(claim));
                    float rest = area - plan.FromAccounts;
                    if (rest > 0.01f)
                        plan.Steel = ClaimGeometry.SteelForArea(claim.Area + plan.FromAccounts, rest,
                            Plugin.Settings.SteelPerSquareMeter.Value, Plugin.Settings.PriceIncreasePer100SquareMeters.Value);
                }
            }
            else
            {
                if (!ClaimGeometry.ShrinkField(c, at.x, at.z, side, out i, out j)) { plan.Error = L.T("field.none"); return plan; }
                c.FieldAt(c.AnchorX, c.AnchorZ, out int ai, out int aj);
                if (i == ai && j == aj) { plan.Error = L.T("field.anchor", ("anchor", L.Anchor)); return plan; }
                c.RemoveField(i, j);
                if (!c.IsConnected()) { plan.Error = L.T("field.split"); return plan; }
            }
            plan.I = i;
            plan.J = j;
            plan.Result = c;
            return plan;
        }

        /// <summary>Carry out a field plan: pay (accounts, then steel), add or remove the field. Null = done, else why not.</summary>
        public static string ApplyField(Claim claim, FieldPlan plan)
        {
            if (plan.Steel > 0 && !TryPaySteel(claim, plan.Steel, out string why)) return why;
            if (plan.FromAccounts > 0f) Slots.TakeArea(claim, plan.FromAccounts);
            Plugin.Store.Batch(list =>
            {
                var live = list.Find(x => x.Id == claim.Id);
                if (live == null) return false;
                if (plan.Add) live.AddField(plan.I, plan.J);
                else live.RemoveField(plan.I, plan.J);
                Slots.SettleOwner(list, live.OwnerId);
                CutBackAbandoned(list, live);
                return true;
            });
            return null;
        }

        /// <summary>Steel from the claim's autolathe (penalty). Null = paid, else why not.</summary>
        public static string PaySteel(Claim claim, double amount) => TryPaySteel(claim, amount, out string why) ? null : why;

        /// <summary>
        /// A claim grows from <paramref name="before"/> to <paramref name="after"/>: buildings of other active players
        /// (not the owner's friends) stay out of it and of its 10 m zone – except ones that were that close already
        /// (they only have to stay outside). Null = fine, else why not.
        /// </summary>
        private static string ForeignProblem(Claim before, Claim after)
        {
            float zone = ClaimGeometry.Zone;
            foreach (var s in World.ForeignActiveStructures(after.MinX - zone, after.MaxX + zone, after.MinZ - zone, after.MaxZ + zone, id => IsOurs(before, id)))
            {
                var p = s.Position;
                bool wasClose = before.Near(p.x, p.z, zone);
                if (wasClose ? after.Contains(p.x, p.z) : after.Near(p.x, p.z, zone))
                    return L.T("foreign.zone", ("player", World.NameOf(s.OwnerClientId)), ("zone", $"{zone:0}"));
            }
            return null;
        }

        // ------------------------------------------------------------------ staked with flags (anchor "Flag")

        /// <summary>Flags placed by a player stake out a new claim: the rectangle around them (at least 10 x 10 m).</summary>
        public static Result CreateStaked(ulong owner, string ownerName, List<(float X, float Z)> flags, long newestFlagId, float y)
        {
            ClaimGeometry.StakeBounds(flags, out float minX, out float maxX, out float minZ, out float maxZ);
            if (maxX - minX > ClaimGeometry.MaxSpan + 0.01f || maxZ - minZ > ClaimGeometry.MaxSpan + 0.01f)
                return Result.Fail(L.T("stake.too_big", ("w", $"{maxX - minX:0}"), ("d", $"{maxZ - minZ:0}"), ("max", L.MaxSize)),
                    L.T("stake.too_big.hint"));
            var index = Plugin.Store.Index;
            var candidate = Claim.Create(owner, ownerName, (minX + maxX) / 2f, y, (minZ + maxZ) / 2f, newestFlagId, 0f);
            candidate.MinX = minX; candidate.MaxX = maxX; candidate.MinZ = minZ; candidate.MaxZ = maxZ;
            candidate.Staked = true;
            var problem = ClaimGeometry.Check(candidate, index.Active, World.LandingZones());
            if (problem != null) return Result.Fail(L.Problem(problem) + ".", L.MoveHint(problem));
            var foreign = World.ForeignActiveStructures(minX, maxX, minZ, maxZ, id => id == owner);
            if (foreign.Count > 0)
                return Result.Fail(L.T("stake.foreign", ("player", World.NameOf(foreign[0].OwnerClientId))));
            Plugin.Store.Batch(list =>
            {
                Slots.BeforeFounding(list, owner);
                CutBackAbandoned(list, candidate);
                list.Add(candidate);
                return true;
            });
            return new Result { Success = true, Claim = candidate, Message = L.T("stake.done", ("claim", candidate.DisplayName)) };
        }

        /// <summary>The owner or a friend put up a flag next to their staked claim: it now reaches that flag. Null = done.</summary>
        public static string ExtendToFlag(Claim claim, Vector3 flag)
        {
            var c = claim.Clone();
            c.MinX = Math.Min(c.MinX, flag.x); c.MaxX = Math.Max(c.MaxX, flag.x);
            c.MinZ = Math.Min(c.MinZ, flag.z); c.MaxZ = Math.Max(c.MaxZ, flag.z);
            if (c.Width > ClaimGeometry.MaxSpan + 0.01f || c.Depth > ClaimGeometry.MaxSpan + 0.01f)
                return L.T("reach.span", ("max", L.MaxSize));
            var problem = ClaimGeometry.Check(c, Plugin.Store.Index.Active, World.LandingZones());
            if (problem != null) return L.Problem(problem);
            string foreign = ForeignProblem(claim, c);
            if (foreign != null) return foreign;
            SetBounds(claim.Id, c);
            return null;
        }

        /// <summary>
        /// A flag of a staked claim is being taken down: the border follows the flags that are left.
        /// Fewer than <see cref="ClaimGeometry.FlagsNeeded"/> → the claim ends. Returns false if it ended.
        /// </summary>
        public static bool Restake(Claim claim, long goneFlagId)
        {
            var flags = new List<(float X, float Z)>();
            foreach (var f in UnityEngine.Object.FindObjectsOfType<Flag>())
            {
                if (f == null || f.BeingDestroyed || f.ReferenceId == goneFlagId) continue;
                var p = f.Position;
                if (claim.Near(p.x, p.z, 0.05f)) flags.Add((p.x, p.z));
            }
            if (flags.Count < ClaimGeometry.FlagsNeeded)
            {
                Markers.DestroyBeacon(claim.MarkerId);
                Plugin.Store.Remove(claim.Id);
                return false;
            }
            ClaimGeometry.StakeBounds(flags, out float minX, out float maxX, out float minZ, out float maxZ);
            Plugin.Store.Update(claim.Id, live =>
            {
                // only ever smaller: what the flags no longer reach is no longer claimed
                live.MinX = Math.Max(live.MinX, minX); live.MaxX = Math.Min(live.MaxX, maxX);
                live.MinZ = Math.Max(live.MinZ, minZ); live.MaxZ = Math.Min(live.MaxZ, maxZ);
                if (goneFlagId != 0 && live.AnchorId == goneFlagId) live.AnchorId = 0;
            });
            return true;
        }

        /// <summary>Carry out a plan: pay, change the claim, cut back abandoned neighbours. Null = done, else why not.</summary>
        public static string ApplyGrow(Claim claim, GrowPlan plan)
        {
            if (plan.Steel > 0 && !TryPaySteel(claim, plan.Steel, out string why)) return why;
            var target = plan.Result;
            Plugin.Store.Batch(list =>
            {
                var live = list.Find(x => x.Id == claim.Id);
                if (live == null) return false;
                live.MinX = target.MinX; live.MaxX = target.MaxX; live.MinZ = target.MinZ; live.MaxZ = target.MaxZ;
                CutBackAbandoned(list, live);
                return true;
            });
            return null;
        }

        /// <summary>Steel stored in the claim's autolathe, or -1 if there is none (flag anchor, missing).</summary>
        public static double SteelInAnchor(Claim claim)
        {
            var lathe = Thing.Find(claim.AnchorId) as Autolathe;
            if (lathe == null || lathe.ReagentMixture == null) return -1;
            return lathe.ReagentMixture.Steel.Quantity;
        }

        private static bool TryPaySteel(Claim claim, double amount, out string why)
        {
            why = null;
            var lathe = Thing.Find(claim.AnchorId) as Autolathe;
            if (lathe == null || lathe.ReagentMixture == null)
            {
                why = L.T("steel.no_lathe");
                return false;
            }
            double have = lathe.ReagentMixture.Steel.Quantity;
            if (have + 1e-6 < amount)
            {
                why = L.T("steel.not_enough", ("need", $"{amount:0}"), ("have", $"{have:0}"));
                return false;
            }
            lathe.ReagentMixture.Subtract(new Reagents.Steel(amount));
            Plugin.Log.LogInfo($"{claim.DisplayName}: paid {amount:0} steel for growing.");
            return true;
        }


        // ------------------------------------------------------------------ anchor removed

        /// <summary>
        /// The anchor of a claim is gone. If another anchor object stands in the claim it takes over (the claim
        /// stays exactly the same); otherwise the claim ends. Returns the new anchor, or null.
        /// </summary>
        public static Structure HandOver(Claim claim, long removedAnchorId)
        {
            var others = World.AnchorsIn(claim, removedAnchorId);
            if (others.Count == 0)
            {
                Plugin.Store.Remove(claim.Id);
                return null;
            }
            var next = others[0];
            var p = next.Position;
            Plugin.Store.Update(claim.Id, c =>
            {
                c.AnchorId = next.ReferenceId;
                c.AnchorX = p.x; c.AnchorY = p.y; c.AnchorZ = p.z;
            });
            return next;
        }
    }
}
