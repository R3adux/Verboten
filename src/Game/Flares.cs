using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Motherboards;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Burning road flares that mark borders for some seconds (setting BorderMarkers = Flares):
    ///  - grey at the corners of the viewer's own claim (grey light = dimmed white),
    ///  - khaki at the corners of other claims (a dark, olive green),
    ///  - 12 red ones around the landing zone (north, east, south, west and evenly in between).
    /// Flares are the game's own items (it drops a burning one at a new player's landing site the same way), so
    /// everyone nearby sees them. Each spot has at most one flare: when someone else asks again, the flare there
    /// is replaced in the colour of THEIR view and burns another 30 s. Nobody can pick them up or use them; they are
    /// removed when the time is up and before every save.
    /// </summary>
    public static class Flares
    {
        private const string Prefab = "ItemRoadFlare";
        public const int LandingZoneFlares = 12;
        private const float Height = 1.5f; // floating above the ground
        private const int MaxPerArea = 32;

        private sealed class Lit
        {
            public long Id;
            public ColorType Color;
            public float Until;
        }

        private static readonly Dictionary<string, Lit> Spots = new Dictionary<string, Lit>(); // spot key → flare
        private static readonly HashSet<long> Ids = new HashSet<long>();
        private static bool _paletteLogged;

        public static bool Enabled => Plugin.Settings.BorderMarkers.Value == BorderMarker.Flares;

        public static bool IsFlare(long referenceId) => referenceId != 0 && Ids.Contains(referenceId);

        /// <summary>
        /// Flares at the outer corners of a claim – or of several claims of one owner that touch (they look like one
        /// area: an L of two claims gets 6 flares) – in the colour of the viewer's relation to it.
        /// </summary>
        public static void AtCorners(IList<Claim> group, bool own, float seconds)
        {
            if (group == null || group.Count == 0) return;
            var color = own ? ColorType.Gray : ColorType.Khaki;
            var parts = new List<Box>();
            foreach (var c in group) parts.AddRange(c.Parts());
            var corners = ClaimGeometry.OutlineCorners(parts);
            // a very jagged shape could have dozens of corners: at most MaxPerArea flares, spread over all of them
            int step = Math.Max(1, (corners.Count + MaxPerArea - 1) / MaxPerArea);
            for (int k = 0; k < corners.Count; k += step)
            {
                var (x, z) = corners[k];
                Place($"c:{Mathf.RoundToInt(x * 10f)}:{Mathf.RoundToInt(z * 10f)}", x, z, group[0].AnchorY, color, seconds);
            }
        }

        public static void AtCorners(Claim claim, bool own, float seconds)
        {
            if (claim != null) AtCorners(new List<Claim> { claim }, own, seconds);
        }

        /// <summary>12 flares on the edge of a landing zone: yellow at the four compass points, red evenly in between.</summary>
        public static void AroundLandingZone(LandingZone zone, int number, float y, float seconds)
        {
            float r = zone.ProtectedRadius;
            for (int i = 0; i < LandingZoneFlares; i++)
            {
                double a = 2 * Math.PI * i / LandingZoneFlares;   // 0° = north (-Z), every 30°
                float x = zone.X - r * (float)Math.Sin(a), z = zone.Z - r * (float)Math.Cos(a);
                var color = i % (LandingZoneFlares / 4) == 0 ? ColorType.Yellow : ColorType.Red;
                Place($"z:{number}:{i}", x, z, y, color, seconds);
            }
        }

        private static void Place(string key, float x, float z, float y, ColorType color, float seconds)
        {
            if (!GameManager.RunSimulation) return;
            float until = Time.unscaledTime + seconds;
            if (Spots.TryGetValue(key, out var lit))
            {
                var existing = Thing.Find(lit.Id);
                if (existing != null && !existing.BeingDestroyed && lit.Color == color) { lit.Until = until; return; }
                Remove(key); // other colour (someone else's view) or gone: light a new one
            }
            try
            {
                LogPaletteOnce();
                var at = new Vector3(x, BorderDisplay.GroundAt(x, z, y) + Height, z);
                var flare = OnServer.Create<RoadFlare>(Prefab, at, Quaternion.Euler(180f, 0f, 0f)); // upside down
                if (flare == null) return;
                var body = flare.RigidBody;
                if (body != null) body.isKinematic = true; // stays where it is, does not roll down hills
                flare.CanPickup = false; // the game's own lock: "cannot be picked up right now" – for the host too
                var swatch = GameManager.GetColorSwatch((int)color);
                if (swatch != null) OnServer.SetCustomColor(flare, swatch.Index);
                flare.WaitThenBurn().Forget();
                Spots[key] = new Lit { Id = flare.ReferenceId, Color = color, Until = until };
                Ids.Add(flare.ReferenceId);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not light a border flare: {ex.Message}");
            }
        }

        /// <summary>Called every frame by the plugin: put out flares whose time is up or that someone picked up
        /// (the host's own hands are not checked by the protection, so this catches them too).</summary>
        public static void Tick()
        {
            if (Spots.Count == 0) return;
            float now = Time.unscaledTime;
            List<string> done = null;
            foreach (var kv in Spots)
            {
                bool over = now >= kv.Value.Until;
                if (!over)
                {
                    var thing = Thing.Find(kv.Value.Id);
                    over = thing == null || thing.BeingDestroyed || thing.RootParentHuman != null;
                }
                if (over) (done ?? (done = new List<string>())).Add(kv.Key);
            }
            if (done != null) foreach (var key in done) Remove(key);
        }

        /// <summary>Before saving: no flares in the save file.</summary>
        public static void RemoveAll()
        {
            foreach (var key in new List<string>(Spots.Keys)) Remove(key);
        }

        private static void Remove(string key)
        {
            if (!Spots.TryGetValue(key, out var lit)) return;
            Spots.Remove(key);
            Ids.Remove(lit.Id);
            try
            {
                var thing = Thing.Find(lit.Id);
                if (thing != null && !thing.BeingDestroyed) OnServer.Destroy(thing);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"Could not remove a flare: {ex.Message}"); }
        }

        /// <summary>Once per run: the light colour of every palette entry, to pick the best flare colours.</summary>
        private static void LogPaletteOnce()
        {
            if (_paletteLogged) return;
            _paletteLogged = true;
            try
            {
                for (int i = 0; i < 32; i++)
                {
                    var s = GameManager.GetColorSwatch(i);
                    if (s == null) break;
                    var l = s.Light;
                    Plugin.Log.LogInfo($"Palette {i} ({(ColorType)i}): light r={l.r:0.00} g={l.g:0.00} b={l.b:0.00} a={l.a:0.00}");
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"Could not read the colour palette: {ex.Message}"); }
        }

        public static void Reset()
        {
            Spots.Clear();
            Ids.Clear();
        }
    }
}
