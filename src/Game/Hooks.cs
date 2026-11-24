using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Verboten.Game
{
    /// <summary>
    /// Every place where Verboten hooks into the game, in one list.
    /// Each hook is looked up and installed on its own, so a game update that renames one method
    /// disables only that feature and is reported clearly instead of breaking the whole mod.
    /// </summary>
    public static class Hooks
    {
        /// <summary>Game version Verboten was last checked against (see 'claim status').</summary>
        public const string TestedGameVersion = "0.2.6429.27802";

        public enum Kind
        {
            /// <summary>Convenience (saving, commands). Missing = feature off, everything else works.</summary>
            Feature,
            /// <summary>Protection. Missing = a hole in the protection; admins are warned.</summary>
            Protection,
        }

        public sealed class Hook
        {
            public string Name;      // shown to admins, e.g. "Save claims with the game"
            public Kind Kind;
            public string Target;    // "SaveHelper.Save" – for the log
            public Func<MethodBase> Find;
            /// <summary>For hooks that patch many methods with the same patch (see AddMany).</summary>
            public Func<IEnumerable<MethodBase>> FindAll;
            public string[] Required;
            public int Patched;
            public MethodInfo Prefix;
            public MethodInfo Postfix;
            public MethodInfo Transpiler;
            public MethodInfo Finalizer;

            public bool Installed;
            public string Error;
        }

        private static readonly List<Hook> All = new List<Hook>();

        public static IReadOnlyList<Hook> Status => All;
        public static bool AllInstalled => All.TrueForAll(h => h.Installed);

        /// <summary>True if the hook with this target was installed.</summary>
        public static bool IsInstalled(string target) => All.Exists(h => h.Target == target && h.Installed);

        /// <summary>
        /// Declare a hook. <paramref name="owner"/> is the class holding the patch methods; the
        /// other arguments are their names (null = none).
        /// </summary>
        public static void Add(Kind kind, string name, string target, Func<MethodBase> find, Type owner,
            string prefix = null, string postfix = null, string transpiler = null, string finalizer = null)
        {
            All.Add(new Hook
            {
                Kind = kind,
                Name = name,
                Target = target,
                Find = find,
                Prefix = prefix != null ? AccessTools.Method(owner, prefix) : null,
                Postfix = postfix != null ? AccessTools.Method(owner, postfix) : null,
                Transpiler = transpiler != null ? AccessTools.Method(owner, transpiler) : null,
                Finalizer = finalizer != null ? AccessTools.Method(owner, finalizer) : null,
            });
        }

        /// <summary>
        /// Declare one hook that patches many methods with the same prefix. It counts as installed only if
        /// every method named in <paramref name="required"/> ("Type.Method") was patched.
        /// </summary>
        public static void AddMany(Kind kind, string name, string target, Func<IEnumerable<MethodBase>> findAll,
            Type owner, string prefix, params string[] required)
        {
            All.Add(new Hook
            {
                Kind = kind,
                Name = name,
                Target = target,
                FindAll = findAll,
                Required = required,
                Prefix = AccessTools.Method(owner, prefix),
            });
        }

        private static void InstallMany(Harmony harmony, Hook hook)
        {
            var patched = new HashSet<string>();
            var failed = new List<string>();
            foreach (var method in hook.FindAll())
            {
                string id = $"{method.DeclaringType?.Name}.{method.Name}";
                try
                {
                    harmony.Patch(method, prefix: Wrap(hook.Prefix), postfix: null, transpiler: null, finalizer: null, ilmanipulator: null);
                    patched.Add(id);
                }
                catch (Exception ex)
                {
                    failed.Add($"{id}: {Describe(ex)}");
                }
            }
            hook.Patched = patched.Count;
            var missing = new List<string>();
            foreach (var r in hook.Required)
                if (!patched.Contains(r)) missing.Add(r);
            hook.Installed = missing.Count == 0 && patched.Count > 0;
            hook.Error = missing.Count > 0 ? "missing: " + string.Join(", ", missing) : null;
            if (failed.Count > 0)
                Plugin.Log.LogWarning($"{hook.Name}: {failed.Count} method(s) could not be patched: {string.Join("; ", failed)}");
        }

        public static void InstallAll(Harmony harmony)
        {
            foreach (var hook in All)
            {
                if (hook.Installed) continue;
                try
                {
                    if (hook.FindAll != null)
                    {
                        InstallMany(harmony, hook);
                        continue;
                    }
                    if (hook.Prefix == null && hook.Postfix == null && hook.Transpiler == null && hook.Finalizer == null)
                        throw new InvalidOperationException("no patch method (Verboten bug)");
                    var target = hook.Find();
                    if (target == null)
                        throw new MissingMethodException("game method not found – probably renamed by a game update");
                    harmony.Patch(target,
                        prefix: Wrap(hook.Prefix), postfix: Wrap(hook.Postfix),
                        transpiler: Wrap(hook.Transpiler), finalizer: Wrap(hook.Finalizer), ilmanipulator: null);
                    hook.Installed = true;
                    hook.Error = null;
                }
                catch (Exception ex)
                {
                    hook.Installed = false;
                    hook.Error = Describe(ex);
                }
            }
            LogSummary();
        }

        private static HarmonyMethod Wrap(MethodInfo method) => method != null ? new HarmonyMethod(method) : null;

        /// <summary>Short reason for the log; Harmony wraps our own exceptions, so unwrap them.</summary>
        private static string Describe(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
                if (e is MissingMethodException || e is PatternException) return e.Message;
            return ex.ToString();
        }

        /// <summary>Thrown by a transpiler when the game code no longer looks as expected.</summary>
        public sealed class PatternException : Exception
        {
            public PatternException(string message) : base(message) { }
        }

        public static IEnumerable<string> Report()
        {
            int ok = All.FindAll(h => h.Installed).Count;
            yield return $"Verboten {Plugin.Version}: {ok} of {All.Count} hooks active. Game {GameVersion()} (tested: {TestedGameVersion}).";
            foreach (var h in All)
            {
                if (!h.Installed)
                    yield return $"  MISSING{(h.Kind == Kind.Protection ? " (protection!)" : "")}: {h.Name} [{h.Target}] – {h.Error}";
                else if (h.FindAll != null)
                    yield return $"  {h.Name}: {h.Patched} game methods watched.";
            }
        }

        private static readonly List<string> Problems = new List<string>();
        private static bool _shownThisWorld;

        /// <summary>A runtime problem (e.g. unreadable claims file) to show the host at world start.</summary>
        public static void ReportProblem(string message)
        {
            if (!Problems.Contains(message)) Problems.Add(message);
        }

        /// <summary>Show missing hooks and problems once per world, to the host (console + popup).</summary>
        public static void OnWorldStarted()
        {
            if (_shownThisWorld) return;
            _shownThisWorld = true;
            var warnings = Warnings();
            if (warnings.Count == 0) return;
            foreach (var w in warnings) Identity.PrintLocal(w);
            if (!Assets.Scripts.GameManager.IsBatchMode)
            {
                try { Assets.Scripts.Networking.AnnounceMessage.Display(string.Join("\n\n", warnings)); }
                catch (Exception ex) { Plugin.Log.LogWarning($"Could not show the warning window: {ex.Message}"); }
            }
        }

        /// <summary>Called when the player is back in the menu.</summary>
        public static void OnWorldLeft()
        {
            _shownThisWorld = false;
            Problems.Clear();
        }

        /// <summary>Warnings worth showing to the host when a world starts. Empty when all is well.</summary>
        public static List<string> Warnings()
        {
            var list = new List<string>(Problems);
            foreach (var h in All)
                if (!h.Installed)
                    list.Add(h.Kind == Kind.Protection
                        ? L.T("host.protection_missing", ("name", h.Name))
                        : L.T("host.feature_missing", ("name", h.Name)));
            return list;
        }

        public static string GameVersion()
        {
            try { return Assets.Scripts.GameManager.GetGameVersion(); }
            catch
            {
                try { return Application.version; }
                catch { return "?"; }
            }
        }

        private static void LogSummary()
        {
            foreach (var line in Report())
            {
                if (line.StartsWith("  MISSING")) Plugin.Log.LogError(line);
                else Plugin.Log.LogInfo(line);
            }
            if (GameVersion() != TestedGameVersion)
                Plugin.Log.LogWarning($"Game version {GameVersion()} differs from the tested {TestedGameVersion}. " +
                                      "All hooks above were checked; if any are missing, update Verboten.");
        }
    }
}
