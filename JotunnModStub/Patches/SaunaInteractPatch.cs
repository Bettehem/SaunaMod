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

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
    internal static class SaunaInteractPatch
    {
        private static bool Prefix(Fireplace __instance, Humanoid user, bool hold, bool alt, ref bool __result)
        {
            if (hold || !alt)
            {
                return true;
            }

            SaunaStove stove = __instance.GetComponent<SaunaStove>();
            if (stove == null)
            {
                return true;
            }

            __result = stove.Pour(user);
            return false;
        }
    }
}
