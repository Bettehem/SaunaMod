// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SaunaMod
{
    /// Skip Wet's regeneration modifiers, preserving the effect and its water-drop visuals.
    /// Dividing the final multiplier cannot undo Wet reliably because vanilla combines
    /// additive bonuses (such as Rested) and multiplicative penalties in list order.
    [HarmonyPatch(typeof(SE_Stats))]
    internal static class WellSteamedWetRegenPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SE_Stats), nameof(SE_Stats.ModifyHealthRegen));
            yield return AccessTools.Method(typeof(SE_Stats), nameof(SE_Stats.ModifyStaminaRegen));
            yield return AccessTools.Method(typeof(SE_Stats), nameof(SE_Stats.ModifyEitrRegen));
        }

        private static bool Prefix(SE_Stats __instance)
        {
            if (SaunaPlugin.WellSteamedHash == 0 || __instance.NameHash() != SEMan.s_statusEffectWet ||
                __instance.m_character == null || !__instance.m_character.IsPlayer())
            {
                return true;
            }

            SE_WellSteamed effect = __instance.m_character.GetSEMan()
                .GetStatusEffect(SaunaPlugin.WellSteamedHash) as SE_WellSteamed;
            return effect == null || effect.SaunaTier < 2;
        }
    }
}
