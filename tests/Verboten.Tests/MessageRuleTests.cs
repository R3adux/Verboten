using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Verboten.Core;
using Xunit;
using Xunit.Abstractions;

namespace Verboten.Tests
{
    public class MessageRuleTests
    {
        private readonly ITestOutputHelper _out;
        public MessageRuleTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Unknown_NeedsBuild()
        {
            var r = MessageRules.For("SomeMessageFromAFutureUpdate");
            Assert.Equal(MessageRules.Check.Permission, r.Check);
            Assert.Equal(Permission.Build, r.Needs);
            Assert.False(r.IgnorePositions);
        }

        [Theory]
        [InlineData("InteractionMessage", Permission.Use)]
        [InlineData("MoveToSlotMessage", Permission.Containers)]
        [InlineData("SwapSlotsMessage", Permission.Containers)]
        [InlineData("MineAsteroidMessage", Permission.Mining)]
        [InlineData("AttackWithMessage", Permission.Build)]
        [InlineData("CreateStructureMessage", Permission.Build)]
        [InlineData("ConstructionCreationMessage", Permission.Build)]
        [InlineData("DestroyThingRequest", Permission.Build)]
        public void KnownActions(string type, Permission needs)
        {
            var r = MessageRules.For(type);
            Assert.Equal(MessageRules.Check.Permission, r.Check);
            Assert.Equal(needs, r.Needs);
        }

        [Fact]
        public void Mining_HasBuffer_DroppingIgnoresPosition()
        {
            Assert.True(MessageRules.For("MineAsteroidMessage").Buffer > 0);
            Assert.True(MessageRules.For("MoveToWorldMessage").IgnorePositions);
        }

        [Theory]
        [InlineData("ChatMessage")]
        [InlineData("VerifyPlayer")]
        [InlineData("PingObjectMessage")]
        public void Harmless_AreSkipped(string type) => Assert.Equal(MessageRules.Check.Skip, MessageRules.For(type).Check);

        [Theory]
        [InlineData("EntityStateMessage")]       // could kill anyone
        [InlineData("RespawnMessage")]
        [InlineData("RelinquishControlMessage")] // could throw anyone out of their body
        [InlineData("HumanIdentityMessage")]
        [InlineData("AnimationEmoteMessage")]
        [InlineData("ChatStatusMessage")]
        public void AboutOthers_IsNotAllowed(string type) => Assert.True(MessageRules.For(type).SelfOnly);

        [Fact]
        public void ServerOnlyClock_FromAClient_IsACheat() =>
            Assert.Equal(MessageRules.Cheat.Never, MessageRules.For("SyncTimeMessage").Cheat);

        [Fact]
        public void Guests_CanUseRulesForUse_ButNotOthers()
        {
            var guest = Roles.Of(Role.Guest);
            Assert.Equal(Permission.Use, guest & MessageRules.For("InteractionMessage").Needs);
            Assert.NotEqual(MessageRules.For("MoveToSlotMessage").Needs, guest & MessageRules.For("MoveToSlotMessage").Needs);
            Assert.NotEqual(Permission.Build, guest & Permission.Build);
        }

        // ------------------------------------------------------------------ against the real game

        /// <summary>
        /// Every name in the rule table must still be a message type in the game – a typo or a renamed
        /// message would silently fall back to "Build" (too strict, e.g. guests could no longer open doors).
        /// Also prints game message types without an explicit rule (they need Build) for review.
        /// </summary>
        [SkippableFact]
        public void RealGame_RuleNamesExist()
        {
            var managed = GameApiTestsPaths.Managed;
            Skip.IfNot(File.Exists(Path.Combine(managed, "Assembly-CSharp.dll")), "Game not found");
            using var context = new MetadataLoadContext(new PathAssemblyResolver(Directory.GetFiles(managed, "*.dll")), "mscorlib");
            var asm = context.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));
            var processable = asm.GetType("Assets.Scripts.Networking.IMessageProcessable");

            var messageTypes = new HashSet<string>();
            foreach (var t in SafeTypes(asm))
                if (!t.IsAbstract && !t.IsInterface && processable.IsAssignableFrom(t)) messageTypes.Add(t.Name);

            var missing = MessageRules.ListedNames.Where(n => !messageTypes.Contains(n)).ToList();
            Assert.True(missing.Count == 0, "Rule names not found in the game: " + string.Join(", ", missing));

            var unlisted = messageTypes.Where(n => !MessageRules.IsListed(n)).OrderBy(n => n).ToList();
            _out.WriteLine($"{messageTypes.Count} message types, {unlisted.Count} use the default (Build): {string.Join(", ", unlisted)}");
        }

        private static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }
    }
}
