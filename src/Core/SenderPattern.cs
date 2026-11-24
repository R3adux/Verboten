using System.Collections.Generic;

namespace Verboten.Core
{
    /// <summary>One IL instruction, reduced to what the sender pattern needs. Built from Harmony (in game)
    /// or Mono.Cecil (in tests), so both check the very same logic.</summary>
    public struct IlOp
    {
        /// <summary>ECMA opcode name, e.g. "call", "ldloc.s", "ldloc.0".</summary>
        public string OpCode;
        /// <summary>For calls: "DeclaringType.Method" (short type name). Otherwise null.</summary>
        public string CallTarget;
        /// <summary>For ldloc*: the local variable index. Otherwise -1.</summary>
        public int Local;

        public bool IsCall => OpCode == "call" || OpCode == "callvirt";
        public bool IsLdloc => OpCode != null && OpCode.StartsWith("ldloc") && !OpCode.StartsWith("ldloca");

        public override string ToString() => CallTarget != null ? $"{OpCode} {CallTarget}" : Local >= 0 ? $"{OpCode} V_{Local}" : OpCode;
    }

    /// <summary>
    /// Finds, in NetworkManager.ReceiveEvents, the local variable that holds the sender of the packet
    /// (Steam P2P SteamID or RakNet GUID) and the call(s) that hand the packet to the message handlers.
    ///
    /// The game reads the sender into a local, uses it for PlayerConnected(sender, method) and
    /// PlayerDisconnected(sender), but then calls HandleGeneralTraffic(_hostId, size) with its own
    /// field instead – so the message handlers never learn who sent the message. Verboten passes the
    /// local along. Every use must agree on the same local, otherwise nothing is changed (fail closed).
    /// </summary>
    public static class SenderPattern
    {
        public const string HandleTraffic = "NetworkManager.HandleGeneralTraffic";
        public const string PlayerConnected = "NetworkManager.PlayerConnected";
        public const string PlayerDisconnected = "NetworkManager.PlayerDisconnected";

        public sealed class Result
        {
            public int SenderLocal = -1;
            /// <summary>Index (into the op list) of the ldloc that loads the sender; cloned when patching.</summary>
            public int SenderLoadIndex = -1;
            public readonly List<int> HandleCalls = new List<int>();
            public string Error;
            public bool Ok => Error == null;
        }

        public static Result Find(IReadOnlyList<IlOp> ops)
        {
            var r = new Result();
            var candidates = new List<int>(); // op indexes of ldloc that load the sender

            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if (!op.IsCall) continue;
                if (op.CallTarget == HandleTraffic) r.HandleCalls.Add(i);
                // PlayerDisconnected(sender)            -> ldloc sender; call
                else if (op.CallTarget == PlayerDisconnected && i >= 1 && ops[i - 1].IsLdloc) candidates.Add(i - 1);
                // PlayerConnected(sender, method)       -> ldloc sender; ldc.i4 method; call
                else if (op.CallTarget == PlayerConnected && i >= 2 && ops[i - 2].IsLdloc && ops[i - 1].OpCode.StartsWith("ldc.i4"))
                    candidates.Add(i - 2);
            }

            if (r.HandleCalls.Count == 0) { r.Error = "no call to HandleGeneralTraffic found"; return r; }
            if (candidates.Count < 2) { r.Error = $"sender variable not found (only {candidates.Count} use(s))"; return r; }

            int local = ops[candidates[0]].Local;
            foreach (int c in candidates)
                if (ops[c].Local != local) { r.Error = "the game uses different variables for the sender"; return r; }

            r.SenderLocal = local;
            r.SenderLoadIndex = candidates[0];
            return r;
        }

        /// <summary>Local index of ldloc.0..3 / ldloc.s / ldloc, or -1.</summary>
        public static int LocalIndexOf(string opCode, object operandIndex)
        {
            switch (opCode)
            {
                case "ldloc.0": return 0;
                case "ldloc.1": return 1;
                case "ldloc.2": return 2;
                case "ldloc.3": return 3;
                case "ldloc.s":
                case "ldloc":
                    return operandIndex is int i ? i : -1;
                default: return -1;
            }
        }
    }
}
