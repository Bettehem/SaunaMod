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

    [HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
    internal static class HideWetIconWhileWellSteamedPatch
    {
        // Hud.UpdateStatusEffects runs every frame, so reuse the list.
        // Otherwise tens of thousands of temporary allocations build up during the buff.
        private static readonly List<StatusEffect> s_filtered = new List<StatusEffect>();

        private static void Prefix(ref List<StatusEffect> statusEffects)
        {
            // Wet must remain active for droplets/visuals, but hide its icon
            // while Well Steamed is active. Replace the argument instead of modifying SEMan's list.
            if (statusEffects == null || Player.m_localPlayer == null ||
                SaunaPlugin.WellSteamedHash == 0)
            {
                return;
            }

            SEMan seman = Player.m_localPlayer.GetSEMan();
            SE_WellSteamed wellSteamed = seman != null
                ? seman.GetStatusEffect(SaunaPlugin.WellSteamedHash) as SE_WellSteamed
                : null;
            if (wellSteamed == null || wellSteamed.SaunaTier < 2)
            {
                return;
            }

            s_filtered.Clear();

            foreach (StatusEffect se in statusEffects)
            {
                if (se != null && se.NameHash() == SEMan.s_statusEffectWet)
                {
                    continue;
                }

                s_filtered.Add(se);
            }

            statusEffects = s_filtered;
        }
    }
}
