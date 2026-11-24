using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Networking;
using Assets.Scripts.Networks;
using Networks;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using HarmonyLib;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// The protection itself: a firewall for everything other players send to the server.
    ///
    /// Every action of a remote player arrives as a network message. Before the game processes one, Verboten
    /// collects what it refers to – things, rooms (atmospheres), pipe/cable networks, positions, grid cells –
    /// and checks them against the claims, with the VERIFIED sender as the actor (never ids from the message).
    /// Rules per message type: <see cref="MessageRules"/>; unknown types need Build. Also: cheat messages
    /// (kick / block), building in approach zones, dropping near claims, attacks on people in claims, corpses.
    /// Not checked: the host's own actions and verified admins.
    /// </summary>
    public static class Protection
    {
        private const string Target = "IMessageProcessable.Process";
        private const int MaxNetworkMembers = 5000;

        /// <summary>Sender of messages the game postponed (DeferredMessageQueue), by message object.</summary>
        private static readonly Dictionary<object, SenderContext.RemoteIdentity> DeferredSenders =
            new Dictionary<object, SenderContext.RemoteIdentity>();

        public static void Register()
        {
            Hooks.AddMany(Hooks.Kind.Protection, "Protect claims from other players' actions", Target,
                FindMessageProcessors, typeof(Protection), nameof(Gate),
                "InteractionMessage.Process", "AttackWithMessage.Process", "CreateStructureMessage.Process",
                "ConstructionCreationMessage.Process", "MoveToSlotMessage.Process", "SwapSlotsMessage.Process",
                "MineAsteroidMessage.Process", "DestroyThingRequest.Process", "AddGasCommand.Process");
            Hooks.AddMany(Hooks.Kind.Feature, "Remember who sent postponed messages", "DeferredMessageQueue.DeferUntilExists",
                () => FindDeferMethods(), typeof(Protection), nameof(DeferPrefix),
                "DeferredMessageQueue.DeferUntilExists");
        }

        /// <summary>Process(long) of every network message type in the game (chat has its own hook).</summary>
        private static IEnumerable<MethodBase> FindMessageProcessors()
        {
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Thing).Assembly))
            {
                if (type == null || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters) continue;
                if (!typeof(IMessageProcessable).IsAssignableFrom(type) || type == typeof(ChatMessage)) continue;
                var process = type.GetMethod("Process", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(long) }, null);
                if (process != null && !process.IsAbstract && process.GetMethodBody() != null) yield return process;
            }
        }

        private static IEnumerable<MethodBase> FindDeferMethods()
        {
            foreach (var m in typeof(DeferredMessageQueue).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.Name == "DeferUntilExists") yield return m;
        }

        private static void DeferPrefix(object __0)
        {
            if (__0 != null && SenderContext.CurrentOrigin == SenderContext.Origin.Network && SenderContext.TryGetSender(out var sender))
                DeferredSenders[__0] = sender;
        }

        // ------------------------------------------------------------------ the gate

        private static bool Gate(object __instance)
        {
            if (!NetworkManager.IsServer) return true;
            var origin = SenderContext.CurrentOrigin;
            if (origin == SenderContext.Origin.Local) return true; // host or the game itself
            try
            {
                var info = MessageInfo.For(__instance.GetType());
                if (info.Rule.Check == MessageRules.Check.Skip) return true;

                SenderContext.RemoteIdentity sender = null;
                if (origin == SenderContext.Origin.Network) SenderContext.TryGetSender(out sender);
                else if (origin == SenderContext.Origin.Deferred) DeferredSenders.TryGetValue(__instance, out sender);

                ulong actor = sender?.SteamId ?? 0;
                Client client = sender != null ? Client.Find(sender.Connection) : null;
                if (sender != null)
                {
                    info.CorrectCreator(__instance, actor); // a client may not claim to be someone else
                    if (sender.Verified && Identity.IsAdminId(actor)) return true;
                }

                // cheats come first: they matter even where nothing is claimed
                if (info.Rule.Cheat == MessageRules.Cheat.Never)
                {
                    if (origin == SenderContext.Origin.Network) KickCheater(client, actor, info.Name);
                    return false;
                }
                if ((info.Rule.Cheat == MessageRules.Cheat.VanillaCheat || info.IsAuthoring(__instance)) && !World.IsCreative)
                {
                    Notify.Chat(actor, Texts.CheatsDisabled);
                    Plugin.Log.LogInfo($"Blocked cheat {info.Name} from {client?.name ?? "unknown"} ({actor}).");
                    return false;
                }

                if (info.Rule.SelfOnly)
                {
                    // unknown sender (sender tracking broken by a game update): behave like the game, or nobody could respawn
                    if (sender == null || info.IsAboutSelf(__instance, actor)) return true;
                    SelfOnlyDenied(client, actor, info);
                    return false;
                }

                if (info.ReferencesFlare(__instance)) return false; // corner flares of new claims: look, do not touch

                if (info.ReferencesMarker(__instance))
                {
                    Notify.Chat(actor, L.T("marker.locked"));
                    return false;
                }

                if (!Plugin.Settings.BuildingInLandingZone.Value && info.TouchesLandingZone(__instance, World.LandingZones()))
                {
                    LandingZoneDenied(client, actor, info);
                    return false;
                }

                var index = Plugin.Store.Index;
                if (index.Count == 0) return true;

                var actorHuman = actor != 0 ? SenderContext.CharacterOf(actor) : null;
                // joined from a new address and has not typed the PIN yet: no rights in any claim for now
                ulong rightsOf = Pins.IsLocked(actor) ? 0 : actor;
                var blocking = info.FindBlocking(__instance, index, rightsOf, actorHuman);
                if (blocking == null) return true;

                if (sender == null && origin == SenderContext.Origin.Deferred)
                {
                    Plugin.Log.LogInfo($"Blocked postponed {info.Name} in {blocking.DisplayName}: sender unknown.");
                    return false;
                }
                Denied(client, actor, actorHuman, blocking, info);
                return false;
            }
            catch (Exception ex)
            {
                // Fail closed only where it matters: if anything goes wrong while claims exist, block.
                Plugin.Log.LogError($"Protection check failed for {__instance?.GetType().Name} (blocked): {ex}");
                return Plugin.Store.Index.Count == 0;
            }
        }

        // ------------------------------------------------------------------ feedback

        private static readonly Dictionary<ulong, float> LastNotice = new Dictionary<ulong, float>();

        private static void Denied(Client client, ulong actor, Human actorHuman, Claim claim, MessageInfo info)
        {
            float now = Time.unscaledTime;
            if (LastNotice.TryGetValue(actor, out float last) && now - last < 3f) return;
            LastNotice[actor] = now;

            string what = What(info.Rule);
            Plugin.Log.LogInfo($"Blocked {info.Name} from {client?.name ?? "unknown"} ({actor}) at {claim.DisplayName}.");
            RemoteChat.SendPrivate(client, L.T("denied." + what, ("claim", claim.DisplayName)));
            Incidents.Add(claim, L.T("incident." + what, ("player", actorHuman?.DisplayName ?? client?.name ?? "?")));
        }

        private static readonly Dictionary<ulong, float> LastSelfOnlyLog = new Dictionary<ulong, float>();

        private static void SelfOnlyDenied(Client client, ulong actor, MessageInfo info)
        {
            float now = Time.unscaledTime;
            if (LastSelfOnlyLog.TryGetValue(actor, out float last) && now - last < 10f) return;
            LastSelfOnlyLog[actor] = now;
            Plugin.Log.LogWarning($"Blocked {info.Name} from {client?.name ?? "unknown"} ({actor}): it was about another player " +
                                  "(only a manipulated client sends that).");
        }

        private static void LandingZoneDenied(Client client, ulong actor, MessageInfo info)
        {
            float now = Time.unscaledTime;
            if (LastNotice.TryGetValue(actor, out float last) && now - last < 3f) return;
            LastNotice[actor] = now;
            Plugin.Log.LogInfo($"Blocked {info.Name} from {client?.name ?? "unknown"} ({actor}) at the landing zone.");
            RemoteChat.SendPrivate(client, L.T("landing.denied"));
        }

        /// <summary>What was refused, as the end of a text key: denied.build, incident.build, …</summary>
        private static string What(MessageRules.Rule rule)
        {
            if (rule.ProtectPeople) return "attack";
            if (rule.ActorZone) return "drop";
            var p = rule.Needs;
            if ((p & Permission.Build) != 0) return "build";
            if ((p & Permission.Containers) != 0) return "take";
            if ((p & Permission.Mining) != 0) return "dig";
            if ((p & Permission.Use) != 0) return "use";
            return "other";
        }

        private static void KickCheater(Client client, ulong actor, string message)
        {
            string name = client?.name ?? actor.ToString();
            string text = $"Verboten: {name} ({actor}) sent '{message}', which the game never sends – a manipulated client. Disconnected.";
            Plugin.Log.LogWarning(text);
            Identity.PrintLocal(text);
            foreach (var other in NetworkBase.Clients)
                if (other != null && !other.IsHost && SenderContext.IsVerifiedConnection(other.connectionId, other.ClientId) && Identity.IsAdminId(other.ClientId))
                    RemoteChat.SendPrivate(other, text);
            if (client == null) return;
            RemoteChat.SendPrivate(client, L.T("cheat.kicked"));
            SenderContext.Later(0.5f, () =>
            {
                try { client.Disconnect(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"Could not disconnect {name}: {ex.Message}"); }
            });
        }

        public static void Reset()
        {
            DeferredSenders.Clear();
            LastSelfOnlyLog.Clear();
        }

        // ------------------------------------------------------------------ per message type

        /// <summary>What a message type refers to, found once by reflection and cached.</summary>
        private sealed class MessageInfo
        {
            private static readonly Dictionary<Type, MessageInfo> Cache = new Dictionary<Type, MessageInfo>();

            public string Name;
            public MessageRules.Rule Rule;
            private readonly List<Func<object, long>> _ids = new List<Func<object, long>>();
            private readonly List<Func<object, Vector3>> _positions = new List<Func<object, Vector3>>();
            private readonly List<Func<object, Grid3>> _cells = new List<Func<object, Grid3>>();
            private readonly List<Action<object, ulong>> _creators = new List<Action<object, ulong>>();
            private Func<object, bool> _authoring;
            // SelfOnly messages: every id in them (also private ones) must be the sender's own character / SteamID
            private readonly List<Func<object, long>> _selfThings = new List<Func<object, long>>();
            private readonly List<Func<object, ulong>> _selfSteamIds = new List<Func<object, ulong>>();

            public static MessageInfo For(Type type)
            {
                if (Cache.TryGetValue(type, out var info)) return info;
                info = new MessageInfo { Name = type.Name, Rule = MessageRules.For(type.Name) };
                if (info.Rule.Check != MessageRules.Check.Skip) info.Inspect(type);
                if (info.Rule.SelfOnly) info.InspectSelf(type);
                Cache[type] = info;
                return info;
            }

            private void InspectSelf(Type type)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var f in type.GetFields(flags))
                {
                    if (f.FieldType == typeof(long)) _selfThings.Add(o => (long)f.GetValue(o));
                    else if (f.FieldType == typeof(ulong)) _selfSteamIds.Add(o => (ulong)f.GetValue(o));
                }
                foreach (var p in type.GetProperties(flags))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    if (p.PropertyType == typeof(long)) _selfThings.Add(o => (long)p.GetValue(o, null));
                    else if (p.PropertyType == typeof(ulong)) _selfSteamIds.Add(o => (ulong)p.GetValue(o, null));
                }
            }

            /// <summary>Is this message only about the sender (their own character or SteamID)?</summary>
            public bool IsAboutSelf(object msg, ulong sender)
            {
                foreach (var get in _selfSteamIds)
                {
                    ulong id = get(msg);
                    if (id != 0 && id != sender) return false;
                }
                foreach (var get in _selfThings)
                {
                    long id = get(msg);
                    if (id == 0) continue;
                    if (!(Thing.Find(id) is Human h) || Identity.SteamIdOf(h) != sender) return false;
                }
                return true;
            }

            private void Inspect(Type type)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                foreach (var f in type.GetFields(flags)) Add(f.Name, f.FieldType, o => f.GetValue(o), (o, v) => f.SetValue(o, v));
                foreach (var p in type.GetProperties(flags))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    Add(p.Name, p.PropertyType, o => p.GetValue(o, null), p.CanWrite ? (o, v) => p.SetValue(o, v, null) : (Action<object, object>)null);
                }
            }

            private void Add(string name, Type type, Func<object, object> get, Action<object, object> set)
            {
                if (type == typeof(long)) _ids.Add(o => (long)get(o));
                else if (type == typeof(Vector3) && name.IndexOf("Position", StringComparison.OrdinalIgnoreCase) >= 0)
                    _positions.Add(o => (Vector3)get(o));
                else if (type == typeof(Grid3)) _cells.Add(o => (Grid3)get(o));
                else if (type == typeof(ulong) && set != null && (name == "CreatorSteamId" || name == "OwnerClientId"))
                    _creators.Add((o, v) => set(o, v));
                else if (type == typeof(bool) && name == "AuthoringMode") _authoring = o => (bool)get(o);
            }

            /// <summary>Does the message touch one of the corner flares of a new claim?</summary>
            public bool ReferencesFlare(object msg)
            {
                foreach (var get in _ids)
                    if (Flares.IsFlare(get(msg))) return true;
                return false;
            }

            /// <summary>Does the message touch a tracker beacon of an abandoned base?</summary>
            public bool ReferencesMarker(object msg)
            {
                foreach (var get in _ids)
                    if (Markers.IsMarker(get(msg))) return true;
                return false;
            }

            /// <summary>Building without a kit (the creative authoring tool).</summary>
            public bool IsAuthoring(object msg) => _authoring != null && _authoring(msg);

            /// <summary>Overwrite "who built this" fields with the verified sender.</summary>
            public void CorrectCreator(object msg, ulong actor)
            {
                foreach (var set in _creators) set(msg, actor);
            }

            public Claim FindBlocking(object msg, ClaimIndex index, ulong actor, Human actorHuman)
            {
                var needs = Rule.Needs;

                // where the sender stands (no dropping/throwing in the approach zone)
                if (Rule.ActorZone && actorHuman != null)
                {
                    var a = actorHuman.Position;
                    var c = InOwnClaim(index, a, actor, needs) ? null : index.FirstBlockingNear(a.x, a.z, ClaimGeometry.Zone, actor, needs);
                    if (c != null) return c;
                }

                foreach (var get in _ids)
                {
                    long id = get(msg);
                    if (id == 0) continue;
                    var c = CheckReferencable(id, index, actor, needs);
                    if (c != null) return c;
                }
                if (Rule.IgnorePositions) return null;

                var points = new List<Vector3>(2);
                foreach (var get in _positions) points.Add(get(msg));
                if (_cells.Count > 0)
                {
                    var grid = GridController.World;
                    if (grid == null) return index.Count > 0 ? index.Active[0] : null; // cannot tell – fail closed
                    foreach (var get in _cells) points.Add(grid.LocalToWorld(get(msg)));
                }
                float buffer = Rule.PositionBuffer;
                foreach (var p in points)
                {
                    // inside a claim where you may do this, a neighbour's zone does not matter
                    var c = InOwnClaim(index, p, actor, needs) ? null : index.FirstBlockingNear(p.x, p.z, buffer, actor, needs);
                    if (c != null) return c;
                }
                // two points (e.g. a cable run from A to B): also check the way in between
                if (points.Count >= 2) return AlongLine(points[0], points[1], index, actor, needs, buffer);
                return null;
            }

            /// <summary>
            /// Building at the landing zone switched off: does this building action touch a landing zone?
            /// (Things it refers to, positions, grid cells; things carried by living people do not count.)
            /// </summary>
            public bool TouchesLandingZone(object msg, List<LandingZone> zones)
            {
                if (zones.Count == 0 || Rule.Needs != Permission.Build || Rule.ProtectPeople) return false;
                foreach (var get in _ids)
                {
                    long id = get(msg);
                    if (id == 0) continue;
                    var thing = Thing.Find(id);
                    if (thing == null || thing is Entity) continue;
                    var carrier = thing.RootParentHuman;
                    if (carrier != null && carrier.State != EntityState.Dead) continue;
                    if (InLandingZone(thing.Position, zones)) return true;
                }
                if (Rule.IgnorePositions) return false;
                foreach (var get in _positions)
                    if (InLandingZone(get(msg), zones)) return true;
                if (_cells.Count > 0)
                {
                    var grid = GridController.World;
                    if (grid == null) return true; // cannot tell – fail closed
                    foreach (var get in _cells)
                        if (InLandingZone(grid.LocalToWorld(get(msg)), zones)) return true;
                }
                return false;
            }

            private static bool InLandingZone(Vector3 p, List<LandingZone> zones)
            {
                foreach (var z in zones)
                {
                    float dx = p.x - z.X, dz = p.z - z.Z, r = z.ProtectedRadius;
                    if (dx * dx + dz * dz < r * r) return true;
                }
                return false;
            }

            private static bool InOwnClaim(ClaimIndex index, Vector3 p, ulong actor, Permission needs)
            {
                var c = index.At(p.x, p.z);
                return c != null && (c.PermissionsOf(actor) & needs) == needs;
            }

            /// <summary>A thing, a room (atmosphere) or a pipe/cable network referred to by id.</summary>
            private Claim CheckReferencable(long id, ClaimIndex index, ulong actor, Permission needs)
            {
                var thing = Thing.Find(id);
                if (thing != null) return CheckThing(thing, index, actor, needs);

                if (!Referencable.Referencables.TryGetValue(id, out var referencable) || referencable == null) return null;
                if (referencable is Atmosphere atmosphere)
                {
                    var p = atmosphere.WorldPosition;
                    return index.FirstBlockingNear(p.x, p.z, 0f, actor, needs);
                }
                if (referencable is StructureNetwork network) return CheckMembers(network.StructureList, index, actor, needs);
                if (referencable is CableNetwork cables) return CheckMembers(cables.CableList, index, actor, needs);
                return null;
            }

            private Claim CheckMembers<T>(IEnumerable<T> members, ClaimIndex index, ulong actor, Permission needs)
            {
                int n = 0;
                foreach (var m in members)
                {
                    if (++n > MaxNetworkMembers) break;
                    Thing t = m as Thing ?? (m as INetworkedStructure)?.GetAsThing;
                    if (t == null) continue;
                    var p = t.Position;
                    var c = index.FirstBlockingNear(p.x, p.z, 0f, actor, needs);
                    if (c != null) return c;
                }
                return null;
            }

            private Claim CheckThing(Thing thing, ClaimIndex index, ulong actor, Permission needs)
            {
                if (thing is Entity entity)
                {
                    // people: only attacks, and never on yourself
                    if (!Rule.ProtectPeople || !(entity is Human h) || Identity.SteamIdOf(h) == actor) return null;
                    var hp = h.Position;
                    return index.FirstBlockingNear(hp.x, hp.z, 0f, actor, Permission.Entry);
                }
                var carrier = thing.RootParentHuman;
                // carried by a living person: not the claim's business (also your own inventory);
                // on a corpse in a claim: protected like everything else there
                if (carrier != null && carrier.State != EntityState.Dead) return null;
                var p = thing.Position;
                return index.FirstBlockingNear(p.x, p.z, 0f, actor, needs);
            }

            private static Claim AlongLine(Vector3 a, Vector3 b, ClaimIndex index, ulong actor, Permission needs, float buffer)
            {
                float length = Vector3.Distance(a, b);
                int steps = Mathf.Clamp(Mathf.CeilToInt(length), 1, 500);
                for (int i = 1; i < steps; i++)
                {
                    var p = Vector3.Lerp(a, b, i / (float)steps);
                    var c = index.FirstBlockingNear(p.x, p.z, buffer, actor, needs);
                    if (c != null) return c;
                }
                return null;
            }
        }
    }
}
