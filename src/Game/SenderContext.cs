using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects.Entities;
using HarmonyLib;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Who really sent the network message that is being processed right now.
    ///
    /// The game never tells its message handlers the sender (it passes its own field instead), and
    /// it takes the player's SteamID and connection id from what the client claims when joining.
    /// Verboten therefore:
    ///  1. passes the true sender of each packet into the message handlers (transpiler on
    ///     NetworkManager.ReceiveEvents),
    ///  2. remembers which connections the server itself saw open and how (PlayerConnected),
    ///  3. checks the join request (NetworkServer.VerifyConnection): the client must use its own
    ///     connection, and on Steam P2P the SteamID must be the one Steam reports for the packets.
    /// Anything that cannot be verified is treated as unknown – never as the host (fail closed).
    /// </summary>
    public static class SenderContext
    {
        public enum Origin
        {
            /// <summary>Not inside network processing: the host's own input or game logic.</summary>
            Local,
            /// <summary>Processing a packet from <see cref="CurrentConnection"/>.</summary>
            Network,
            /// <summary>A message the game queued and runs later; the sender is lost.</summary>
            Deferred,
        }

        public sealed class RemoteIdentity
        {
            public long Connection;
            public ulong SteamId;
            public string Name;
            public ConnectionMethod Method;
            /// <summary>True if Steam itself vouches for the SteamID (Steam P2P). False for IP connections.</summary>
            public bool Verified;
        }

        [ThreadStatic] private static Origin _origin;
        [ThreadStatic] private static long _connection;

        private static Action<long, int> _handleGeneralTraffic;
        private static readonly Dictionary<long, ConnectionMethod> Observed = new Dictionary<long, ConnectionMethod>();
        private static readonly Dictionary<long, RemoteIdentity> Identities = new Dictionary<long, RemoteIdentity>();
        private static readonly List<KeyValuePair<float, Action>> Delayed = new List<KeyValuePair<float, Action>>();

        public static Origin CurrentOrigin => _origin;
        public static long CurrentConnection => _connection;

        /// <summary>All parts installed: remote senders can be identified.</summary>
        public static bool Ready =>
            _handleGeneralTraffic != null
            && Hooks.IsInstalled(TargetReceive)
            && Hooks.IsInstalled(TargetConnected)
            && Hooks.IsInstalled(TargetVerify);

        private const string TargetReceive = "NetworkManager.ReceiveEvents";
        private const string TargetConnected = "NetworkManager.PlayerConnected";
        private const string TargetVerify = "NetworkServer.VerifyConnection";

        public static void Register()
        {
            var t = typeof(SenderContext);
            var handle = AccessTools.Method(typeof(NetworkManager), "HandleGeneralTraffic", new[] { typeof(long), typeof(int) });
            if (handle != null)
                _handleGeneralTraffic = AccessTools.MethodDelegate<Action<long, int>>(handle);

            Hooks.Add(Hooks.Kind.Protection, "Know who sends each network message", TargetReceive,
                () => handle == null ? null : AccessTools.Method(typeof(NetworkManager), "ReceiveEvents"),
                t, transpiler: nameof(ReceiveEventsTranspiler));
            Hooks.Add(Hooks.Kind.Protection, "Track connections opened by the server", TargetConnected,
                () => AccessTools.Method(typeof(NetworkManager), "PlayerConnected", new[] { typeof(long), typeof(ConnectionMethod) }),
                t, prefix: nameof(PlayerConnectedPrefix));
            Hooks.Add(Hooks.Kind.Protection, "Track closed connections", "NetworkManager.PlayerDisconnected",
                () => AccessTools.Method(typeof(NetworkManager), "PlayerDisconnected", new[] { typeof(long) }),
                t, prefix: nameof(ConnectionGonePrefix));
            Hooks.Add(Hooks.Kind.Protection, "Track removed players", "NetworkServer.ClientDisconnected",
                () => AccessTools.Method(typeof(NetworkServer), "ClientDisconnected", new[] { typeof(long) }),
                t, prefix: nameof(ConnectionGonePrefix));
            Hooks.Add(Hooks.Kind.Protection, "Check SteamID and connection when a player joins", TargetVerify,
                () => AccessTools.Method(typeof(NetworkServer), "VerifyConnection", new[] { typeof(long), typeof(NetworkMessages.VerifyPlayer) }),
                t, prefix: nameof(VerifyConnectionPrefix));
            Hooks.Add(Hooks.Kind.Protection, "Mark queued messages as unknown sender", "DeferredMessageQueue.Drain",
                () => AccessTools.Method(typeof(DeferredMessageQueue), "Drain"),
                t, prefix: nameof(DrainPrefix), finalizer: nameof(DrainFinalizer));
        }

        // ------------------------------------------------------------------ queries

        /// <summary>The verified identity of the sender of the message being processed, if any.</summary>
        public static bool TryGetSender(out RemoteIdentity identity)
        {
            identity = null;
            if (_origin != Origin.Network || !Ready) return false;
            if (!Identities.TryGetValue(_connection, out var id)) return false;
            var client = Client.Find(_connection);
            if (client == null || client.IsHost || client.ClientId != id.SteamId) return false;
            identity = id;
            return true;
        }

        /// <summary>True if this connection joined through the check with a SteamID proven by Steam.</summary>
        public static bool IsVerifiedConnection(long connection, ulong steamId) =>
            Ready && Identities.TryGetValue(connection, out var id) && id.Verified && id.SteamId == steamId;

        /// <summary>
        /// Is <paramref name="actor"/> really the one doing this? Network: the sender must control
        /// that character. Local: only the host's own character. Deferred/unknown: never.
        /// </summary>
        public static bool IsGenuine(Human actor, out ulong steamId, out bool verified)
        {
            steamId = 0;
            verified = false;
            if (actor == null) return false;
            switch (_origin)
            {
                case Origin.Local:
                    if (GameManager.IsBatchMode || actor != Human.LocalHuman) return false;
                    steamId = Identity.SteamIdOf(actor);
                    verified = true;
                    return true;
                case Origin.Network:
                    if (!TryGetSender(out var id) || Identity.SteamIdOf(actor) != id.SteamId) return false;
                    steamId = id.SteamId;
                    verified = id.Verified;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The living character controlled by a player (not a corpse), or null.</summary>
        public static Human CharacterOf(ulong steamId)
        {
            Human fallback = null;
            foreach (var h in Human.AllHumans)
            {
                if (h == null || Identity.SteamIdOf(h) != steamId) continue;
                if (h.OrganBrain != null) return h;
                fallback = fallback ?? h;
            }
            return fallback;
        }

        public static void Reset()
        {
            Observed.Clear();
            Identities.Clear();
            Delayed.Clear();
        }

        /// <summary>Run an action a little later (e.g. close a connection after the last message went out).</summary>
        public static void Later(float seconds, Action action) =>
            Delayed.Add(new KeyValuePair<float, Action>(Time.unscaledTime + seconds, action));

        /// <summary>Called every frame by the plugin; runs delayed actions (closing rejected connections).</summary>
        public static void Tick()
        {
            if (Delayed.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = Delayed.Count - 1; i >= 0; i--)
            {
                if (Delayed[i].Key > now) continue;
                var action = Delayed[i].Value;
                Delayed.RemoveAt(i);
                try { action(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"Delayed action failed: {ex.Message}"); }
            }
        }

        // ------------------------------------------------------------------ transpiler

        private static IEnumerable<CodeInstruction> ReceiveEventsTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var list = new List<CodeInstruction>(instructions);
            var ops = new List<IlOp>(list.Count);
            foreach (var ci in list)
            {
                ops.Add(new IlOp
                {
                    OpCode = ci.opcode.Name,
                    CallTarget = ci.operand is MethodBase m ? $"{m.DeclaringType?.Name}.{m.Name}" : null,
                    Local = ci.IsLdloc() ? SenderPattern.LocalIndexOf(ci.opcode.Name, OperandIndex(ci.operand)) : -1,
                });
            }

            var found = SenderPattern.Find(ops);
            if (!found.Ok) throw new Hooks.PatternException("ReceiveEvents changed: " + found.Error);

            var locals = __originalMethod.GetMethodBody()?.LocalVariables;
            if (locals == null || found.SenderLocal >= locals.Count || locals[found.SenderLocal].LocalType != typeof(long))
                throw new Hooks.PatternException("ReceiveEvents changed: sender variable is not a long");

            var wrapper = AccessTools.Method(typeof(SenderContext), nameof(HandleGeneralTrafficWithSender));
            var loadSender = list[found.SenderLoadIndex];
            // Walk backwards so earlier indexes stay valid while inserting.
            for (int k = found.HandleCalls.Count - 1; k >= 0; k--)
            {
                int i = found.HandleCalls[k];
                if (list[i].labels.Count > 0 || list[i].blocks.Count > 0)
                    throw new Hooks.PatternException("ReceiveEvents changed: jump or exception block at the HandleGeneralTraffic call");
                // stack before: hostId, size   ->   hostId, size, sender
                list[i] = new CodeInstruction(OpCodes.Call, wrapper);
                list.Insert(i, new CodeInstruction(loadSender.opcode, loadSender.operand));
            }
            return list;
        }

        /// <summary>Local variable index from an ldloc operand (LocalBuilder / LocalVariableInfo / number).</summary>
        private static object OperandIndex(object operand)
        {
            switch (operand)
            {
                case LocalVariableInfo info: return info.LocalIndex;
                case byte b: return (int)b;
                case sbyte sb: return (int)sb;
                case short s: return (int)s;
                case ushort us: return (int)us;
                case int i: return i;
                default: return null;
            }
        }

        /// <summary>Replaces HandleGeneralTraffic(hostId, size) inside ReceiveEvents.</summary>
        public static void HandleGeneralTrafficWithSender(long hostId, int size, long sender)
        {
            var previousOrigin = _origin;
            long previousConnection = _connection;
            _origin = Origin.Network;
            _connection = sender;
            try
            {
                _handleGeneralTraffic(hostId, size);
            }
            finally
            {
                _origin = previousOrigin;
                _connection = previousConnection;
            }
        }

        // ------------------------------------------------------------------ connections

        private static void PlayerConnectedPrefix(long __0, ConnectionMethod __1)
        {
            // Only the server's own view counts; a client calls this for its connection to the host.
            if (NetworkManager.NetworkRole == NetworkRole.Server) Observed[__0] = __1;
        }

        private static void ConnectionGonePrefix(long __0)
        {
            Observed.Remove(__0);
            Identities.Remove(__0);
            Pins.Forget(__0);
        }

        private static void DrainPrefix(out Origin __state)
        {
            __state = _origin;
            _origin = Origin.Deferred;
        }

        private static Exception DrainFinalizer(Exception __exception, Origin __state)
        {
            _origin = __state;
            return __exception;
        }

        // ------------------------------------------------------------------ join check

        private static bool VerifyConnectionPrefix(long __0, NetworkMessages.VerifyPlayer __1)
        {
            try
            {
                var msg = __1;
                if (!NetworkManager.IsServer || msg == null) return true;
                if (_origin != Origin.Network)
                {
                    // Sender tracking is not working (game update?). Let the game decide as without the mod;
                    // nobody gets an identity, so remote players have no rights in claims (fail closed).
                    Plugin.Log.LogWarning($"Cannot verify joining player {msg.Name} ({msg.ClientId}): sender unknown.");
                    return true;
                }

                long sender = _connection;
                string problem = Check(sender, msg, out var method);
                if (problem != null)
                {
                    Plugin.Log.LogWarning($"Rejected join of '{msg.Name}' (claims {msg.ClientId}) from connection {sender}: {problem}");
                    Reject(__0, sender, method, msg, problem);
                    return false;
                }

                var identity = new RemoteIdentity
                {
                    Connection = sender,
                    SteamId = msg.ClientId,
                    Name = msg.Name,
                    Method = method,
                    Verified = method == ConnectionMethod.FacepunchSteamP2P,
                };
                Identities[sender] = identity;
                Plugin.Log.LogInfo($"Player '{msg.Name}' ({msg.ClientId}) joins via {method}" +
                                   (identity.Verified ? ", SteamID confirmed by Steam." : ", SteamID not verifiable (IP connection)."));
                if (!identity.Verified) Pins.OnIpJoin(identity);
                return true;
            }
            catch (Exception ex)
            {
                // Never crash the join; but without an identity the player has no rights in claims.
                Plugin.Log.LogError($"Join check failed: {ex}");
                return true;
            }
        }

        /// <summary>Null if the join request is consistent, otherwise the reason.</summary>
        private static string Check(long sender, NetworkMessages.VerifyPlayer msg, out ConnectionMethod method)
        {
            method = msg.ClientConnectionMethod;
            if (!Observed.TryGetValue(sender, out method))
            {
                method = msg.ClientConnectionMethod;
                return "the server never opened this connection";
            }
            if (msg.OwnerConnectionId != sender)
                return $"claims connection {msg.OwnerConnectionId}, but sends from {sender}";
            if (msg.ClientConnectionMethod != method)
                return $"claims {msg.ClientConnectionMethod}, but connected via {method}";
            if (!Identity.IsValidSteamId(msg.ClientId))
                return "invalid SteamID";
            if (method == ConnectionMethod.FacepunchSteamP2P && msg.ClientId != unchecked((ulong)sender))
                return $"claims SteamID {msg.ClientId}, but Steam reports {unchecked((ulong)sender)}";
            if (NetworkManager.HostClient != null && NetworkManager.HostClient.ClientId == msg.ClientId)
                return "uses the host's SteamID";
            if (Trespass.IsBlocked(msg.ClientId, out int minutes))
                return L.T("join.blocked", ("minutes", minutes));
            foreach (var other in NetworkBase.Clients)
                if (other != null && other.ClientId == msg.ClientId && other.connectionId != sender)
                    return "this SteamID is already connected";
            return null;
        }

        private static void Reject(long hostId, long sender, ConnectionMethod method, NetworkMessages.VerifyPlayer msg, string reason)
        {
            try
            {
                var client = new Client(hostId, sender, msg.ClientId, msg.Name, method);
                NetworkServer.SendToClient(new NetworkMessages.Handshake
                {
                    HandshakeState = HandshakeType.Rejected,
                    Message = L.T("join.rejected", ("reason", reason)),
                }, NetworkChannel.GeneralTraffic, client);
                Delayed.Add(new KeyValuePair<float, Action>(Time.unscaledTime + 0.5f,
                    () => NetworkManager.CloseConnectionServer(sender, method)));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not notify the rejected player: {ex.Message}");
                NetworkManager.CloseConnectionServer(sender, method);
            }
        }
    }
}
