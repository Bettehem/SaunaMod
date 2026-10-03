// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using HarmonyLib;

namespace SaunaMod
{
    /// Fireplace.UpdateFireplace burns off all fuel used since its last update in one step,
    /// including the time the area was unloaded. Bring the stone heat up to date first, while
    /// the fuel still shows when the wood ran out; afterwards that moment is lost.
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.UpdateFireplace))]
    internal static class SaunaStoveFuelPatch
    {
        private static void Prefix(Fireplace __instance)
        {
            if (__instance.m_nview == null || !__instance.m_nview.IsOwner())
            {
                return;
            }

            SaunaStove stove = __instance.GetComponent<SaunaStove>();
            if (stove != null)
            {
                stove.UpdateStoneHeat();
            }
        }
    }
}
