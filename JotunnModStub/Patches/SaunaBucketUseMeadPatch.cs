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
    /// <summary>
    /// Vanilla does not expose an obvious "use this consumable on that arbitrary object"
    /// action. This redirects resistance-mead use to the bucket when the player is
    /// actually aiming at it. Hotbar use is the primary path; inventory use also works
    /// when Valheim keeps the bucket as the current hover target.
    /// </summary>
    // IMPORTANT: this class is patched manually from SaunaPlugin.Awake().
    // Do not mark it with [HarmonyPatch]: an incompatibility in this optional
    // hotbar hook must never prevent the sauna pieces themselves from registering.
    internal static class SaunaBucketUseMeadPatch
    {
        internal static bool Prefix(
            Humanoid __instance,
            Inventory inventory,
            ItemDrop.ItemData item,
            bool fromInventoryGui)
        {
            Player player = __instance as Player;
            if (player == null || player != Player.m_localPlayer || item == null)
            {
                return true;
            }

            GameObject hover = player.GetHoverObject();
            if (hover == null)
            {
                return true;
            }

            SaunaBucket bucket = hover.GetComponentInParent<SaunaBucket>();
            if (bucket == null)
            {
                return true;
            }

            int meadType = SaunaMeadSystem.GetType(item);
            if (meadType == SaunaMeadSystem.None)
            {
                if (!SaunaMeadSystem.IsMeadLike(item))
                {
                    return true;
                }

                player.Message(MessageHud.MessageType.Center, "$msg_sauna_bucket_resistance_only");
                return false;
            }

            // Humanoid.UseItem(Inventory, ItemData, bool) returns void in Valheim 1.0.12.
            // A Harmony prefix may still return bool to decide whether the original runs,
            // but it must NOT request __result. The previous test hook did so and Harmony
            // correctly rejected it with "Cannot get result from void method".
            bucket.TryUseMead(player, item);
            return false;
        }
    }
}
