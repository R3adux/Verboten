using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace Verboten.Core
{
    /// <summary>
    /// What the server remembers about a player (saved with the world):
    ///  - when they were last online (server minutes) – for "abandoned" ruins and claims,
    ///  - whether they saw the window that explains claims (comes with the first thing they build),
    ///  - recent join addresses as short hashes and an optional PIN (as a hash) – for players on IP connections,
    ///    whose SteamID the game cannot check: from a new address their rights wait for the PIN.
    /// </summary>
    public sealed class KnownPlayer
    {
        public const int MaxAddresses = 5;

        [XmlAttribute] public ulong SteamId;
        [XmlAttribute] public string Name;
        [XmlAttribute] public long LastSeenMinute;
        [XmlAttribute] public bool GuideShown;
        /// <summary>One-way hash of the player's PIN; null = no PIN.</summary>
        [XmlAttribute] public string PinHash;
        [XmlElement("Address")] public List<string> AddressHashes = new List<string>();
        /// <summary>Messages to show the next time the player joins (e.g. "your base was taken over").</summary>
        [XmlElement("Notice")] public List<string> Notices = new List<string>();
        /// <summary>Square meters on the player's account: start credit plus area earned by playing, not used yet.</summary>
        [XmlAttribute] public float Area;
        /// <summary>The start credit was given (once per player).</summary>
        [XmlAttribute] public bool AreaGiven;

        public KnownPlayer Clone() => new KnownPlayer
        {
            SteamId = SteamId,
            Name = Name,
            LastSeenMinute = LastSeenMinute,
            GuideShown = GuideShown,
            PinHash = PinHash,
            AddressHashes = new List<string>(AddressHashes),
            Notices = new List<string>(Notices),
            Area = Area,
            AreaGiven = AreaGiven,
        };
    }

    public static class AddressHash
    {
        /// <summary>Short one-way hash of SteamID + address (the address itself is never stored).</summary>
        public static string Of(ulong steamId, string address)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"verboten:{steamId}:{address?.Trim()}"));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }

    public static class Pin
    {
        public const int MaxLength = 10;

        /// <summary>The PIN as typed (trimmed), or null if it is empty or longer than <see cref="MaxLength"/>.</summary>
        public static string Clean(string text)
        {
            string pin = text?.Trim();
            return string.IsNullOrEmpty(pin) || pin.Length > MaxLength ? null : pin;
        }

        /// <summary>One-way hash of a PIN, different for every player (the PIN itself is never stored).</summary>
        public static string Hash(ulong steamId, string pin)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"verboten-pin:{steamId}:{pin}"));
                var sb = new StringBuilder(64);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static bool Matches(KnownPlayer player, string pin) =>
            player?.PinHash != null && pin != null && player.PinHash == Hash(player.SteamId, pin);
    }

    public sealed class KnownPlayerList
    {
        private readonly Dictionary<ulong, KnownPlayer> _players = new Dictionary<ulong, KnownPlayer>();

        public int Count => _players.Count;

        public void Clear() => _players.Clear();

        public void Load(IEnumerable<KnownPlayer> players)
        {
            _players.Clear();
            if (players == null) return;
            foreach (var p in players)
            {
                if (p == null || p.SteamId == 0) continue;
                if (p.AddressHashes == null) p.AddressHashes = new List<string>();
                if (p.Notices == null) p.Notices = new List<string>();
                _players[p.SteamId] = p;
            }
        }

        public List<KnownPlayer> CopyForSave()
        {
            var list = new List<KnownPlayer>(_players.Count);
            foreach (var p in _players.Values) list.Add(p.Clone());
            return list;
        }

        public KnownPlayer Get(ulong steamId) => _players.TryGetValue(steamId, out var p) ? p : null;

        public KnownPlayer GetOrAdd(ulong steamId)
        {
            if (!_players.TryGetValue(steamId, out var p)) _players[steamId] = p = new KnownPlayer { SteamId = steamId };
            return p;
        }

        /// <summary>Mark a player as online right now.</summary>
        public void Seen(ulong steamId, string name, long minute)
        {
            var p = GetOrAdd(steamId);
            p.LastSeenMinute = minute;
            if (!string.IsNullOrEmpty(name)) p.Name = name;
        }

        /// <summary>Everyone who was ever on this server under exactly this name (case-insensitive).</summary>
        public List<KnownPlayer> FindByName(string name)
        {
            var found = new List<KnownPlayer>();
            if (string.IsNullOrWhiteSpace(name)) return found;
            foreach (var p in _players.Values)
                if (string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) found.Add(p);
            return found;
        }

        /// <summary>Server minutes since the player was last online; null if never seen.</summary>
        public long? MinutesAway(ulong steamId, long now) =>
            _players.TryGetValue(steamId, out var p) ? now - p.LastSeenMinute : (long?)null;

        public enum AddressSeen
        {
            /// <summary>No address recorded before for this player – recorded now, nothing to compare.</summary>
            FirstTime,
            /// <summary>Joined from this address before.</summary>
            Known,
            /// <summary>Has joined before, but never from this address.</summary>
            NewAddress,
        }

        /// <summary>Has the player joined from this address before? (Records nothing.)</summary>
        public bool KnowsAddress(ulong steamId, string addressHash) =>
            _players.TryGetValue(steamId, out var p) && addressHash != null && p.AddressHashes.Contains(addressHash);

        /// <summary>Record that a player joined from an address hash; tells whether it is new.</summary>
        public AddressSeen RecordAddress(ulong steamId, string addressHash)
        {
            var p = GetOrAdd(steamId);
            int i = p.AddressHashes.IndexOf(addressHash);
            if (i >= 0)
            {
                p.AddressHashes.RemoveAt(i); // move to the end = most recent
                p.AddressHashes.Add(addressHash);
                return AddressSeen.Known;
            }
            p.AddressHashes.Add(addressHash);
            while (p.AddressHashes.Count > KnownPlayer.MaxAddresses) p.AddressHashes.RemoveAt(0);
            return p.AddressHashes.Count == 1 ? AddressSeen.FirstTime : AddressSeen.NewAddress;
        }
    }
}
