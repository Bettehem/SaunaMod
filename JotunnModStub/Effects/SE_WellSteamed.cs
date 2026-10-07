// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SaunaMod
{
    /// Well Steamed cancels the Wet penalties to health, stamina, and eitr regeneration.
    /// Wet's regeneration penalties are skipped by WellSteamedWetRegenPatch; no extra regeneration is granted.
    internal class SE_WellSteamed : SE_Stats
    {
        /// Reached TIME duration tier, 1..3.
        /// This is NOT the sauna tier: TimeTier only represents 300/600/900 seconds.
        /// It is stored directly in the effect because it cannot be derived reliably from remaining time:
        /// the remaining time is almost never exactly on a tier boundary.
        public int TimeTier;

        /// Tier of the sauna itself, 1..4. Do not confuse it with TimeTier:
        /// 1 = stove only, 2 = stove + whisks, 3 = stove + whisks + bucket, 4 = all of them + towel rack.
        /// Tier 2 makes wetness harmless; tier 4 adds the climbing and freezing bonuses (TowelRackTuning).
        public int SaunaTier = 1;

        // Captured before ForceJump clears ground contact; valid only during that call.
        internal float JumpGroundSlope = -1f;

        /// Tier 4: running uphill on a slope costs less stamina.
        public override void ModifyRunStaminaDrain(float baseDrain, ref float drain, Vector3 dir)
        {
            base.ModifyRunStaminaDrain(baseDrain, ref drain, dir);

            if (HasClimbingBonus() && TowelRackTuning.GroundSlope(m_character) >= TowelRackTuning.ClimbMinSlope)
            {
                // The ground normal leans downhill, so moving against it is moving uphill.
                Vector3 downhill = m_character.m_lastGroundNormal;
                downhill.y = 0f;
                if (Vector3.Dot(dir, downhill) < 0f)
                {
                    drain -= baseDrain * (1f - Mathf.Clamp01(TowelRackTuning.ClimbStaminaMultiplier));
                }
            }
        }

        /// Tier 4: jumping on a slope costs less stamina.
        public override void ModifyJumpStaminaUsage(float baseStaminaUse, ref float staminaUse)
        {
            base.ModifyJumpStaminaUsage(baseStaminaUse, ref staminaUse);

            if (HasClimbingBonus() && JumpGroundSlope >= TowelRackTuning.ClimbMinSlope)
            {
                staminaUse -= baseStaminaUse * (1f - Mathf.Clamp01(TowelRackTuning.ClimbStaminaMultiplier));
            }
        }

        public override string GetTooltipString()
        {
            // Config changes can disable the bonuses while this effect is already active.
            m_tooltip = SaunaTier >= TowelRackTuning.SaunaTier && TowelRackTuning.Enabled != 0
                ? "$se_sauna_wellsteamed_tooltip_towels"
                : SaunaTier >= 2
                    ? "$se_sauna_wellsteamed_tooltip_whisks"
                    : "$se_sauna_wellsteamed_tooltip";
            return base.GetTooltipString();
        }

        private bool HasClimbingBonus()
        {
            return TowelRackTuning.Enabled != 0 && SaunaTier >= TowelRackTuning.SaunaTier &&
                m_character != null && m_character == Player.m_localPlayer;
        }

    }
}
