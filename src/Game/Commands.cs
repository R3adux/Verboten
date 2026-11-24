using System;
using System.Collections.Generic;
using System.Globalization;
using Assets.Scripts;
using Assets.Scripts.Objects.Entities;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// The 'claim' command. Runs on the server/host only; who the actor is was decided by the caller
    /// (host console/chat, or a verified remote player).
    /// </summary>
    public static class Commands
    {
        public const string Word = "claim";
        private static readonly string[] ChatPrefixes = { "/claim", "!claim" };

        public static bool IsChatCommand(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.TrimStart();
            foreach (var p in ChatPrefixes)
                if (text.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                    && (text.Length == p.Length || char.IsWhiteSpace(text[p.Length])))
                    return true;
            return false;
        }

        /// <summary>Splits "/claim add Bob guest" into ["add", "Bob", "guest"].</summary>
        public static string[] ArgsFromChat(string text)
        {
            var parts = text.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var args = new string[Math.Max(0, parts.Length - 1)];
            Array.Copy(parts, 1, args, 0, args.Length);
            return args;
        }

        public static void Execute(Actor actor, string[] args)
        {
            try
            {
                if (!GameManager.RunSimulation)
                {
                    actor.Reply(L.T("cmd.server_only"));
                    return;
                }
                string verb = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
                string rest = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : string.Empty;
                bool admin = Identity.IsAdmin(actor);
                if (verb != "pin" && !verb.StartsWith("help") && Pins.IsLocked(actor.SteamId))
                {
                    actor.Reply(L.T("cmd.locked"));
                    return;
                }
                switch (verb)
                {
                    case "help": Help(actor, admin, args.Length > 1 && int.TryParse(args[1], out int page) ? page : 1); break;
                    case "help2": Help(actor, admin, 2); break;
                    case "help3": Help(actor, admin, 3); break;
                    case "help4": Help(actor, admin, 4); break;
                    case "donate": Donate(actor, args); break;
                    case "pay": Pay(actor); break;
                    case "pin": Pins.Command(actor, rest, admin, text => ResolvePlayer(null, text, out ulong id, out _, actor.Reply) ? id : 0); break;
                    case "info": Info(actor, rest, admin); break;
                    case "check": Check(actor); break;
                    case "show": ShowBorders(actor); break;
                    case "find": Find(actor); break;
                    case "take": Take(actor); break;
                    case "add": Add(actor, args); break;
                    case "remove": RemoveMember(actor, rest); break;
                    case "name": Rename(actor, rest); break;
                    case "grow": Resize(actor, args, admin, grow: true); break;
                    case "shrink" when admin: Resize(actor, args, admin, grow: false); break;
                    case "owner": Transfer(actor, rest, admin); break;
                    case "delete": Delete(actor, rest, admin); break;
                    case "list" when admin: List(actor); break;
                    case "status" when admin: foreach (var line in Hooks.Report()) actor.Reply(line); break;
                    default: Help(actor, admin); break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error in claim command: {ex}");
                actor.Reply(L.T("cmd.error"));
            }
        }

        private static int ExpiryDays => Math.Max(1, Plugin.Settings.ClaimExpiryDays.Value);

        private static void Help(Actor a, bool admin, int page = 1)
        {
            // players get a window with everything (their own claim first); the console gets chat lines
            if (a.HasPosition && Identity.IsValidSteamId(a.SteamId))
            {
                var mine = Here(a) ?? OnlyOwned(a);
                if (mine != null && mine.RoleOf(a.SteamId) == Role.None && !admin) mine = OnlyOwned(a);
                Notify.Popup(a.SteamId, Texts.Help(mine, mine?.RoleOf(a.SteamId) ?? Role.None, admin, page, a.SteamId, a.Position.x, a.Position.z));
                return;
            }
            // console: the same text, line by line, without formatting
            foreach (var line in Texts.Plain(Texts.Help(null, Role.None, admin, page, 0, 0f, 0f)).Split('\n'))
                if (line.Trim().Length > 0) a.Reply(line);
        }

        // ---------------------------------------------------------------- looking

        private static void Info(Actor a, string target, bool admin)
        {
            Claim claim;
            if (target.Length > 0)
            {
                if (!admin) { a.Reply(L.T("info.need_inside")); return; }
                claim = FindForAdmin(a, target);
                if (claim == null) return;
            }
            else
            {
                claim = Here(a) ?? AbandonedHere(a) ?? OnlyOwned(a);
                if (claim == null) { a.Reply(L.T("info.not_inside")); return; }
            }
            foreach (var line in Describe(claim)) a.Reply(line);
            if (!a.IsConsole) a.Reply(L.T("info.you", ("role", L.Role(claim.RoleOf(a.SteamId)))));
            if (claim.IsActive) a.Reply(L.T("info.expiry", ("days", ExpiryDays)));
            if (claim.IsActive && ClaimActions.Growing && !claim.Staked && (admin || claim.OwnerId == a.SteamId))
            {
                a.Reply(L.T("info.account", ("area", $"{Slots.Balance(claim.OwnerId):0}")));
                a.Reply(L.T("info.slots", ("need", claim.SlotsNeeded), ("own", claim.OwnSlots), ("donated", claim.Donated), ("free", Math.Max(0, Slots.Free(claim.OwnerId))), ("max", Slots.Max)));
                double price = Slots.PricePerSquareMeter(claim);
                double steel = ClaimActions.SteelInAnchor(claim);
                a.Reply(L.T("info.price", ("price", $"{price:0.##}")) + (steel >= 0 ? L.T("info.next_meter_have", ("have", $"{steel:0}")) : ""));
            }
        }

        private static void Check(Actor a)
        {
            if (!a.HasPosition) { a.Reply(L.T("need_character")); return; }
            var result = ClaimActions.Evaluate(a.SteamId, a.Name, a.Position, 0);
            if (result.Success && result.Claim != null && !result.Claim.IsActive)
            {
                a.Reply(result.Claim.AnchorId != 0
                    ? L.T("check.abandoned_take", ("message", result.Message))
                    : L.T("check.abandoned_build", ("message", result.Message), ("anchor", L.Anchor)));
                return;
            }
            if (!result.Success) { a.Reply(L.T("check.no", ("reason", result.Message + (result.Hint != null ? " " + result.Hint : "")))); return; }
            if (Staking.Enabled) { a.Reply(L.T("check.flags", ("flags", ClaimGeometry.FlagsNeeded))); return; }
            if (ClaimActions.Growing) { a.Reply(L.T("check.ok", ("anchor", L.Anchor))); return; }
            a.Reply(L.T("check.ok_size", ("anchor", L.Anchor), ("w", $"{result.Claim.Width:0}"), ("d", $"{result.Claim.Depth:0}")));
            if (result.Note != null) a.Reply(result.Note);
        }

        private static void ShowBorders(Actor a)
        {
            if (!a.HasPosition) { a.Reply(L.T("need_character_borders")); return; }
            int shown = BorderDisplay.Start(a);
            a.Reply(shown == 0
                ? L.T("show.none", ("range", $"{BorderDisplay.Range:0}"))
                : L.T("show.some", ("count", shown)));
        }

        private static void Find(Actor a)
        {
            if (!a.HasPosition) { a.Reply(L.T("need_character")); return; }
            var p = a.Position;
            Claim best = null;
            float bestDistance = float.MaxValue;
            foreach (var c in Plugin.Store.Index.Abandoned)
            {
                float d = c.DistanceTo(p.x, p.z);
                if (d < bestDistance) { best = c; bestDistance = d; }
            }
            if (best == null) { a.Reply(L.T("find.none")); return; }
            if (bestDistance <= 0f) { a.Reply(L.T("find.inside", ("owner", best.OwnerName))); return; }
            float cx = (best.MinX + best.MaxX) / 2f, cz = (best.MinZ + best.MaxZ) / 2f;
            a.Reply(L.T("find.nearest", ("claim", best.DisplayName), ("m", $"{bestDistance:0}"), ("dir", L.Direction(cx - p.x, cz - p.z))));
        }

        // ---------------------------------------------------------------- owning

        private static void Take(Actor a)
        {
            var abandoned = AbandonedHere(a);
            if (abandoned == null) { a.Reply(L.T("take.not_inside")); return; }
            if (abandoned.AnchorId == 0 || Assets.Scripts.Objects.Thing.Find(abandoned.AnchorId) == null)
            {
                a.Reply(L.T("take.no_anchor", ("anchor", L.Anchor)));
                return;
            }
            var result = ClaimActions.TakeOver(abandoned, a.SteamId, a.Name, 0, Vector3.zero);
            if (!result.Success) { a.Reply(result.Message); return; }
            Notify.Popup(a.SteamId, Texts.TakenOver(result.Claim));
            BorderDisplay.ShowNewClaim(a.SteamId, result.Claim);
            a.Reply(result.Message);
        }

        private static void Add(Actor a, string[] args)
        {
            if (args.Length < 2) { a.Reply(L.T("add.usage")); return; }
            var role = Role.Friend;
            int end = args.Length;
            if (args.Length >= 3 && Roles.TryParse(args[args.Length - 1], out var parsed)) { role = parsed; end--; }
            string playerText = string.Join(" ", args, 1, end - 1);

            var claim = Manageable(a);
            if (claim == null) return;
            if (!ResolvePlayer(claim, playerText, out ulong id, out string name, a.Reply)) return;
            if (id == claim.OwnerId) { a.Reply(L.T("add.is_owner", ("player", name))); return; }

            Plugin.Store.Update(claim.Id, c =>
            {
                var m = c.FindMember(id);
                if (m == null) c.Members.Add(new ClaimMember { SteamId = id, Name = name, Role = role });
                else { m.Role = role; m.Name = name; }
            });
            if (role != Role.Friend && claim.DonatedBy(id) > 0)
                Plugin.Store.Batch(list =>
                {
                    var live = list.Find(x => x.Id == claim.Id);
                    if (live == null) return false;
                    Slots.DropDonation(list, live, id); // only friends donate
                    return true;
                });
            a.Reply(L.T("add.done", ("player", name), ("role", L.Role(role)), ("claim", claim.DisplayName)));
            if (role == Role.Friend && !ClaimActions.Growing && ClaimActions.RefitBase(claim.Id))
            {
                var now = Plugin.Store.Index.FindById(claim.Id);
                a.Reply(L.T("add.refit", ("player", name), ("w", $"{now.Width:0}"), ("d", $"{now.Depth:0}")));
            }
        }

        private static void RemoveMember(Actor a, string playerText)
        {
            if (playerText.Length == 0) { a.Reply(L.T("remove.usage")); return; }
            var claim = Manageable(a);
            if (claim == null) return;
            if (!ResolvePlayer(claim, playerText, out ulong id, out string name, a.Reply)) return;
            if (claim.FindMember(id) == null) { a.Reply(L.T("remove.not_member", ("player", name), ("claim", claim.DisplayName))); return; }
            Plugin.Store.Batch(list =>
            {
                var live = list.Find(x => x.Id == claim.Id);
                if (live == null) return false;
                live.Members.RemoveAll(m => m.SteamId == id);
                Slots.DropDonation(list, live, id); // their slots go back to them
                return true;
            });

            a.Reply(L.T("remove.done", ("player", name), ("claim", claim.DisplayName)));
        }

        private static void Rename(Actor a, string newName)
        {
            var claim = Manageable(a);
            if (claim == null) return;
            string clean = Claim.CleanName(newName);
            if (clean == null) { a.Reply(L.T("name.usage", ("max", Claim.MaxNameLength))); return; }
            var other = Plugin.Store.Index.FindByName(clean);
            if (other != null && other.Id != claim.Id) { a.Reply(L.T("name.taken", ("name", clean))); return; }
            Plugin.Store.Update(claim.Id, c => c.Name = clean);
            a.Reply(L.T("name.done", ("name", clean)));
        }

        private sealed class PendingGrow
        {
            public string ClaimId;
            public string Key;
            public float Until;
        }

        private static readonly Dictionary<ulong, PendingGrow> Pending = new Dictionary<ulong, PendingGrow>();

        /// <summary>
        /// /claim grow &lt;m&gt; (owner) and /claim shrink &lt;m&gt; (admins, other claims): the side the player LOOKS at
        /// moves – the only way, no compass words (players do not need to know where north is).
        /// </summary>
        private static void Resize(Actor a, string[] args, bool admin, bool grow)
        {
            string verb = grow ? "grow" : "shrink";
            if (!a.HasPosition) { a.Reply(L.T("need_character")); return; }
            // where the player LOOKS (the body of the host's own character does not turn with the view)
            var look = a.Human.EntityForward;
            var side = ClaimGeometry.Facing(look.x, look.z);
            var claim = Here(a) ?? OnlyOwned(a);
            // admins resize other claims for free (tidying up); on their own claim they are owners like everyone else
            bool free = admin && claim != null && claim.RoleOf(a.SteamId) == Role.None;
            if (Staking.Enabled && !free) { a.Reply(Texts.FlagsResize); return; }
            if (grow && !free && !ClaimActions.Growing) { a.Reply(L.T("resize.fit", ("max", L.MaxSize))); return; }
            if (!grow && !free) { a.Reply(L.T("resize.shrink_admin")); return; }
            if (claim == null) { a.Reply(L.T("resize.need_inside")); return; }
            if (claim.Staked) { a.Reply(Texts.FlagsResize); return; }
            if (!free && claim.OwnerId != a.SteamId) { a.Reply(L.T("resize.only_owner", ("owner", claim.OwnerName))); return; }
            if (!claim.HasFields && Slots.FieldMode && !claim.Staked)
            {
                // a claim from before fields (or growing was switched on later): it becomes a field claim
                string id = claim.Id;
                Plugin.Store.Batch(list =>
                {
                    var live = list.Find(x => x.Id == id);
                    if (live == null) return false;
                    live.ConvertToFields();
                    Slots.SettleOwner(list, live.OwnerId);
                    return true;
                });
                claim = Plugin.Store.Index.FindById(id);
                if (claim == null) return;
            }
            // the next field is found from where the player stands: they must be on the claim or right next to it
            if (claim.HasFields && !claim.Near(a.Position.x, a.Position.z, ClaimGeometry.Zone)) { a.Reply(L.T("resize.need_inside")); return; }
            if (claim.HasFields) { ResizeField(a, claim, side, grow, free); return; }

            // claims without fields (flags, follows the base): admins move a side by meters
            if (args.Length != 2 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int meters)
                || meters < 1 || meters > 45)
            {
                a.Reply(L.T("resize.usage", ("verb", verb)));
                return;
            }
            var plan = ClaimActions.PlanGrow(claim, new List<Side> { side }, grow ? meters : -meters, free);
            if (plan.Error != null) { a.Reply(L.T("resize.not_possible", ("error", plan.Error))); return; }
            string error = ClaimActions.ApplyGrow(claim, plan);
            if (error != null) { a.Reply(error); return; }
            var updated = Plugin.Store.Index.FindById(claim.Id);
            a.Reply(L.T(grow ? "resize.done" : "resize.done_shrink", ("side", L.T("dir." + side.ToString().ToLowerInvariant())), ("m", meters),
                ("claim", updated.DisplayName), ("w", $"{updated.Width:0}"), ("d", $"{updated.Depth:0}")));
            BorderDisplay.ShowOwn(a, updated);
        }

        /// <summary>'/claim grow': the next 10 x 10 m field in the direction the player looks; '/claim shrink' (admins): the outermost one.</summary>
        private static void ResizeField(Actor a, Claim claim, Side side, bool grow, bool free)
        {
            var plan = ClaimActions.PlanField(claim, a.Position, side, grow, free);
            if (plan.Error != null) { a.Reply(L.T("resize.not_possible", ("error", plan.Error))); return; }
            if (plan.Steel > 0)
            {
                double have = ClaimActions.SteelInAnchor(claim);
                if (have < 0) { a.Reply(L.T("resize.no_lathe")); return; }
                string key = $"field {plan.I} {plan.J}";
                if (!Confirmed(a, claim, key))
                {
                    string extra = plan.FromAccounts > 0 ? L.T("resize.cost_extra", ("area", $"{plan.FromAccounts:0}")) : "";
                    a.Reply(L.T("field.cost", ("side", L.T("dir." + side.ToString().ToLowerInvariant())),
                        ("steel", $"{plan.Steel:0}"), ("have", $"{have:0}"), ("extra", extra)));
                    return;
                }
            }
            string error = ClaimActions.ApplyField(claim, plan);
            if (error != null) { a.Reply(error); return; }
            var updated = Plugin.Store.Index.FindById(claim.Id);
            a.Reply(L.T(grow ? "field.done" : "field.done_shrink", ("side", L.T("dir." + side.ToString().ToLowerInvariant())),
                ("claim", updated.DisplayName), ("fields", updated.Fields.Count), ("area", $"{updated.Area:0}"),
                ("w", $"{updated.Width:0}"), ("d", $"{updated.Depth:0}")) +
                (free ? "" : L.T("field.left", ("area", $"{Slots.Balance(a.SteamId):0}"))));
            BorderDisplay.ShowOwn(a, updated);
        }

        /// <summary>Costs steel: the first time only the price is shown; the same command within 30 s confirms it.</summary>
        private static bool Confirmed(Actor a, Claim claim, string key)
        {
            float now = Time.unscaledTime;
            if (Pending.TryGetValue(a.SteamId, out var p) && p.ClaimId == claim.Id && p.Key == key && now <= p.Until)
            {
                Pending.Remove(a.SteamId);
                return true;
            }
            Pending[a.SteamId] = new PendingGrow { ClaimId = claim.Id, Key = key, Until = now + 30f };
            return false;
        }

        // ---------------------------------------------------------------- slots

        /// <summary>'/claim donate &lt;n&gt;': a friend gives n of their claim slots to the claim they stand in (0 = take them back).</summary>
        private static void Donate(Actor a, string[] args)
        {
            if (!Slots.FieldMode) { a.Reply(L.T("donate.off")); return; }
            if (args.Length != 2 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n > Slots.Max)
            {
                a.Reply(L.T("donate.usage", ("max", Slots.Max)));
                return;
            }
            var claim = Here(a);
            if (claim == null) { a.Reply(L.T("donate.need_inside")); return; }
            if (claim.OwnerId == a.SteamId) { a.Reply(L.T("donate.own")); return; }
            if (claim.RoleOf(a.SteamId) != Role.Friend) { a.Reply(L.T("donate.only_friends", ("owner", claim.OwnerName))); return; }
            int now = claim.DonatedBy(a.SteamId);
            int free = Slots.Free(a.SteamId);
            if (n - now > free) { a.Reply(L.T("donate.not_enough", ("free", Math.Max(0, free)), ("max", Slots.Max))); return; }
            Plugin.Store.Batch(list =>
            {
                var live = list.Find(x => x.Id == claim.Id);
                if (live == null) return false;
                live.Donations.RemoveAll(d => d.SteamId == a.SteamId);
                if (n > 0) live.Donations.Add(new Donation { SteamId = a.SteamId, Name = a.Name, Slots = n });
                Slots.SettleOwner(list, live.OwnerId);
                Slots.SettleOwner(list, a.SteamId); // slots they take back may settle a claim of their own
                return true;
            });
            a.Reply(n > 0
                ? L.T("donate.done", ("n", n), ("claim", claim.DisplayName), ("free", Math.Max(0, Slots.Free(a.SteamId))))
                : L.T("donate.back", ("claim", claim.DisplayName), ("free", Math.Max(0, Slots.Free(a.SteamId)))));
            if (n != now)
                Notify.Chat(claim.OwnerId, n > 0
                    ? L.T("donate.notify", ("player", a.Name), ("n", n), ("claim", claim.DisplayName))
                    : L.T("donate.notify_back", ("player", a.Name), ("claim", claim.DisplayName)));
        }

        /// <summary>'/claim pay': pay the penalty of the claim that is short of slots.</summary>
        private static void Pay(Actor a)
        {
            Claim claim = null;
            var here = Here(a);
            if (here != null && here.OwnerId == a.SteamId && here.MissingSlots > 0) claim = here;
            else foreach (var c in Plugin.Store.Index.OwnedBy(a.SteamId)) if (c.MissingSlots > 0) { claim = c; break; }
            if (claim == null) { a.Reply(L.T("pay.nothing")); return; }
            double steel = Slots.PenaltySteel(claim, out float built);
            if (built <= 0f)
            {
                // nothing built would be lost: no penalty to pay, the claim simply shrinks (as it would anyway)
                Slots.Shrink(claim, nothingLost: true);
                return;
            }
            double have = ClaimActions.SteelInAnchor(claim);
            if (have < 0) { a.Reply(L.T("steel.no_lathe")); return; }
            if (!Confirmed(a, claim, "pay"))
            {
                a.Reply(L.T("pay.cost", ("claim", claim.DisplayName), ("steel", $"{steel:0}"), ("have", $"{have:0}"), ("built", $"{built:0}")));
                return;
            }
            string error = ClaimActions.PaySteel(claim, steel);
            if (error != null) { a.Reply(error); return; }
            Slots.Paid(claim.Id);
            a.Reply(L.T("pay.done", ("claim", claim.DisplayName)));
        }

        private static void Transfer(Actor a, string playerText, bool admin)
        {
            if (playerText.Length == 0) { a.Reply(L.T("owner.usage")); return; }
            var claim = Here(a) ?? OnlyOwned(a);
            if (claim == null) { a.Reply(L.T("owner.need_inside")); return; }
            if (!admin && claim.OwnerId != a.SteamId) { a.Reply(L.T("owner.only_owner", ("owner", claim.OwnerName))); return; }
            if (!ResolvePlayer(claim, playerText, out ulong id, out string name, a.Reply)) return;
            if (id == claim.OwnerId) { a.Reply(L.T("owner.already", ("player", name))); return; }
            if (Slots.Free(id) + claim.DonatedBy(id) < Math.Max(1, claim.OwnSlots))
            {
                a.Reply(L.T("owner.limit", ("player", name)));
                return;
            }
            ulong oldOwner = claim.OwnerId;
            string oldName = claim.OwnerName;
            Plugin.Store.Batch(list =>
            {
                var c = list.Find(x => x.Id == claim.Id);
                if (c == null) return false;
                c.Members.RemoveAll(m => m.SteamId == id);
                c.Donations.RemoveAll(d => d.SteamId == id);
                c.Members.Add(new ClaimMember { SteamId = oldOwner, Name = oldName, Role = Role.Friend });
                c.OwnerId = id;
                c.OwnerName = name;
                Slots.SettleOwner(list, id);
                Slots.SettleOwner(list, oldOwner);
                return true;
            });
            a.Reply(L.T("owner.done", ("claim", claim.DisplayName), ("player", name), ("old", oldName)));
            Notify.Chat(id, L.T("owner.notify", ("old", oldName), ("claim", claim.DisplayName)));
        }

        private static void Delete(Actor a, string target, bool admin)
        {
            Claim claim;
            if (target.Length > 0)
            {
                if (!admin) { a.Reply(L.T("delete.admin_only")); return; }
                claim = FindForAdmin(a, target);
                if (claim == null) return;
            }
            else
            {
                claim = Here(a) ?? AbandonedHere(a) ?? OnlyOwned(a);
                if (claim == null) { a.Reply(L.T("delete.need_inside")); return; }
                if (!admin && claim.OwnerId != a.SteamId) { a.Reply(L.T("delete.only_owner", ("owner", claim.OwnerName))); return; }
            }
            Markers.DestroyBeacon(claim.MarkerId);
            Plugin.Store.Remove(claim.Id);
            a.Reply(L.T("delete.done", ("claim", claim.DisplayName)));
        }

        private static void List(Actor a)
        {
            var claims = Plugin.Store.Index.Claims;
            if (claims.Count == 0) { a.Reply(L.T("list.none")); return; }
            a.Reply(L.T("list.count", ("count", claims.Count)));
            foreach (var c in claims)
                foreach (var line in Describe(c)) a.Reply(line);
        }

        // ---------------------------------------------------------------- helpers

        private static IEnumerable<string> Describe(Claim c)
        {
            string state = c.IsActive ? "" : L.T("describe.abandoned");
            string line = L.T("describe.line", ("claim", c.DisplayName), ("state", state), ("owner", c.OwnerName),
                ("w", $"{c.Width:0}"), ("d", $"{c.Depth:0}"));
            if (!c.Staked)
                line += L.T("describe.reach", ("n", $"{c.Reach(Side.North):0}"), ("e", $"{c.Reach(Side.East):0}"),
                    ("s", $"{c.Reach(Side.South):0}"), ("west", $"{c.Reach(Side.West):0}"), ("anchor", L.Anchor));
            yield return line;
            var friends = new List<string>();
            var guests = new List<string>();
            foreach (var m in c.Members)
                (m.Role == Role.Guest ? guests : friends).Add(m.Name);
            yield return L.T("describe.friends", ("list", friends.Count > 0 ? string.Join(", ", friends) : "-"));
            yield return L.T("describe.guests", ("list", guests.Count > 0 ? string.Join(", ", guests) : "-"));
        }

        private static Claim Here(Actor a)
        {
            if (!a.HasPosition) return null;
            var p = a.Position;
            return Plugin.Store.Index.At(p.x, p.z);
        }

        private static Claim AbandonedHere(Actor a)
        {
            if (!a.HasPosition) return null;
            var p = a.Position;
            return Plugin.Store.Index.AbandonedAt(p.x, p.z);
        }

        private static Claim OnlyOwned(Actor a)
        {
            var owned = Plugin.Store.Index.OwnedBy(a.SteamId);
            return owned.Count == 1 ? owned[0] : null;
        }

        /// <summary>The claim the actor manages: the one they stand in, or their only claim.</summary>
        private static Claim Manageable(Actor a)
        {
            var claim = Here(a) ?? OnlyOwned(a);
            if (claim == null)
            {
                a.Reply(L.T("manage.need_inside"));
                return null;
            }
            if (Identity.IsAdmin(a) || (claim.PermissionsOf(a.SteamId) & Permission.Manage) != 0) return claim;
            a.Reply(L.T("manage.only_owner", ("owner", claim.OwnerName)));
            return null;
        }

        /// <summary>Admin lookup by claim name or owner name/SteamID.</summary>
        private static Claim FindForAdmin(Actor a, string text)
        {
            var index = Plugin.Store.Index;
            var byName = index.FindByName(text);
            if (byName != null) return byName;
            var found = new List<Claim>();
            ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id);
            foreach (var c in index.Claims)
                if (c.OwnerId == id || string.Equals(c.OwnerName, text, StringComparison.OrdinalIgnoreCase)) found.Add(c);
            if (found.Count == 1) return found[0];
            if (found.Count > 1) { a.Reply(L.T("admin.many", ("text", text), ("count", found.Count))); return null; }
            a.Reply(L.T("admin.none", ("text", text)));
            return null;
        }

        /// <summary>
        /// Exact name (case-insensitive) of a player in the world, a member, or anyone who was ever on this server; or a SteamID64.
        /// No partial matches: a typo must never give rights to the wrong person.
        /// </summary>
        private static bool ResolvePlayer(Claim claim, string text, out ulong id, out string name, Action<string> reply)
        {
            id = 0;
            name = text;
            if (string.IsNullOrWhiteSpace(text)) { reply(L.T("resolve.empty")); return false; }

            var matches = new Dictionary<ulong, string>();
            foreach (var h in Human.AllHumans)
            {
                if (h == null) continue;
                ulong hid = Identity.SteamIdOf(h);
                if (Identity.IsValidSteamId(hid) && string.Equals(h.DisplayName, text, StringComparison.OrdinalIgnoreCase))
                    matches[hid] = h.DisplayName;
            }
            if (claim != null)
                foreach (var m in claim.Members)
                    if (string.Equals(m.Name, text, StringComparison.OrdinalIgnoreCase) && !matches.ContainsKey(m.SteamId))
                        matches[m.SteamId] = m.Name;
            // everyone who was ever on this server (they need not be online)
            foreach (var known in Plugin.Players.FindByName(text))
                if (Identity.IsValidSteamId(known.SteamId) && !matches.ContainsKey(known.SteamId))
                    matches[known.SteamId] = known.Name;

            if (matches.Count == 1)
            {
                foreach (var kv in matches) { id = kv.Key; name = kv.Value; }
                return true;
            }
            if (matches.Count > 1)
            {
                reply(L.T("resolve.several", ("text", text)));
                return false;
            }
            if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && Identity.IsValidSteamId(id))
            {
                name = claim?.FindMember(id)?.Name ?? World.NameOf(id);
                return true;
            }
            reply(L.T("resolve.none", ("text", text)));
            return false;
        }
    }
}
