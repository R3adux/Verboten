using System;
using System.Collections.Generic;
using System.Diagnostics;
using Assets.Scripts;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using HarmonyLib;
using UnityEngine;
using Verboten.Core;

namespace Verboten.Game
{
    /// <summary>
    /// Explosions and claims (decision 2026-11-17):
    ///  - A built device that explodes INSIDE a claim (furnace, Stirling engine, circuit housing, …) damages
    ///    that claim like in the normal game – accidents still happen.
    ///  - Everything else (grenades, dynamite, explosive charges, portable canisters, suits, tablets, rockets,
    ///    devices outside the claim, unknown sources) damages nothing and nobody in a claim, whoever caused it,
    ///    and does not dig terrain in or at a claim.
    ///  - Carried things are damaged as usual; outside claims nothing changes.
    ///
    /// The game does not say who or what caused an explosion. Explosion.Explode is called directly by the
    /// exploding object, so the calling method's class tells what exploded. Unknown = treated as foreign.
    /// </summary>
    public static class Damage
    {
        /// <summary>Claim that the current explosion may damage (its own exploding device), or null.</summary>
        [ThreadStatic] private static string _allowedClaim;
        [ThreadStatic] private static bool _inExplosion;

        public static void Register()
        {
            var t = typeof(Damage);
            Hooks.Add(Hooks.Kind.Protection, "Find what exploded", "Explosion.Explode",
                () => AccessTools.Method(typeof(Explosion), "Explode",
                    new[] { typeof(float), typeof(Vector3), typeof(float), typeof(float), typeof(bool) }),
                t, prefix: nameof(ExplodePrefix), finalizer: nameof(ExplodeFinalizer));
            Hooks.Add(Hooks.Kind.Protection, "No foreign explosion damage in claims", "Explosion.DamageThing",
                () => AccessTools.Method(typeof(Explosion), "DamageThing", new[] { typeof(Thing), typeof(Vector3), typeof(float) }),
                t, prefix: nameof(DamageThingPrefix));
        }

        public struct State
        {
            public string AllowedClaim;
            public bool InExplosion;
        }

        private static void ExplodePrefix(Vector3 __1, float __2, ref bool __4, out State __state)
        {
            __state = new State { AllowedClaim = _allowedClaim, InExplosion = _inExplosion };
            _inExplosion = true;
            _allowedClaim = null;
            try
            {
                var index = Plugin.Store.Index;
                if (index.Count == 0) return;
                var claim = index.At(__1.x, __1.z);
                var source = claim != null ? SourceType() : null;
                if (claim != null && source != null && typeof(Structure).IsAssignableFrom(source))
                {
                    _allowedClaim = claim.Id; // a built device in this claim: its own accident
                    return;
                }
                // everything else must not dig terrain in or at a claim (no ore theft by dynamite)
                if (__4 && index.FirstBlockingNear(__1.x, __1.z, Math.Max(1f, __2), 0, Permission.Mining) != null)
                    __4 = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not tell what exploded (treated as foreign): {ex.Message}");
            }
        }

        private static Exception ExplodeFinalizer(Exception __exception, State __state)
        {
            _allowedClaim = __state.AllowedClaim;
            _inExplosion = __state.InExplosion;
            return __exception;
        }

        private static bool DamageThingPrefix(Thing __0)
        {
            try
            {
                var thing = __0;
                if (thing == null) return true;
                if (Markers.IsMarker(thing.ReferenceId)) return false; // tracker beacons of abandoned bases
                // things someone carries are hit as usual (they move with the person)
                if (!(thing is Entity) && thing.RootParentHuman != null) return true;
                var index = Plugin.Store.Index;
                if (index.Count == 0) return true;
                var p = thing.Position;
                var claim = index.At(p.x, p.z);
                if (claim == null) return true;
                // in a claim: only its own device accidents do damage – to things and to people.
                // Called outside Explode (should not happen): protect.
                return _inExplosion && claim.Id == _allowedClaim;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Explosion check failed (thing protected): {ex}");
                return false;
            }
        }

        /// <summary>
        /// Class of the game object whose code called Explosion.Explode, or null if unknown.
        /// Skips Harmony/Verboten frames and unwraps compiler-generated classes (coroutines, lambdas).
        /// </summary>
        private static Type SourceType()
        {
            var frames = new StackTrace(1, false).GetFrames();
            if (frames == null) return null;
            var gameAssembly = typeof(Thing).Assembly;
            foreach (var frame in frames)
            {
                var type = frame.GetMethod()?.DeclaringType;
                if (type == null || type.Assembly != gameAssembly) continue; // Harmony wrappers, Verboten, Unity
                while (type.IsNested && type.Name.StartsWith("<", StringComparison.Ordinal) && type.DeclaringType != null)
                    type = type.DeclaringType;
                if (type == typeof(Explosion)) continue;
                return type; // the first game class below Explode is the one that exploded
            }
            return null;
        }
    }

    /// <summary>Who is online right now (host + connected players), recomputed at most once per frame.</summary>
    public static class Online
    {
        private static readonly HashSet<ulong> Ids = new HashSet<ulong>();
        private static int _frame = -1;

        public static bool Contains(ulong steamId)
        {
            if (_frame != Time.frameCount)
            {
                _frame = Time.frameCount;
                Ids.Clear();
                if (!GameManager.IsBatchMode && Human.LocalHuman != null) Ids.Add(Identity.SteamIdOf(Human.LocalHuman));
                foreach (var client in NetworkBase.Clients)
                    if (client != null && !client.IsHost) Ids.Add(client.ClientId);
            }
            return Ids.Contains(steamId);
        }
    }
}
