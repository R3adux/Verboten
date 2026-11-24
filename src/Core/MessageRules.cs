using System;
using System.Collections.Generic;

namespace Verboten.Core
{
    /// <summary>
    /// What permission a player needs for each kind of network message that touches a claim.
    ///
    /// Every action of a remote player reaches the server as a message. Verboten looks at every
    /// message, finds the things and positions it refers to, and checks them against the claims.
    /// The table below only says which permission is needed; message types that are not listed need
    /// the strictest one (Build). That way, message types added by a game update are protected too.
    /// </summary>
    public static class MessageRules
    {
        public enum Check
        {
            /// <summary>Never checked (connection, chat, emotes, pings, sounds).</summary>
            Skip,
            /// <summary>Checked against the permission below.</summary>
            Permission,
        }

        public enum Cheat
        {
            None,
            /// <summary>Group A: the game never sends this – only manipulated clients. Kick.</summary>
            Never,
            /// <summary>Group B: cheat features of the normal game (console, creative menu). Block outside creative mode.</summary>
            VanillaCheat,
        }

        public sealed class Rule
        {
            public Check Check = Check.Permission;
            public Permission Needs = Permission.Build;
            /// <summary>Also block this close (meters) to a claim. Mining digs a bit around the point.</summary>
            public float Buffer;
            /// <summary>Only the things in the message count, not positions (dropping own items is fine).</summary>
            public bool IgnorePositions;
            /// <summary>Also check where the SENDER stands, with the approach zone (no dropping/throwing near claims).</summary>
            public bool ActorZone;
            /// <summary>People in the message count too (attacks on people in claims).</summary>
            public bool ProtectPeople;
            public Cheat Cheat;
            /// <summary>
            /// The message may only be about the sender themself (their own character or SteamID). The game trusts
            /// the ids in these messages; a manipulated client could otherwise kill, puppet or respawn other players.
            /// </summary>
            public bool SelfOnly;

            /// <summary>Buffer for positions: building near claims is not allowed within the approach zone.</summary>
            public float PositionBuffer => (Needs & Permission.Build) != 0 ? Math.Max(Buffer, ClaimGeometry.Zone) : Buffer;
        }

        public const float MiningBuffer = 2f;

        private static readonly Rule Skip = new Rule { Check = Check.Skip };
        private static readonly Rule Default = new Rule { Needs = Permission.Build };

        private static readonly Dictionary<string, Rule> Rules = Build();

        /// <summary>Rule for a message type (short type name, nested types without the outer class).</summary>
        public static Rule For(string typeName) =>
            typeName != null && Rules.TryGetValue(typeName, out var rule) ? rule : Default;

        public static bool IsListed(string typeName) => typeName != null && Rules.ContainsKey(typeName);

        /// <summary>All message type names with an explicit rule (for tests against the game).</summary>
        public static IEnumerable<string> ListedNames => Rules.Keys;

        private static Dictionary<string, Rule> Build()
        {
            var r = new Dictionary<string, Rule>();
            void Set(Rule rule, params string[] names) { foreach (var n in names) r[n] = rule; }

            // connection, chat (handled separately), cosmetics, pings, sounds, own body
            // Only message types the server actually processes (IMessageProcessable) belong here;
            // a test checks every name against the installed game.
            Set(Skip,
                "Handshake", "VerifyPlayer", "VerifyPlayerRequest",
                "ChatMessage", "DismissHelperHintMessage",
                "PingBoxMessage", "PingObjectMessage", "PingPositionMessage", "PlayAudioClipsDataMessage",
                "PlayWorldAudioClipsDataMessage", "AvailableDLCMessage",
                "JetpackStateMessage", "EmitSparksMessage", "WeldingEffect", "HumanSpawnEffect",
                // protected by the game's own server secret
                "ServerRunCommandMessage");

            // only about yourself: own state (unconscious → dead), respawn, give up control, looks, emotes, "typing…"
            Set(new Rule { SelfOnly = true },
                "EntityStateMessage", "RespawnMessage", "RelinquishControlMessage", "HumanIdentityMessage",
                "AnimationEmoteMessage", "BlendShapeEmoteMessage", "BlendShapeEmoteResetMessage", "BlendShapeNuancedMessage",
                "ChatStatusMessage");

            // doors, switches, levers, valves, using an item
            Set(new Rule { Needs = Permission.Use },
                "InteractionMessage", "RequestInteractionToServer", "RequestUseItemToServer");

            // taking, putting, moving, using up items; machines that use up materials
            Set(new Rule { Needs = Permission.Containers },
                "MoveToSlotMessage", "SwapSlotsMessage", "MergeStackablesMessage", "UseStackableMessage",
                "SortContentsMessage", "FabricatorJobMessage", "FabricatorJobDeleteMessage", "SetRecipeMessage",
                "AddPrintingComponenetMessage");
            // dropping and throwing: the item counts, not where it lands; but not at all near claims (approach zone)
            Set(new Rule { Needs = Permission.Containers, IgnorePositions = true, ActorZone = true },
                "MoveToWorldMessage", "MoveToWorldPositionMessage");

            // tools on things and people (attacks on people in claims are blocked too)
            Set(new Rule { Needs = Permission.Build, ProtectPeople = true }, "AttackWithMessage");

            // group A: never sent by the game (manipulated clients only) – kick
            // (SyncTimeMessage: only the server sends it; from a client it would shift the server's clock)
            Set(new Rule { Cheat = Cheat.Never }, "SyncGameMode", "SyncCustomGrowSpeed", "ThingSpawnMessage", "SyncTimeMessage");

            // group B: cheat features of the normal game (F3 console "addgas"/"thing", creative menu) – blocked outside creative
            Set(new Rule { Cheat = Cheat.VanillaCheat }, "AddGasCommand", "SpawnDynamicThingMaxStackMessage", "SpawnSpawnDataMessage");

            // digging and filling terrain
            Set(new Rule { Needs = Permission.Mining, Buffer = MiningBuffer },
                "MineAsteroidMessage", "AsteroidVoxelMessage", "AsteroidVoxelPaintMessage");

            // everything else (building, tools, logic, renaming, painting, …) needs Build – the default
            return r;
        }
    }
}
