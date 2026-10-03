// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using HarmonyLib;
using UnityEngine;

namespace SaunaMod
{
    /// Tier 4 Well steamed: the player stands on steeper slopes and slips off them more slowly.
    ///
    /// Character.ApplySlide is replaced only for the local player with the bonus; the copy below
    /// is the vanilla method with two changes: the slide angle (vanilla GetSlideAngle, 38 degrees
    /// for players) gets SlideAngleBonus, and slippage builds up at SlipSpeed instead of 1 per second.
    /// GetSlideAngle itself is not patched: it is small enough for the JIT to inline into ApplySlide.
    [HarmonyPatch(typeof(Character), nameof(Character.ApplySlide))]
    internal static class WellSteamedClimbingPatch
    {
        private static bool Prefix(Character __instance, float dt, ref Vector3 currentVel, bool running)
        {
            if (TowelRackTuning.ActiveFor(__instance) == null)
            {
                return true;
            }

            bool canWallRun = __instance.CanWallRun();
            Vector3 groundNormal = __instance.m_groundTilt != Character.GroundTiltType.None
                ? __instance.m_groundTiltNormal
                : __instance.m_lastGroundNormal;
            float angle = Mathf.Clamp(Mathf.Acos(Mathf.Clamp01(groundNormal.y)) * Mathf.Rad2Deg, 0f, 90f);
            Vector3 slideDir = Vector3.Cross(__instance.m_lastGroundNormal,
                Vector3.Cross(__instance.m_lastGroundNormal, Vector3.up));
            bool moving = currentVel.magnitude > 0.1f;

            if (angle > __instance.GetSlideAngle() + Mathf.Max(0f, TowelRackTuning.SlideAngleBonus))
            {
                if (running && canWallRun && moving)
                {
                    __instance.m_slippage = 0f;
                    __instance.m_wallRunning = true;
                }
                else
                {
                    __instance.m_slippage = Mathf.MoveTowards(__instance.m_slippage, 1f,
                        Mathf.Max(0f, TowelRackTuning.SlipSpeed) * dt);
                }

                currentVel = Vector3.Lerp(currentVel, slideDir * 5f, __instance.m_slippage);
                __instance.m_sliding = __instance.m_slippage > 0.5f;
            }
            else
            {
                __instance.m_slippage = 0f;
            }

            return false;
        }
    }

    /// Tier 4 Well steamed: Freezing ticks hurt less. Freezing deals its damage as a HitData of
    /// type Freezing through Character.Damage, which ends in RPC_Damage on the player's owner.
    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    internal static class WellSteamedFreezingPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (hit == null || hit.m_hitType != HitData.HitType.Freezing ||
                TowelRackTuning.ActiveFor(__instance) == null)
            {
                return;
            }

            hit.m_damage.Modify(Mathf.Clamp01(TowelRackTuning.FreezingDamageMultiplier));
        }
    }
}
