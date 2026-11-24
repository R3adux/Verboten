using System;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Networking;
using HarmonyLib;
using Util.Commands;

namespace Verboten.Game
{
    /// <summary>
    /// Entry points for the 'claim' command that are trusted without network verification:
    /// the F3 console and the host's own chat. Chat from remote clients is NOT handled
    /// here – that needs verified senders (milestone M1).
    /// </summary>
    public static class CommandEntry
    {
        public static void Register()
        {
            try
            {
                CommandLine.AddCommand(Commands.Word, new ClaimConsoleCommand());
            }
            catch (Exception ex)
            {
                Hooks.ReportProblem(L.T("host.console_failed"));
                Plugin.Log.LogError($"Failed to register the console command: {ex}");
            }

            // The first parameter is the typed text; the game currently passes (string, string).
            Hooks.Add(Hooks.Kind.Feature, "'/claim' in the host chat", "InventoryManager.OnChatMessage",
                () => AccessTools.Method(typeof(InventoryManager), "OnChatMessage"),
                typeof(CommandEntry), prefix: nameof(HostChatPrefix));
        }

        /// <summary>Host / single player typing '/claim ...' in chat. Clients fall through unchanged.</summary>
        private static bool HostChatPrefix(string __0)
        {
            try
            {
                if (NetworkManager.IsClient || !Commands.IsChatCommand(__0)) return true;
                Commands.Execute(Actor.Local(), Commands.ArgsFromChat(__0));
                return false; // do not broadcast the command as chat
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Host chat command failed: {ex}");
                return true;
            }
        }
    }

    public sealed class ClaimConsoleCommand : CommandBase
    {
        public override string HelpText => "Verboten base claims. Use 'claim help'.";
        public override string[] Arguments => new[] { "check | show | find | info | grow <side> <m> | add <player> [guest] | remove <player> | name <name> | owner <player> | delete | take | list | shrink | status" };
        public override bool IsLaunchCmd => false;

        public override string Execute(string[] args)
        {
            if (GameManager.GameState != GameState.Running && GameManager.GameState != GameState.Paused)
                return L.T("console.only_world");
            if (NetworkManager.IsClient)
                return L.T("console.client");

            // Inside network processing the only way here is the game's 'serverrun' (ServerRunCommandMessage), which the
            // game allowed because the client knows the server's ServerAuthSecret: that player is an admin. Run as them,
            // with their position, and answer them – otherwise they would see nothing (dedicated servers).
            if (SenderContext.CurrentOrigin == SenderContext.Origin.Network)
            {
                if (!SenderContext.TryGetSender(out var sender))
                {
                    Plugin.Log.LogWarning("'serverrun claim' from a sender that could not be verified – ignored.");
                    return null;
                }
                var client = Client.Find(sender.Connection);
                var actor = Actor.Remote(sender, text => RemoteChat.SendPrivate(client, text));
                actor.ServerAdmin = true;
                Plugin.Log.LogInfo($"{actor.Name} ({actor.SteamId}) runs 'claim {string.Join(" ", args ?? new string[0])}' with the server secret.");
                Commands.Execute(actor, args ?? new string[0]);
                return null;
            }
            Commands.Execute(Actor.Local(), args ?? new string[0]);
            return null;
        }
    }
}
