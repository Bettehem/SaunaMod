// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using HarmonyLib;

namespace SaunaMod
{
    /// Every equipment change re-applies the saved skin color to VisEquipment.
    /// Re-apply the sauna redness right away so the skin does not flash back to normal.
    [HarmonyPatch(typeof(Player), "SetupVisEquipment")]
    internal static class SkinRednessPatch
    {
        private static void Postfix(Player __instance, VisEquipment visEq, bool isRagdoll)
        {
            if (isRagdoll || __instance != Player.m_localPlayer || visEq != __instance.m_visEquipment)
            {
                return;
            }

            SaunaPlugin.ApplySkinRedness(__instance);
        }
    }
}
