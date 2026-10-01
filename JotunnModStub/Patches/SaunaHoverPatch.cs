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
            SaunaStove stove = __instance.GetComponent<SaunaStove>();
            if (stove == null)
            {
                return;
            }

            if (StoveTuning.ShowHeatOnHover != 0)
            {
                float heat = stove.GetHeat();
                string color = heat >= StoveTuning.MinPourHeat ? "orange" : "#9AA0A6";
                __result += $"\n{Localization.instance.Localize("$piece_sauna_heat")}: " +
                    $"<color={color}><b>{Mathf.FloorToInt(heat)}</b></color> / {StoveTuning.MaxHeat:0}";
            }

            __result += "\n[<color=yellow><b>Shift + E</b></color>] " +
                Localization.instance.Localize("$piece_sauna_pour");
        }
    }
}
