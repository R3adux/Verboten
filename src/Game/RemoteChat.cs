using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Networking;
using HarmonyLib;
using UnityEngine;

namespace Verboten.Game
{
    /// <summary>
    /// Chat from remote players, on the server:
    ///  - '/claim ...' runs as the VERIFIED sender of the message (never as the HumanId the client
    ///    writes into the message) and is not broadcast. Replies go privately to that player.
    ///  - Any other chat line is corrected to the sender's real name, so nobody can post as another
    ///    player or as "Verboten".
    /// If the sender cannot be verified, '/claim' is refused and nothing runs (fail closed).
    /// </summary>
    public static class RemoteChat
    {
        public const string SystemName = "Verboten";
        private const float CommandEvery = 1f;
        private static readonly Dictionary<ulong, float> LastCommand = new Dictionary<ulong, float>();

        public static void Register()
        {
            Hooks.Add(Hooks.Kind.Protection, "'/claim' from other players (verified sender)", "ChatMessage.Process",
                () => AccessTools.Method(typeof(ChatMessage), "Process", new[] { typeof(long) }),
                typeof(RemoteChat), prefix: nameof(ProcessPrefix));
        }

        private static bool ProcessPrefix(ChatMessage __instance)
        {
            try
            {
                if (!NetworkManager.IsServer) return true;

                // On the server this method only runs for chat received over the network.
                bool known = SenderContext.TryGetSender(out var sender);
                if (Commands.IsChatCommand(__instance.ChatText))
                {
                    if (!known)
                    {
                        Plugin.Log.LogWarning("Ignored '/claim' from a sender that could not be verified " +
                                              $"(origin {SenderContext.CurrentOrigin}, connection {SenderContext.CurrentConnection}).");
                        return false; // do not run, do not broadcast
                    }
                    var client = Client.Find(sender.Connection);
                    // some commands look at every structure in the world: one per second, or a client could lag the server
                    float now = Time.unscaledTime;
                    if (LastCommand.TryGetValue(sender.SteamId, out float last) && now - last < CommandEvery)
                    {
                        SendPrivate(client, L.T("chat.slow"));
                        return false;
                    }
                    LastCommand[sender.SteamId] = now;
                    var actor = Actor.Remote(sender, text => SendPrivate(client, text));
                    Commands.Execute(actor, Commands.ArgsFromChat(__instance.ChatText));
                    return false;
                }

                if (known) CorrectSender(__instance, sender);
                else if (LooksLikeSystem(__instance.DisplayName)) __instance.DisplayName = __instance.DisplayName.Trim() + " " + L.T("chat.player_suffix");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Remote chat handling failed: {ex}");
                // A command must never fall through to the public chat; normal chat may.
                return !Commands.IsChatCommand(__instance?.ChatText);
            }
        }

        /// <summary>Make the chat line show who really sent it.</summary>
        private static void CorrectSender(ChatMessage msg, SenderContext.RemoteIdentity sender)
        {
            var human = SenderContext.CharacterOf(sender.SteamId);
            long humanId = human != null ? human.ReferenceId : -1L;
            string name = human != null ? human.DisplayName : sender.Name;
            // nobody may look like the server's own messages
            if (LooksLikeSystem(name)) name = (name ?? "").Trim() + " " + L.T("chat.player_suffix");
            if (msg.HumanId != humanId || msg.DisplayName != name)
            {
                if (msg.HumanId != humanId)
                    Plugin.Log.LogWarning($"Chat from {sender.Name} ({sender.SteamId}) claimed to be '{msg.DisplayName}' – corrected.");
                msg.HumanId = humanId;
                msg.DisplayName = name;
            }
        }

        private static bool LooksLikeSystem(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var letters = new System.Text.StringBuilder();
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) letters.Append(char.ToLowerInvariant(c));
            return letters.ToString() == SystemName.ToLowerInvariant();
        }

        public static void Reset() => LastCommand.Clear();

        public static void SendPrivate(Client client, string text)
        {
            if (client == null || string.IsNullOrEmpty(text)) return;
            try
            {
                NetworkServer.SendToClient(new ChatMessage
                {
                    HumanId = -1L,
                    DisplayName = SystemName,
                    ChatText = text,
                }, NetworkChannel.GeneralTraffic, client);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not send a message to {client.name}: {ex.Message}");
            }
        }
    }
}
