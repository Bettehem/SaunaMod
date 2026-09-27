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

    [HarmonyPatch(typeof(Player), "GetComfortLevel")]
    internal static class SaunaComfortPatch
    {
        private static void Postfix(Player __instance, ref int __result)
        {
            if (__instance == null || !SaunaPlugin.SaunaComfortEnabled)
            {
                return;
            }

            // Sauna accessories are intentionally not normal comfort furniture.
            // They contribute only inside a sheltered, active sauna.
            SEMan seman = __instance.GetSEMan();
            if (seman == null || !seman.HaveStatusEffect(SEMan.s_statusEffectShelter))
            {
                return;
            }

            int bonus = SaunaStove.GetSaunaComfortBonusNear(__instance.transform.position);
            if (bonus > 0)
            {
                __result += bonus;
            }
        }
    }
}
