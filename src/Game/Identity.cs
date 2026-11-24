using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects.Entities;
using UnityEngine;

namespace Verboten.Game
{
    /// <summary>Who is running a command or performing an action.</summary>
    public sealed class Actor
    {
        /// <summary>The character, or null for the server console.</summary>
        public Human Human;
        public ulong SteamId;
        public string Name;
        public bool IsConsole;
        /// <summary>
        /// True if the SteamID is proven: the host itself, or a Steam P2P connection.
        /// False for IP connections, where the game takes the SteamID the client claims.
        /// </summary>
        public bool Verified;
        /// <summary>The host playing on their own machine (never set for network players).</summary>
        public bool IsLocalHost;
        /// <summary>Network connection of a remote player (0 for the host/console).</summary>
        public long Connection;
        /// <summary>A remote player who ran the command with the server's ServerAuthSecret ('serverrun claim …').</summary>
        public bool ServerAdmin;
        public Action<string> Reply = _ => { };

        public bool HasPosition => Human != null;
        public Vector3 Position => Human != null ? Human.Position : Vector3.zero;

        /// <summary>The host's own character / console (always trusted, never via network).</summary>
        public static Actor Local()
        {
            var human = GameManager.IsBatchMode ? null : Human.LocalHuman;
            return new Actor
            {
                Human = human,
                SteamId = human != null ? Identity.SteamIdOf(human) : 0,
                Name = human != null ? human.DisplayName : "Console",
                IsConsole = human == null,
                IsLocalHost = human != null,
                Verified = true,
                Reply = Identity.PrintLocal,
            };
        }

        /// <summary>A remote player whose message was verified by <see cref="SenderContext"/>.</summary>
        public static Actor Remote(SenderContext.RemoteIdentity id, Action<string> reply)
        {
            var human = SenderContext.CharacterOf(id.SteamId);
            return new Actor
            {
                Human = human,
                SteamId = id.SteamId,
                Name = human != null ? human.DisplayName : id.Name,
                IsConsole = false,
                Verified = id.Verified,
                Connection = id.Connection,
                Reply = reply,
            };
        }
    }

    public static class Identity
    {
        public const ulong MinSteamId = 76561197960265728UL;

        private static readonly HashSet<ulong> AdminIds = new HashSet<ulong>();
        private static string _adminSource;

        public static bool IsValidSteamId(ulong id) => id >= MinSteamId;

        /// <summary>SteamID64 of a character, or 0 if unknown.</summary>
        public static ulong SteamIdOf(Human human)
        {
            if (human == null) return 0;
            var brain = human.OrganBrain;
            if (brain != null && IsValidSteamId(brain.ClientId)) return brain.ClientId;
            if (IsValidSteamId(human.OwnerClientId)) return human.OwnerClientId;
            if (!GameManager.IsBatchMode && human == Human.LocalHuman && IsValidSteamId(NetworkManager.LocalClientId))
                return NetworkManager.LocalClientId;
            return 0;
        }

        /// <summary>
        /// Admins: the host's own character/console and the configured SteamIDs.
        /// Default deny: an unknown actor is never an admin.
        /// </summary>
        public static bool IsAdmin(Actor actor)
        {
            if (actor == null) return false;
            if (actor.IsConsole || actor.IsLocalHost || actor.ServerAdmin) return true;
            // A listed SteamID only counts if the SteamID is proven (see Actor.Verified).
            return actor.Verified && IsAdminId(actor.SteamId);
        }

        public static bool IsAdminId(ulong id)
        {
            if (!IsValidSteamId(id)) return false;
            string source = Plugin.Settings.AdminSteamIds.Value ?? string.Empty;
            if (source != _adminSource)
            {
                _adminSource = source;
                AdminIds.Clear();
                foreach (var part in source.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (ulong.TryParse(part.Trim(), out var parsed) && IsValidSteamId(parsed)) AdminIds.Add(parsed);
            }
            return AdminIds.Contains(id);
        }

        public static bool IsOnline(ulong steamId)
        {
            if (!IsValidSteamId(steamId)) return false;
            if (!GameManager.IsBatchMode && SteamIdOf(Human.LocalHuman) == steamId) return true;
            return Client.Find(steamId) != null;
        }

        public static void PrintLocal(string message)
        {
            ConsoleWindow.Print("[Verboten] " + message, ConsoleColor.Yellow, false, false, true);
        }
    }
}
