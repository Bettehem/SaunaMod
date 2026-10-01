// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using UnityEngine;

namespace SaunaMod
{
    /// How the skin reddens from sauna heat. Editable live in the F7 editor.
    internal static class SkinRednessTuning
    {
        /// How red the skin gets at full heat: 0 = never changes, 1 = full Tint.
        public static float Strength = 0.6f;

        /// Multiplier applied to the character's own skin color at full strength.
        /// Lowering green and blue while keeping red is what makes the skin look flushed.
        public const float TintR = 1.1f;
        public const float TintG = 0.55f;
        public const float TintB = 0.5f;

        /// Seconds for fully red skin to return to the normal color after leaving the steam.
        /// Default equals the full heating time: 3 time tiers of SteamTimeToBuff (20 s each).
        public static float CoolSeconds = 60f;

        /// Editor only: 1 shows full redness regardless of steaming, 0 = normal behavior.
        public static int Preview;

        /// Returns the skin color to display for the given redness (0..1).
        /// At zero redness the original color is returned unchanged.
        public static Vector3 Apply(Vector3 baseColor, float redness)
        {
            float t = Mathf.Clamp01(redness) * Mathf.Clamp01(Strength);
            if (t <= 0f)
            {
                return baseColor;
            }

            Vector3 hot = Vector3.Scale(baseColor, new Vector3(TintR, TintG, TintB));
            return Vector3.Lerp(baseColor, hot, t);
        }
    }
}
