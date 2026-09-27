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

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
    internal static class SaunaHoverPatch
    {
        private static void Postfix(Fireplace __instance, ref string __result)
        {
            if (__instance.GetComponent<SaunaStove>() == null)
            {
                return;
            }

            __result += "\n[<color=yellow><b>Shift + E</b></color>] " +
                Localization.instance.Localize("$piece_sauna_pour");
        }
    }
}
