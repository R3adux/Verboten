using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Serialization;
using HarmonyLib;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Stores the claims next to every save file, in a sub folder:
    ///   &lt;folder of the save&gt;/verboten/&lt;save file name&gt;.xml
    /// Loading an older save therefore also loads the matching claims.
    ///
    /// Why a sub folder: the game counts *all* files in the autosave/quicksave folders to decide when
    /// to delete old saves (SaveHelper.RollSaveFiles) and expects exactly one "*.save" in a station
    /// folder. Files next to the saves would make it delete saves too early.
    /// </summary>
    public static class Persistence
    {
        public const string FolderName = "verboten";
        public const string Extension = ".xml";

        private static GameState _lastState;
        private static string _pendingLoadPath;

        public static void Register()
        {
            var t = typeof(Persistence);
            Hooks.Add(Hooks.Kind.Feature, "Save claims with the game", "SaveHelper.Save",
                () => AccessTools.Method(typeof(SaveHelper), "Save",
                    new[] { typeof(DirectoryInfo), typeof(string), typeof(bool), typeof(CancellationToken) }),
                t, prefix: nameof(SavePrefix));
            Hooks.Add(Hooks.Kind.Feature, "Keep claims on 'save as'", "SaveHelper.CopyToHeadSave",
                () => AccessTools.Method(typeof(SaveHelper), "CopyToHeadSave", new[] { typeof(string), typeof(FileInfo) }),
                t, prefix: nameof(CopyToHeadSavePrefix));
            Hooks.Add(Hooks.Kind.Feature, "Keep claims when a station is renamed", "SaveHelper.RenameStation",
                () => AccessTools.Method(typeof(SaveHelper), "RenameStation", new[] { typeof(string), typeof(string) }),
                t, postfix: nameof(RenameStationPostfix));
            Hooks.Add(Hooks.Kind.Feature, "Load claims with the game", "LoadHelper.LoadGame",
                () => AccessTools.Method(typeof(LoadHelper), "LoadGame", new[] { typeof(string), typeof(string) }),
                t, prefix: nameof(LoadGamePrefix));

            GameManager.OnGameStateChange += OnGameStateChange;
        }

        /// <summary>Where the claims of a save file live.</summary>
        public static string SidecarFor(string saveFilePath)
        {
            string dir = Path.GetDirectoryName(saveFilePath) ?? string.Empty;
            return Path.Combine(Path.Combine(dir, FolderName), Path.GetFileName(saveFilePath) + Extension);
        }

        // ---------- saving ----------

        // Patch parameters are positional (__0, __1 …) so a renamed game parameter cannot break the hook.
        private static void SavePrefix(DirectoryInfo __0, string __1)
        {
            DirectoryInfo saveDirectory = __0;
            string saveFileName = __1;
            try
            {
                // Clients never save worlds; RunSimulation == !NetworkManager.IsClient (host or single player).
                if (!GameManager.RunSimulation || saveDirectory == null || string.IsNullOrEmpty(saveFileName)) return;
                string sidecar = SidecarFor(Path.Combine(saveDirectory.FullName, saveFileName));
                Flares.RemoveAll(); // the corner flares of new claims are only for show, never saved
                var claims = Plugin.Store.CopyForSave();
                if (claims.Count == 0 && !File.Exists(sidecar)) return;
                WriteAtomic(sidecar, new ClaimFile
                {
                    ServerMinutes = World.Minute,
                    Claims = claims,
                    Players = Plugin.Players.CopyForSave(),
                    Robots = Robots.CopyForSave(),
                });
                // This runs BEFORE the game writes the save (the save is asynchronous): a new save file (auto, quick,
                // new, "save as") does not exist yet, so its claims file must be kept explicitly.
                RemoveOrphans(Path.GetDirectoryName(sidecar), saveDirectory.FullName, keep: sidecar);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to save claims: {ex}");
            }
        }

        private static void CopyToHeadSavePrefix(string __0, FileInfo __1)
        {
            string manualSaveFullPath = __0;
            FileInfo headSaveFileInfo = __1;
            try
            {
                if (string.IsNullOrEmpty(manualSaveFullPath) || headSaveFileInfo == null) return;
                string source = SidecarFor(manualSaveFullPath);
                string target = SidecarFor(headSaveFileInfo.FullName);
                if (File.Exists(source))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(source, target, overwrite: true);
                }
                else if (File.Exists(target))
                {
                    File.Delete(target); // the manual save had no claims
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to copy claims to the head save: {ex}");
            }
        }

        private static void RenameStationPostfix(bool __result, string __1)
        {
            string newStationName = __1;
            try
            {
                if (!__result || string.IsNullOrEmpty(newStationName)) return;
                var stationDir = Path.Combine(StationSaveUtils.GetSavePathSavesSubDir().FullName, newStationName);
                string newHead = Path.Combine(stationDir, newStationName + SaveLoadConstants.SaveFileExtension);
                string newSidecar = SidecarFor(newHead);
                var folder = new DirectoryInfo(Path.GetDirectoryName(newSidecar));
                if (File.Exists(newSidecar) || !folder.Exists) return;
                // The game renamed the head save; move its now orphaned claims file along.
                foreach (var file in folder.GetFiles("*" + Extension))
                {
                    string save = Path.Combine(stationDir, file.Name.Substring(0, file.Name.Length - Extension.Length));
                    if (File.Exists(save)) continue;
                    file.MoveTo(newSidecar);
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to rename the claims file: {ex}");
            }
        }

        private static void WriteAtomic(string target, ClaimFile content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string tmp = target + ".tmp";
            using (var stream = File.Create(tmp))
                ClaimFile.Write(stream, content);
            if (File.Exists(target)) File.Replace(tmp, target, null);
            else File.Move(tmp, target);
        }

        /// <summary>The game deletes old auto/quick saves; delete their claims files too (never <paramref name="keep"/>).</summary>
        private static void RemoveOrphans(string sidecarFolder, string saveFolder, string keep)
        {
            try
            {
                foreach (var file in new DirectoryInfo(sidecarFolder).GetFiles("*" + Extension))
                {
                    if (string.Equals(file.FullName, Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) continue;
                    string save = Path.Combine(saveFolder, file.Name.Substring(0, file.Name.Length - Extension.Length));
                    if (!File.Exists(save)) file.Delete();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not clean up old claims files: {ex.Message}");
            }
        }

        // ---------- loading ----------

        private static void LoadGamePrefix(string __0)
        {
            // Load synchronously before the world starts, so there is no moment without protection.
            // Remember the path: if an asynchronous "leave game" clean-up sets the state to None after
            // this point, the claims are loaded again when the state switches to Loading.
            _pendingLoadPath = __0;
            LoadFrom(__0);
        }

        private static void LoadFrom(string path)
        {
            string sidecar = string.IsNullOrEmpty(path) ? null : SidecarFor(path);
            try
            {
                if (sidecar == null || !File.Exists(sidecar))
                {
                    ClearAll();
                    Plugin.Log.LogInfo("No claims file for this save; starting without claims.");
                    return;
                }
                ClaimFile file;
                using (var stream = File.OpenRead(sidecar))
                    file = ClaimFile.Read(stream);
                Plugin.Store.Load(file.Claims);
                Plugin.Players.Load(file.Players);
                Robots.Load(file.Robots);
                World.Minute = file.ServerMinutes;
                Plugin.Log.LogInfo($"{Plugin.Store.Index.Count} claim(s) loaded from {sidecar}");
            }
            catch (Exception ex)
            {
                // Never let the next save overwrite a file we could not read: keep a copy.
                ClearAll();
                string kept = KeepBrokenFile(sidecar);
                Plugin.Log.LogError($"Failed to load claims from '{sidecar}'. Starting WITHOUT claims. " +
                                    $"The file was kept as '{kept}'. Error: {ex}");
                Hooks.ReportProblem(L.T("host.claims_unreadable"));
            }
        }

        private static void ClearAll()
        {
            Plugin.Store.Clear();
            Plugin.Players.Clear();
            Robots.Reset();
            World.Minute = 0;
        }

        private static string KeepBrokenFile(string file)
        {
            try
            {
                if (file == null || !File.Exists(file)) return "(none)";
                string kept = file + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(file, kept, overwrite: true);
                return kept;
            }
            catch
            {
                return "(could not copy)";
            }
        }

        private static void OnGameStateChange()
        {
            try
            {
                var state = GameManager.GameState;
                if (state == _lastState) return;
                _lastState = state;

                if (state == GameState.None)
                {
                    // back in the menu (world left, also before a new game is created)
                    ClearAll();
                    SenderContext.Reset();
                    Anchor.OnWorldLeft();
                    Trespass.Reset();
                    BorderDisplay.Reset();
                    Upkeep.Reset();
                    BuildGuide.Reset();
                    RemoteChat.Reset();
                    Pins.Reset();
                    Flares.Reset();
                    Incidents.Reset();
                    Protection.Reset();
                    Markers.Reset();
                    Hooks.OnWorldLeft();
                }
                else if (state == GameState.Joining)
                {
                    // we are a client of someone else's server
                    _pendingLoadPath = null;
                    ClearAll();
                }
                else if (state == GameState.Loading && _pendingLoadPath != null)
                {
                    // XmlSaveLoad.LoadWorld sets Loading; reload in case a late clean-up cleared the store
                    LoadFrom(_pendingLoadPath);
                }
                else if (state == GameState.Running || state == GameState.Paused)
                {
                    _pendingLoadPath = null;
                    if (GameManager.RunSimulation)
                    {
                        Anchor.ValidateAfterLoad();
                        Hooks.OnWorldStarted();
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error handling game state change: {ex}");
            }
        }
    }
}
