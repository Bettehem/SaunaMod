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

    [HarmonyPatch(typeof(SEMan), "Internal_AddStatusEffect")]
    internal static class BlockColdPatch
    {
        private static bool Prefix(SEMan __instance, int nameHash, ref StatusEffect __result)
        {
            // Well Steamed protects against Cold, but Wet remains active
            // because it is still needed for vanilla water-drop visuals on the character.
            if (nameHash != SEMan.s_statusEffectCold)
            {
                return true;
            }

            if (SaunaPlugin.WellSteamedHash == 0)
            {
                return true;
            }

            if (__instance.m_character == null || !__instance.m_character.IsPlayer())
            {
                return true;
            }

            if (!__instance.HaveStatusEffect(SaunaPlugin.WellSteamedHash))
            {
                return true;
            }

            __result = null;
            return false;
        }
    }
}
