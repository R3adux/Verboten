using System.Collections.Generic;
using UnityEngine;

namespace Verboten.Game
{
    /// <summary>
    /// Players without a claim who build something (usually an iron frame first):
    ///  - the first time ever: a window explaining claims, with the verdict for that very spot;
    ///  - later: a short chat hint, at most once a minute, when they build where no claim is possible,
    ///    and the borders in the way as sparks.
    /// </summary>
    public static class BuildGuide
    {
        private const float HintEvery = 60f;
        private static readonly Dictionary<ulong, float> LastHint = new Dictionary<ulong, float>();

        public static void OnBuilt(ulong builder, string name, Vector3 p)
        {
            var index = Plugin.Store.Index;
            if (index.OwnedBy(builder).Count > 0) return; // has a base already
            if (index.At(p.x, p.z) != null) return;       // building in a claim (as a friend)

            var record = Plugin.Players.GetOrAdd(builder);
            if (!record.GuideShown)
            {
                record.GuideShown = true;
                var result = ClaimActions.Evaluate(builder, name, p, 0);
                Notify.Popup(builder, Texts.Guide(Verdict(result)));
                if (!result.Success) BorderDisplay.ShowNear(builder, BorderDisplay.Duration);
                return;
            }

            float now = Time.unscaledTime;
            if (LastHint.TryGetValue(builder, out float last) && now - last < HintEvery) return;
            string problem = ClaimActions.QuickProblem(builder, p);
            if (problem == null) return;
            LastHint[builder] = now;
            Notify.Chat(builder, L.T("guide.hint", ("problem", problem)));
            BorderDisplay.ShowNear(builder, BorderDisplay.Duration);
        }

        private static string Verdict(ClaimActions.Result result)
        {
            if (!result.Success) return L.T("verdict.no", ("reason", result.Message + (result.Hint != null ? " " + result.Hint : "")));
            if (result.Claim != null && !result.Claim.IsActive)
                return L.T("guide.verdict.abandoned", ("anchor", L.Anchor));
            return L.T(Staking.Enabled ? "guide.verdict.ok_flags" : "guide.verdict.ok");
        }

        /// <summary>The window was shown in another way (the player built a claim right away).</summary>
        public static void MarkShown(ulong steamId) => Plugin.Players.GetOrAdd(steamId).GuideShown = true;

        public static void Reset() => LastHint.Clear();
    }
}
