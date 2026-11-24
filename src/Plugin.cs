using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Verboten
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "verboten.stationeers";
        public const string Name = "Verboten";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;
        internal static Settings Settings;
        /// <summary>All claims of the current world (server/host only).</summary>
        internal static readonly Core.ClaimStore Store = new Core.ClaimStore();
        /// <summary>Recent join addresses (hashed) per player, for the impersonation warning.</summary>
        internal static readonly Core.KnownPlayerList Players = new Core.KnownPlayerList();

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Settings = new Settings(Config);
            Game.L.Load();

            try
            {
                _harmony = new Harmony(Guid);
                // Every game hook is declared in a Register() method and installed (and checked) in one place.
                // The sender check comes first: everything that trusts a remote player builds on it.
                Register("sender check", Game.SenderContext.Register);
                Register("persistence", Game.Persistence.Register);
                Register("commands", Game.CommandEntry.Register);
                Register("remote chat", Game.RemoteChat.Register);
                Register("protection", Game.Protection.Register);
                Register("claim anchor", Game.Anchor.Register);
                Register("explosion damage", Game.Damage.Register);
                Register("robots", Game.Robots.Register);
                Game.Hooks.InstallAll(_harmony);
                Log.LogInfo($"{Name} {Version} loaded.");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to initialize: {ex}");
            }
        }

        private void Update()
        {
            try
            {
                Game.SenderContext.Tick();
                Game.Upkeep.Tick();
                Game.Trespass.Tick();
                Game.Robots.Tick();
                Game.BorderDisplay.Tick();
                Game.Flares.Tick();
            }
            catch (Exception ex) { Log.LogError($"Update failed: {ex}"); }
        }

        /// <summary>Each part registers independently, so one broken part does not take down the rest.</summary>
        private static void Register(string part, Action register)
        {
            try { register(); }
            catch (Exception ex) { Log.LogError($"Failed to register {part}: {ex}"); }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
