using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Verboten.Core;
using Xunit;

namespace Verboten.Tests
{
    public class SenderPatternTests
    {
        private static IlOp Call(string target) => new IlOp { OpCode = "call", CallTarget = target, Local = -1 };
        private static IlOp Ld(int local) => new IlOp { OpCode = local <= 3 ? $"ldloc.{local}" : "ldloc.s", Local = local };
        private static IlOp Op(string name) => new IlOp { OpCode = name, Local = -1 };

        private static List<IlOp> Typical(int senderLocal = 7) => new List<IlOp>
        {
            Ld(senderLocal), Op("ldc.i4.1"), Call(SenderPattern.PlayerConnected),
            Op("ldsfld"), Ld(5), Call(SenderPattern.HandleTraffic),
            Ld(senderLocal), Call(SenderPattern.PlayerDisconnected),
            Ld(senderLocal), Op("ldc.i4.0"), Call(SenderPattern.PlayerConnected),
        };

        [Fact]
        public void Finds_SenderAndCall()
        {
            var r = SenderPattern.Find(Typical());
            Assert.True(r.Ok, r.Error);
            Assert.Equal(7, r.SenderLocal);
            Assert.Equal(new[] { 5 }, r.HandleCalls);
        }

        [Fact]
        public void Fails_WhenVariablesDisagree()
        {
            var ops = Typical();
            ops[6] = Ld(2); // PlayerDisconnected uses another local
            Assert.False(SenderPattern.Find(ops).Ok);
        }

        [Fact]
        public void Fails_WithoutHandleCall()
        {
            var ops = Typical();
            ops[5] = Call("NetworkManager.SomethingElse");
            Assert.False(SenderPattern.Find(ops).Ok);
        }

        [Fact]
        public void Fails_WithTooFewUses()
        {
            var ops = new List<IlOp> { Op("ldsfld"), Ld(5), Call(SenderPattern.HandleTraffic), Ld(7), Call(SenderPattern.PlayerDisconnected) };
            Assert.False(SenderPattern.Find(ops).Ok);
        }

        [Theory]
        [InlineData("ldloc.0", null, 0)]
        [InlineData("ldloc.3", null, 3)]
        [InlineData("ldloc.s", 12, 12)]
        [InlineData("ldloc", 300, 300)]
        [InlineData("ldloca.s", 4, -1)]
        public void LocalIndexOf(string op, object operand, int expected) =>
            Assert.Equal(expected, SenderPattern.LocalIndexOf(op, operand));

        // ------------------------------------------------------------------ against the real game

        /// <summary>The installed game's ReceiveEvents must match the pattern the mod patches.</summary>
        [SkippableFact]
        public void RealGame_ReceiveEventsMatchesPattern()
        {
            string dll = Path.Combine(GameApiTestsPaths.Managed, "Assembly-CSharp.dll");
            Skip.IfNot(File.Exists(dll), "Game not found");

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(GameApiTestsPaths.Managed);
            using var module = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = resolver });
            var method = module.GetType("Assets.Scripts.Networking.NetworkManager").Methods.Single(m => m.Name == "ReceiveEvents");

            var ops = method.Body.Instructions.Select(ToOp).ToList();
            var r = SenderPattern.Find(ops);
            Assert.True(r.Ok, r.Error);
            Assert.Equal("System.Int64", method.Body.Variables[r.SenderLocal].VariableType.FullName);
            Assert.NotEmpty(r.HandleCalls);

            // The mod inserts an instruction right before each call; no jump may land on the call itself.
            var instructions = method.Body.Instructions;
            foreach (int index in r.HandleCalls)
            {
                var call = instructions[index];
                bool isJumpTarget = instructions.Any(i =>
                    i.Operand == call || (i.Operand is Instruction[] targets && targets.Contains(call)));
                Assert.False(isJumpTarget, "A jump lands on the HandleGeneralTraffic call.");
                Assert.False(method.Body.ExceptionHandlers.Any(h => h.HandlerStart == call || h.TryStart == call || h.HandlerEnd == call || h.TryEnd == call),
                    "An exception block starts or ends at the HandleGeneralTraffic call.");
            }
        }

        private static IlOp ToOp(Instruction i)
        {
            string name = i.OpCode.Name;
            object index = i.Operand is VariableDefinition v ? v.Index : (object)null;
            return new IlOp
            {
                OpCode = name,
                CallTarget = i.Operand is MethodReference m ? $"{m.DeclaringType.Name}.{m.Name}" : null,
                Local = name.StartsWith("ldloc") && !name.StartsWith("ldloca") ? SenderPattern.LocalIndexOf(name, index) : -1,
            };
        }
    }

    internal static class GameApiTestsPaths
    {
        public static readonly string Managed = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "Stationeers", "rocketstation_Data", "Managed"));
    }
}
