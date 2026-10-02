// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using UnityEngine;

namespace SaunaMod
{
    /// How the stove stones redden and glow with stone heat. Editable live in the F7 editor.
    ///
    /// Same logic as skin redness: the stone color is multiplied by Tint, scaled by heat and Strength.
    /// Not every stone reddens equally: the top of the dome and the center of the floor
    /// get the full effect, the bottom dome layer and the floor edge stay untouched.
    internal static class StoneRednessTuning
    {
        /// How red the hottest stones get at 100 heat: 0 = never changes, 1 = full Tint.
        public static float Strength = 0.9f;

        /// Multiplier applied to the stone color at full strength.
        public const float TintR = 1.5f;
        public const float TintG = 0.55f;
        public const float TintB = 0.4f;

        /// Self-illumination of the hottest stones at 100 heat. Works only if the stone shader
        /// has an emission color; the point light below works regardless.
        public static float Glow = 0.55f;

        /// Extra glow multiplier for the floor stones. Their weight already fades to zero
        /// at the edge, so this mostly brightens the center under the fire.
        public const float FloorGlowBoost = 1.5f;

        /// Point light inside the dome at 100 heat. Unlike the fire light, it stays on
        /// while the stones are hot, also after the fire has gone out.
        public static float LightIntensity = 1.6f;
        public static float LightRange = 3.5f;

        /// Glow of the coals under the logs: full while the fire burns,
        /// otherwise fading with the stone heat.
        public static float CoalGlow = 0.7f;
        public static readonly Color CoalGlowColor = new Color(1f, 0.3f, 0.05f);

        /// Editor only: 1 shows the stones at 100 heat, 0 = real stone heat.
        public static int Preview;

        private static readonly Color GlowColor = new Color(1f, 0.3f, 0.05f);
        public static readonly Color LightColor = new Color(1f, 0.45f, 0.2f);

        public enum StoveGlow
        {
            Standard,
            Dim
        }

        /// Redness, glow and light brightness for the chosen preset (cfg Stove.Glow).
        /// Dim halves them; the light range stays the same so the sauna is lit as wide, only softer.
        public static void ApplyGlow(StoveGlow glow)
        {
            float scale = glow == StoveGlow.Dim ? 0.5f : 1f;

            Strength = 0.9f * scale;
            Glow = 0.55f * scale;
            LightIntensity = 1.6f * scale;
            CoalGlow = 0.7f * scale;
        }

        /// Stone color for the given redness (0..1). At zero the original color is returned unchanged.
        public static Color Apply(Color baseColor, float redness)
        {
            float amount = Mathf.Clamp01(redness) * Mathf.Clamp01(Strength);
            if (amount <= 0f)
            {
                return baseColor;
            }

            Color hotColor = new Color(baseColor.r * TintR, baseColor.g * TintG, baseColor.b * TintB, baseColor.a);
            return Color.Lerp(baseColor, hotColor, amount);
        }

        public static Color Emission(float redness)
        {
            return GlowColor * (Mathf.Clamp01(redness) * Mathf.Max(0f, Glow));
        }
    }
}
