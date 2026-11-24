using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Verboten.Tests
{
    /// <summary>
    /// Checks against the INSTALLED game (../../../Stationeers) that every game method Verboten hooks
    /// still exists with the expected parameters. Run after every game update: a failure here names
    /// exactly what changed. Skipped automatically if the game is not found.
    /// Keep this list in sync with the Hooks.Add(...) calls in src/Game.
    /// </summary>
    public class GameApiTests
    {
        private static readonly string Managed = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Stationeers", "rocketstation_Data", "Managed"));

        public static TheoryData<string, string, string[]> Hooked => new TheoryData<string, string, string[]>
        {
            // type, method, parameter types (null = any overload with that name)
            { "Assets.Scripts.Serialization.SaveHelper", "Save", new[] { "DirectoryInfo", "String", "Boolean", "CancellationToken" } },
            { "Assets.Scripts.Serialization.SaveHelper", "CopyToHeadSave", new[] { "String", "FileInfo" } },
            { "Assets.Scripts.Serialization.SaveHelper", "RenameStation", new[] { "String", "String" } },
            { "Assets.Scripts.Serialization.LoadHelper", "LoadGame", new[] { "String", "String" } },
            { "Assets.Scripts.Inventory.InventoryManager", "OnChatMessage", null },
            // sender check (SenderContext)
            { "Assets.Scripts.Networking.NetworkManager", "ReceiveEvents", new string[0] },
            { "Assets.Scripts.Networking.NetworkManager", "HandleGeneralTraffic", new[] { "Int64", "Int32" } },
            { "Assets.Scripts.Networking.NetworkManager", "PlayerConnected", new[] { "Int64", "ConnectionMethod" } },
            { "Assets.Scripts.Networking.NetworkManager", "PlayerDisconnected", new[] { "Int64" } },
            { "Assets.Scripts.NetworkServer", "ClientDisconnected", new[] { "Int64" } },
            { "Assets.Scripts.NetworkServer", "VerifyConnection", new[] { "Int64", "VerifyPlayer" } },
            { "Assets.Scripts.Networking.DeferredMessageQueue", "Drain", new string[0] },
            // remote chat
            { "Assets.Scripts.Networking.ChatMessage", "Process", new[] { "Int64" } },
            // protection gate (required ones) and claim anchor
            { "Assets.Scripts.Networking.InteractionMessage", "Process", new[] { "Int64" } },
            { "Assets.Scripts.Networking.AttackWithMessage", "Process", new[] { "Int64" } },
            { "Assets.Scripts.Networking.CreateStructureMessage", "Process", new[] { "Int64" } },
            { "Assets.Scripts.Networking.ConstructionCreationMessage", "Process", new[] { "Int64" } },
            { "Assets.Scripts.Networking.MoveToSlotMessage", "Process", new[] { "Int64" } },
            { "Assets.Scripts.Networking.SwapSlotsMessage", "Process", new[] { "Int64" } },
            { "Assets.Scripts.Networking.MineAsteroidMessage", "Process", new[] { "Int64" } },
            { "Assets.Scripts.Networking.DestroyThingRequest", "Process", new[] { "Int64" } },
            { "Assets.Scripts.Objects.Structure", "SetStructureData", new[] { "Quaternion", "UInt64", "Grid3", "Int32" } },
            { "OnServer", "Destroy", new[] { "Thing" } },
            { "Assets.Scripts.GridController", "LocalToWorld", new[] { "Grid3" } },
            { "Assets.Scripts.Networking.AnnounceMessage", "Display", new[] { "String" } },
            // explosion damage
            { "Explosion", "DamageThing", new[] { "Thing", "Vector3", "Single" } },
            { "Explosion", "Explode", new[] { "Single", "Vector3", "Single", "Single", "Boolean" } },
            // border display
            { "WorldManager", "Spark", new[] { "Vector3", "Int32", "Boolean" } },
            // round 2: robots, postponed messages, safe points, landing zones, kicking
            { "TerrainSystem.Vein", "TryMineServer", new[] { "Vector3Int", "Ore&", "Vector3" } },
            { "Assets.Scripts.Networking.DeferredMessageQueue", "DeferUntilExists", null },
            { "Assets.Scripts.Objects.SpawnPoint", "GetSafePoint", new[] { "Vector3", "Vector3", "Boolean" } },
            { "StartLocationData", "GetSpawnRadius", new string[0] },
            { "StartLocationData", "WorldPosition", new string[0] },
            { "Assets.Scripts.Client", "Disconnect", new string[0] },
            { "Reagents.ReagentMixture", "Subtract", new[] { "Reagent" } },
            // tracker beacons for abandoned bases
            { "Assets.Scripts.Objects.Constructor", "SpawnConstruct", new[] { "CreateStructureInstance" } },
            { "Assets.Scripts.GridController", "WorldToLocal", new[] { "Vector3" } },
            { "OnServer", "SetCustomName", new[] { "Thing", "String" } },
        };

        [SkippableTheory]
        [MemberData(nameof(Hooked))]
        public void HookTargetExists(string typeName, string method, string[] parameters)
        {
            using var context = Load();
            var type = context.Assembly.GetType(typeName);
            Assert.True(type != null, $"Type {typeName} no longer exists in the game.");

            var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name == method).ToList();
            Assert.True(candidates.Count > 0, $"{typeName}.{method} no longer exists in the game.");
            if (parameters == null) return;

            bool match = candidates.Any(m => m.GetParameters().Select(p => p.ParameterType.Name).SequenceEqual(parameters));
            Assert.True(match, $"{typeName}.{method}({string.Join(", ", parameters)}) changed. Now: " +
                string.Join(" | ", candidates.Select(m => "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")")));
        }

        /// <summary>Fields and properties the mod reads directly (a rename would silently disable a check).</summary>
        public static TheoryData<string, string, string> Members => new TheoryData<string, string, string>
        {
            // type, member, expected type name
            { "Assets.Scripts.Networking.NetworkManager", "Instance", "NetworkManager" },
            { "Assets.Scripts.Networking.NetworkManager", "rakNet", "RakPeerInstance" },
            { "Assets.Scripts.Networking.NetworkManager", "NetworkRole", "NetworkRole" },
            { "Assets.Scripts.Networking.NetworkMessages+VerifyPlayer", "OwnerConnectionId", "Int64" },
            { "Assets.Scripts.Networking.NetworkMessages+VerifyPlayer", "ClientId", "UInt64" },
            { "Assets.Scripts.Networking.NetworkMessages+VerifyPlayer", "ClientConnectionMethod", "ConnectionMethod" },
            { "Assets.Scripts.Networking.NetworkMessages+VerifyPlayer", "Name", "String" },
            { "Assets.Scripts.Client", "ClientId", "UInt64" },
            { "Assets.Scripts.Client", "connectionId", "Int64" },
            { "Assets.Scripts.Client", "IsHost", "Boolean" },
            { "Assets.Scripts.Networking.ChatMessage", "HumanId", "Int64" },
            { "Assets.Scripts.Networking.ChatMessage", "DisplayName", "String" },
            { "Assets.Scripts.Networking.ChatMessage", "ChatText", "String" },
            { "Assets.Scripts.Networking.AnnounceMessage", "AnnounceText", "String" },
            { "Assets.Scripts.GridController", "World", "GridController" },
            { "Assets.Scripts.Networking.CreateStructureMessage", "CreatorSteamId", "UInt64" },
            { "Assets.Scripts.Networking.ConstructionCreationMessage", "OwnerClientId", "UInt64" },
            { "Assets.Scripts.Objects.Thing", "OwnerClientId", "UInt64" },
            { "Assets.Scripts.Objects.Thing", "RootParentHuman", "Human" },
            { "Assets.Scripts.Objects.Entity", "State", "EntityState" },
            { "Assets.Scripts.Objects.RobotMining", "AllRobots", "List`1" },
            { "Assets.Scripts.Objects.Thing", "ReagentMixture", "ReagentMixture" },
            { "Reagents.ReagentMixture", "Steel", "Steel" },
            { "WorldManager", "GameMode", "GameMode" },
            { "RoundRobinStartLocationData", "Locations", "List`1" },
            { "Assets.Scripts.Atmospherics.Atmosphere", "WorldPosition", "Vector3" },
            { "Assets.Scripts.Networks.CableNetwork", "CableList", "List`1" },
            { "Networks.StructureNetwork", "StructureList", "List`1" },
            { "Referencable", "Referencables", "Dictionary`2" },
            { "Assets.Scripts.Objects.Structure", "AllStructurePrefabs", "List`1" },
            { "Assets.Scripts.Networking.ThingRenameMessage", "ThingId", "Int64" },
            { "Assets.Scripts.Networking.ThingRenameMessage", "ThingName", "String" },
            { "Assets.Scripts.Networking.EmitSparksMessage", "Position", "Vector3" },
            { "Assets.Scripts.Networking.EmitSparksMessage", "Quantity", "Int32" },
            { "Assets.Scripts.Networking.EmitSparksMessage", "IsGravity", "Boolean" },
            { "Assets.Scripts.Objects.Entities.Human", "AllHumans", "List`1" },
            // "only about yourself" messages: the ids Verboten compares with the sender
            { "Assets.Scripts.Networking.EntityStateMessage", "ThingId", "Int64" },
            { "Assets.Scripts.Networking.RespawnMessage", "HumanId", "UInt64" },
            { "Assets.Scripts.Networking.RelinquishControlMessage", "HumanId", "UInt64" },
            { "Assets.Scripts.Networking.HumanIdentityMessage", "HumanId", "Int64" },
            { "Assets.Scripts.Networking.AnimationEmoteMessage", "HumanNetId", "Int64" },
            { "Assets.Scripts.Networking.ChatStatusMessage", "HumanId", "Int64" },
            // admins on dedicated servers: 'serverrun claim …'
            { "Util.Commands.ServerRunCommandMessage", "Command", "String" },
            { "Assets.Scripts.Objects.Entities.Human", "LocalHuman", "Human" },
        };

        /// <summary>
        /// Explosion rule: built devices (Structure) may damage their own claim, portable things may not.
        /// If the game moves one of these to the other side, the behaviour would change silently.
        /// </summary>
        [SkippableTheory]
        [InlineData("Assets.Scripts.Objects.Pipes.FurnaceBase", true)]
        [InlineData("Assets.Scripts.Objects.Electrical.StirlingEngine", true)]
        [InlineData("Assets.Scripts.Objects.Electrical.CircuitHousing", true)]
        [InlineData("Assets.Scripts.Objects.Weapons.Grenade", false)]
        [InlineData("Assets.Scripts.Objects.Items.Dynamite", false)]
        [InlineData("Assets.Scripts.Objects.Items.ItemExplosive", false)]
        [InlineData("Assets.Scripts.Objects.Items.GasCanister", false)]
        [InlineData("Assets.Scripts.Objects.DynamicGasCanister", false)]
        public void ExplosionSource_IsDeviceOrNot(string typeName, bool isDevice)
        {
            using var context = Load();
            var type = context.Assembly.GetType(typeName);
            var structure = context.Assembly.GetType("Assets.Scripts.Objects.Structure");
            Assert.True(type != null, $"Type {typeName} no longer exists in the game.");
            Assert.Equal(isDevice, type.IsSubclassOf(structure));
        }

        /// <summary>Anchor types must still exist under these names.</summary>
        [SkippableTheory]
        [InlineData("Assets.Scripts.Objects.Electrical.Autolathe")]
        [InlineData("Assets.Scripts.Objects.Flag")]
        [InlineData("Assets.Scripts.Objects.Entity")]
        [InlineData("Assets.Scripts.Objects.RobotMining")]
        [InlineData("Reagents.Steel")]
        [InlineData("Networks.INetworkedStructure")]
        [InlineData("Assets.Scripts.Objects.Electrical.FixedBeacon")]
        public void TypeExists(string typeName)
        {
            using var context = Load();
            Assert.True(context.Assembly.GetType(typeName) != null, $"Type {typeName} no longer exists in the game.");
        }

        [SkippableTheory]
        [MemberData(nameof(Members))]
        public void MemberExists(string typeName, string member, string expectedType)
        {
            using var context = Load();
            var type = context.Assembly.GetType(typeName);
            Assert.True(type != null, $"Type {typeName} no longer exists in the game.");
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var field = type.GetField(member, all);
            var property = type.GetProperty(member, all);
            string actual = field?.FieldType.Name ?? property?.PropertyType.Name;
            Assert.True(actual != null, $"{typeName}.{member} no longer exists in the game.");
            Assert.Equal(expectedType, actual);
        }

        [SkippableFact]
        public void FirstChatParameterIsTheText()
        {
            using var context = Load();
            var m = context.Assembly.GetType("Assets.Scripts.Inventory.InventoryManager")
                .GetMethod("OnChatMessage", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
            Assert.NotNull(m);
            Assert.Equal("String", m.GetParameters()[0].ParameterType.Name);
        }

        private sealed class GameContext : IDisposable
        {
            public MetadataLoadContext Context;
            public Assembly Assembly;
            public void Dispose() => Context.Dispose();
        }

        private static GameContext Load()
        {
            Skip.IfNot(File.Exists(Path.Combine(Managed, "Assembly-CSharp.dll")), $"Game not found at {Managed}");
            var paths = Directory.GetFiles(Managed, "*.dll");
            var context = new MetadataLoadContext(new PathAssemblyResolver(paths), "mscorlib");
            return new GameContext { Context = context, Assembly = context.LoadFromAssemblyPath(Path.Combine(Managed, "Assembly-CSharp.dll")) };
        }
    }
}
