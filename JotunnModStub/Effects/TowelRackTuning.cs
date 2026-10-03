// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using UnityEngine;

namespace SaunaMod
{
    /// Experimental Well steamed bonuses earned at sauna tier 4 (stove + whisks + bucket + towel rack).
    /// Tuned live from the editor; they apply to the local player only.
    internal static class TowelRackTuning
    {
        /// 1 = bonuses on, 0 = off.
        public static int Enabled = 1;

        /// Degrees added to the steepest slope a player can stand on (vanilla 38).
        public static float SlideAngleBonus = 10f;

        /// How fast the player starts slipping on a slope that is still too steep; vanilla is 1.
        public static float SlipSpeed = 0.5f;

        /// Stamina used for running uphill and jumping on a slope, as a share of normal.
        public static float ClimbStaminaMultiplier = 0.7f;

        /// The ground counts as a slope for the stamina bonus from this angle, degrees.
        public static float ClimbMinSlope = 20f;

        /// Damage taken from Freezing, as a share of normal.
        public static float FreezingDamageMultiplier = 0.5f;

        public const int SaunaTier = 4;

        /// The local player's Well steamed when it carries the tier 4 bonuses, otherwise null.
        public static SE_WellSteamed ActiveFor(Character character)
        {
            if (Enabled == 0 || character == null || character != Player.m_localPlayer ||
                SaunaPlugin.WellSteamedHash == 0)
            {
                return null;
            }

            SE_WellSteamed effect = character.GetSEMan().GetStatusEffect(SaunaPlugin.WellSteamedHash) as SE_WellSteamed;
            return effect != null && effect.SaunaTier >= SaunaTier ? effect : null;
        }

        /// Angle of the ground under the character, degrees; 0 in the air.
        public static float GroundSlope(Character character)
        {
            if (!character.IsOnGround())
            {
                return 0f;
            }

            return Mathf.Acos(Mathf.Clamp01(character.m_lastGroundNormal.y)) * Mathf.Rad2Deg;
        }
    }
}
