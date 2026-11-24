using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Networking;
using Brutal.RakNetApi;
using HarmonyLib;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Optional PIN against fake identities (design 0.12).
    ///
    /// Over IP connections (all players on dedicated servers) the game cannot check a SteamID: a manipulated
    /// client could pretend to be a claim owner who is offline. A player may set a PIN ('/claim pin &lt;code&gt;',
    /// up to 10 characters). When they join over IP from an address Verboten has not seen from them before,
    /// their rights in all claims wait until they type the PIN: they can stand in their base (no kick) but cannot
    /// use, build or take anything there, and claim commands wait too. Steam P2P connections are proven by Steam
    /// and never need the PIN. Stored are only hashes: of the PIN and of the last 5 addresses.
    /// </summary>
    public static class Pins
    {
        private const int MaxFailures = 3;
        private const float WaitAfterFailures = 600f;

        /// <summary>Connections whose claim rights wait for the PIN, with the new address to remember on success.</summary>
        private static readonly Dictionary<long, string> Locked = new Dictionary<long, string>();
        private static readonly Dictionary<ulong, KeyValuePair<int, float>> Failures = new Dictionary<ulong, KeyValuePair<int, float>>();

        /// <summary>Called by the join check after an IP (RakNet) player was accepted.</summary>
        public static void OnIpJoin(SenderContext.RemoteIdentity player)
        {
            try
            {
                string address = AddressOf(player.Connection);
                string hash = address != null ? AddressHash.Of(player.SteamId, address) : null;
                bool hasPin = Plugin.Players.Get(player.SteamId)?.PinHash != null;
                if (!hasPin)
                {
                    if (hash != null) Plugin.Players.RecordAddress(player.SteamId, hash); // known addresses for a later PIN
                    return;
                }
                if (Plugin.Players.KnowsAddress(player.SteamId, hash))
                {
                    Plugin.Players.RecordAddress(player.SteamId, hash); // most recent first
                    return;
                }
                Locked[player.Connection] = hash;
                Plugin.Log.LogWarning($"{player.Name} ({player.SteamId}) joined from a new address – claim rights wait for the PIN.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"PIN check failed for {player.Name}: {ex.Message}");
            }
        }

        /// <summary>Do this player's claim rights wait for the PIN right now?</summary>
        public static bool IsLocked(ulong steamId)
        {
            if (Locked.Count == 0 || !Identity.IsValidSteamId(steamId)) return false;
            var client = Notify.ClientOf(steamId);
            return client != null && Locked.ContainsKey(client.connectionId);
        }

        public static bool HasPin(ulong steamId) => Plugin.Players.Get(steamId)?.PinHash != null;

        /// <summary>Connection closed: forget its state.</summary>
        public static void Forget(long connection) => Locked.Remove(connection);

        public static void Reset()
        {
            Locked.Clear();
            Failures.Clear();
        }

        // ------------------------------------------------------------------ '/claim pin …'

        public static void Command(Actor a, string rest, bool admin, Func<string, ulong> resolve)
        {
            if (a.IsConsole && !a.ServerAdmin && !rest.StartsWith("off ", StringComparison.OrdinalIgnoreCase))
            {
                a.Reply(L.T("pin.console_usage"));
                return;
            }
            string text = rest.Trim();
            var record = Identity.IsValidSteamId(a.SteamId) ? Plugin.Players.GetOrAdd(a.SteamId) : new KnownPlayer();

            if (text.Length == 0)
            {
                a.Reply(record.PinHash != null ? L.T("pin.status_has") : L.T("pin.status_none", ("max", Core.Pin.MaxLength)));
                return;
            }

            string first = text.Split(' ')[0].ToLowerInvariant();
            if (first == "off" || first == "aus")
            {
                string who = text.Substring(first.Length).Trim();
                if (who.Length > 0)
                {
                    if (!admin) { a.Reply(L.T("pin.admin_only")); return; }
                    ulong id = resolve(who);
                    if (id == 0) return; // resolve replied
                    var other = Plugin.Players.Get(id);
                    if (other?.PinHash == null) { a.Reply(L.T("pin.none_for", ("player", World.NameOf(id)))); return; }
                    other.PinHash = null;
                    Plugin.Log.LogInfo($"{a.Name} removed the PIN of {World.NameOf(id)} ({id}).");
                    a.Reply(L.T("pin.removed_for", ("player", World.NameOf(id))));
                    return;
                }
                if (IsLocked(a.SteamId)) { a.Reply(L.T("pin.first")); return; }
                record.PinHash = null;
                a.Reply(L.T("pin.removed"));
                return;
            }

            string pin = Core.Pin.Clean(text);
            if (pin == null) { a.Reply(L.T("pin.length", ("max", Core.Pin.MaxLength))); return; }

            if (IsLocked(a.SteamId)) { Unlock(a, record, pin); return; }

            record.PinHash = Core.Pin.Hash(a.SteamId, pin);
            // the address they are on now counts as known
            if (a.Connection != 0)
            {
                string address = AddressOf(a.Connection);
                if (address != null) Plugin.Players.RecordAddress(a.SteamId, AddressHash.Of(a.SteamId, address));
            }
            a.Reply(L.T("pin.set") + (a.Verified ? L.T("pin.set_steam") : ""));
        }

        private static void Unlock(Actor a, KnownPlayer record, string pin)
        {
            float now = Time.unscaledTime;
            if (Failures.TryGetValue(a.SteamId, out var f) && f.Key >= MaxFailures && now < f.Value)
            {
                a.Reply(L.T("pin.wait", ("minutes", Mathf.CeilToInt((f.Value - now) / 60f))));
                return;
            }
            if (!Core.Pin.Matches(record, pin))
            {
                int count = (f.Value > 0f && now >= f.Value ? 0 : f.Key) + 1;
                Failures[a.SteamId] = new KeyValuePair<int, float>(count, count >= MaxFailures ? now + WaitAfterFailures : 0f);
                Plugin.Log.LogWarning($"Wrong PIN for {a.Name} ({a.SteamId}), attempt {count}.");
                a.Reply(count >= MaxFailures ? L.T("pin.wrong_wait", ("minutes", $"{WaitAfterFailures / 60f:0}")) : L.T("pin.wrong"));
                return;
            }
            Failures.Remove(a.SteamId);
            var client = Notify.ClientOf(a.SteamId);
            if (client != null && Locked.TryGetValue(client.connectionId, out string hash))
            {
                Locked.Remove(client.connectionId);
                if (hash != null) Plugin.Players.RecordAddress(a.SteamId, hash);
            }
            Plugin.Log.LogInfo($"{a.Name} ({a.SteamId}) confirmed the PIN.");
            a.Reply(L.T("pin.ok"));
        }

        // ------------------------------------------------------------------ address of a connection

        /// <summary>IPv4 address of a RakNet connection, or null.</summary>
        public static string AddressOf(long connection)
        {
            try
            {
                // Both are private in the game; looked up by name, a game update makes this return null (no crash).
                var instanceField = AccessTools.Field(typeof(NetworkManager), "Instance");
                var peerField = AccessTools.Field(typeof(NetworkManager), "rakNet");
                var manager = instanceField?.GetValue(null);
                if (manager == null || peerField == null || peerField.FieldType != typeof(RakPeerInstance)) return null;
                var peer = (RakPeerInstance)peerField.GetValue(manager);
                if (peer.IsNull()) return null;
                var address = peer.GetSystemAddressFromGuid(new RakNetGUID { G = unchecked((ulong)connection), SystemIndex = ushort.MaxValue });
                string text = address.ToString(false);
                return string.IsNullOrEmpty(text) || text == "0.0.0.0" ? null : text;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not read a player's address: {ex.Message}");
                return null;
            }
        }
    }
}
