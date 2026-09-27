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
    /// Called by the game only while a placement ghost for the selected build piece exists.
    /// Therefore the golden line is visible only while our whisks or bucket are selected in the hammer.
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class SaunaPlacementVisualLinkPatch
    {
        private static void Postfix(Player __instance, GameObject ___m_placementGhost)
        {
            if (__instance != Player.m_localPlayer)
            {
                return;
            }

            SaunaVisualLink.UpdateForPlacementGhost(___m_placementGhost);
        }
    }
}
