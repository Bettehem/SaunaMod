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
    internal static class SaunaMeadSystem
    {
        public const int None = 0;
        public const int Poison = 1;
        public const int Frost = 2;
        public const int Fire = 3;

        private static string PrefabName(int type)
        {
            switch (type)
            {
                case Poison: return "MeadPoisonResist";
                case Frost: return "MeadFrostResist";
                case Fire: return "BarleyWine";
                default: return null;
            }
        }

        private static ItemDrop GetItemDrop(int type)
        {
            string prefabName = PrefabName(type);
            if (string.IsNullOrEmpty(prefabName) || ObjectDB.instance == null)
            {
                return null;
            }

            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            return prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        }

        public static int GetType(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return None;
            }

            string prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            for (int type = Poison; type <= Fire; type++)
            {
                if (!string.IsNullOrEmpty(prefabName) &&
                    string.Equals(prefabName, PrefabName(type), StringComparison.Ordinal))
                {
                    return type;
                }
            }

            // Fallback for ItemData without m_dropPrefab: compare the actual
            // consumed StatusEffect with the corresponding vanilla mead effect.
            StatusEffect effect = item.m_shared != null ? item.m_shared.m_consumeStatusEffect : null;
            if (effect != null)
            {
                int hash = effect.NameHash();
                for (int type = Poison; type <= Fire; type++)
                {
                    ItemDrop drop = GetItemDrop(type);
                    StatusEffect candidate =
                        drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                            ? drop.m_itemData.m_shared.m_consumeStatusEffect
                            : null;

                    if (candidate != null && candidate.NameHash() == hash)
                    {
                        return type;
                    }
                }
            }

            return None;
        }

        /// <summary>
        /// Returns true for vanilla mead/barley-wine consumables, including meads
        /// that are not supported by the sauna bucket. This lets the bucket explain
        /// why an ordinary mead cannot be poured in without intercepting unrelated food.
        /// </summary>
        public static bool IsMeadLike(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return false;
            }

            string prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            if (!string.IsNullOrEmpty(prefabName) &&
                (prefabName.StartsWith("Mead", StringComparison.OrdinalIgnoreCase) ||
                 prefabName.StartsWith("BarleyWine", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            string sharedName = item.m_shared != null ? item.m_shared.m_name : null;
            return !string.IsNullOrEmpty(sharedName) &&
                (sharedName.IndexOf("mead", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 sharedName.IndexOf("barleywine", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string LocalizedName(int type)
        {
            ItemDrop drop = GetItemDrop(type);
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
            {
                return type == Poison ? "Poison resistance mead"
                    : type == Frost ? "Frost resistance mead"
                    : type == Fire ? "Fire resistance barley wine"
                    : "mead";
            }

            string token = drop.m_itemData.m_shared.m_name;
            return Localization.instance != null
                ? Localization.instance.Localize(token)
                : token;
        }

        public static bool ApplyToPlayer(Player player, int type)
        {
            if (player == null || type < Poison || type > Fire)
            {
                return false;
            }

            ItemDrop drop = GetItemDrop(type);
            StatusEffect source =
                drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                    ? drop.m_itemData.m_shared.m_consumeStatusEffect
                    : null;

            if (source == null)
            {
                Jotunn.Logger.LogWarning($"sauna mead: no consume status effect for type={type}");
                return false;
            }

            SEMan seman = player.GetSEMan();
            if (seman == null)
            {
                return false;
            }

            StatusEffect old = seman.GetStatusEffect(source.NameHash());
            float remaining = old != null ? old.GetRemaningTime() : 0f;
            float boosted = Mathf.Max(1f, source.m_ttl * Mathf.Max(0.1f, SaunaMeadTuning.DurationMultiplier));

            seman.AddStatusEffect(source, true, 0, 0f, -1);

            StatusEffect live = seman.GetStatusEffect(source.NameHash());
            if (live != null)
            {
                // Multiple pours do not stack. A new sauna application only
                // refreshes the duration to the full boosted TTL and never shortens
                // a longer effect that is already active.
                live.m_ttl = Mathf.Max(boosted, remaining);
                live.m_time = 0f;
            }

            string fmt = Localization.instance != null
                ? Localization.instance.Localize("$msg_sauna_mead_aroma")
                : "The aroma of {0} fills the sauna";
            player.Message(
                MessageHud.MessageType.Center,
                string.Format(fmt, LocalizedName(type)));

            Jotunn.Logger.LogInfo(
                $"sauna mead: {player.GetPlayerName()} type={type}, " +
                $"base={source.m_ttl:0.#}, x={SaunaMeadTuning.DurationMultiplier:0.##}, " +
                $"ttl={boosted:0.#}");
            return true;
        }

        public static int DistributeFromStove(SaunaStove stove, int type)
        {
            if (stove == null || type < Poison || type > Fire)
            {
                return 0;
            }

            int sent = 0;
            List<Player> players = Player.GetAllPlayers();
            if (players == null)
            {
                return 0;
            }

            foreach (Player player in players)
            {
                if (player == null ||
                    UnityEngine.Vector3.Distance(player.transform.position, stove.transform.position) >
                        SaunaMeadTuning.EffectRadius)
                {
                    continue;
                }

                // The final Shelter check runs on the player-owning client,
                // so multiplayer does not depend on another player's SEMan being synchronized.
                SaunaPlayer net = player.GetComponent<SaunaPlayer>();
                if (net != null)
                {
                    net.GrantMeadToOwner(type, stove.transform.position);
                    sent++;
                }
            }

            Jotunn.Logger.LogInfo(
                $"sauna mead: distributed type={type} to {sent} nearby player(s)");
            return sent;
        }

        public static ItemDrop.ItemData FindSingleAvailableMead(Humanoid user, out bool multipleTypes)
        {
            multipleTypes = false;
            if (user == null || user.GetInventory() == null)
            {
                return null;
            }

            ItemDrop.ItemData first = null;
            int firstType = None;

            foreach (ItemDrop.ItemData candidate in user.GetInventory().GetAllItems())
            {
                int type = GetType(candidate);
                if (type == None)
                {
                    continue;
                }

                if (first == null)
                {
                    first = candidate;
                    firstType = type;
                    continue;
                }

                if (type != firstType)
                {
                    multipleTypes = true;
                    return null;
                }
            }

            return first;
        }

        public static void SpawnTestMead(int type, int amount)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            ItemDrop drop = GetItemDrop(type);
            if (drop == null || drop.m_itemData == null)
            {
                player.Message(MessageHud.MessageType.Center, "Test mead prefab not found");
                return;
            }

            ItemDrop.ItemData data = drop.m_itemData.Clone();
            data.m_dropPrefab = drop.gameObject;
            data.m_stack = Mathf.Clamp(
                Mathf.Max(1, amount),
                1,
                Mathf.Max(1, data.m_shared.m_maxStackSize));

            if (!player.GetInventory().AddItem(data))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_noroom");
                return;
            }

            player.Message(
                MessageHud.MessageType.Center,
                $"Spawned {data.m_stack} × {LocalizedName(type)}");
        }
    }
}
