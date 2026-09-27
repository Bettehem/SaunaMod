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
    internal partial class SaunaPlugin
    {
        private void OnVanillaPrefabsAvailable()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;

            CreateStatusEffects();
            AddSaunaStove();
            AddSaunaWrisks();
            AddSaunaBucket();
            AddPlayerComponent();

            // The whisks are the only SaunaMod piece assembled through Jotunn Kitbash.
            // Verify them after PieceManager has populated the live Hammer table.
            // Subscribe only after our pieces exist, so manager initialisation order stays
            // the same as in the previously working builds.
            PieceManager.OnPiecesRegistered -= OnPiecesRegistered;
            PieceManager.OnPiecesRegistered += OnPiecesRegistered;
        }

        private void AddPlayerComponent()
        {
            GameObject playerPrefab = PrefabManager.Instance.GetPrefab("Player");

            if (playerPrefab != null && playerPrefab.GetComponent<SaunaPlayer>() == null)
            {
                playerPrefab.AddComponent<SaunaPlayer>();
            }
        }

        private void CreateStatusEffects()
        {
            try
            {
                _steaming = ScriptableObject.CreateInstance<SE_Stats>();
                InitEffect(_steaming, "SaunaSteaming", "se_sauna_steaming", 0f);

                _wellSteamed = ScriptableObject.CreateInstance<SE_WellSteamed>();
                InitEffect(_wellSteamed, "SaunaWellSteamed", "se_sauna_wellsteamed", WellSteamedTimeTiers[0]);

                WellSteamedHash = _wellSteamed.NameHash();

                ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(_steaming, false));
                ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(_wellSteamed, false));

                Jotunn.Logger.LogInfo($"effects created: wellSteamed={WellSteamedHash}");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"status effects failed: {ex}");
            }
        }

        private void InitEffect(SE_Stats se, string objectName, string token, float ttl)
        {
            // The hash is calculated from the object's name, not from m_name.
            se.name = objectName;
            se.m_name = "$" + token;
            se.m_tooltip = "$" + token + "_tooltip";
            se.m_ttl = ttl;
            se.m_startMessage = "$" + token + "_start";
            se.m_startMessageType = MessageHud.MessageType.TopLeft;

            // IMPORTANT: zero in these fields means regeneration is multiplied by zero.
            se.m_healthRegenMultiplier = 1f;
            se.m_staminaRegenMultiplier = 1f;
            se.m_eitrRegenMultiplier = 1f;
        }

        private void ReadWetPenalty()
        {
            SE_Stats wet = FindEffect("Wet") as SE_Stats;

            if (wet == null)
            {
                Jotunn.Logger.LogWarning("Wet effect not found");
                return;
            }

            SE_WellSteamed.WetStaminaMultiplier = wet.m_staminaRegenMultiplier;
            SE_WellSteamed.WetHealthMultiplier = wet.m_healthRegenMultiplier;
            SE_WellSteamed.WetEitrMultiplier = wet.m_eitrRegenMultiplier;

            Jotunn.Logger.LogInfo($"Wet compensated: stamina={wet.m_staminaRegenMultiplier}, " +
                $"health={wet.m_healthRegenMultiplier}, eitr={wet.m_eitrRegenMultiplier}");
        }

        private void FindSteamVfx()
        {
            StatusEffect smoked = FindEffect("Smoked");

            if (smoked == null || smoked.m_startEffects == null ||
                smoked.m_startEffects.m_effectPrefabs == null)
            {
                Jotunn.Logger.LogWarning("Smoked effects not found");
                return;
            }

            foreach (EffectList.EffectData data in smoked.m_startEffects.m_effectPrefabs)
            {
                if (data == null || data.m_prefab == null)
                {
                    continue;
                }

                if (data.m_prefab.GetComponentInChildren<ParticleSystem>(true) != null &&
                    _steamVfxPrefab == null)
                {
                    _steamVfxPrefab = data.m_prefab;
                }
            }

            Jotunn.Logger.LogInfo(_steamVfxPrefab != null
                ? $"player vfx source: {_steamVfxPrefab.name}"
                : "no particle vfx among Smoked effects");
        }

        private StatusEffect FindEffect(string objectName)
        {
            foreach (StatusEffect se in ObjectDB.instance.m_StatusEffects)
            {
                if (se != null && se.name == objectName)
                {
                    return se;
                }
            }
            return null;
        }

        public static void SpawnSteamVfxOn(GameObject target)
        {
            if (_steamVfxPrefab == null || target == null || _prefabContainer == null)
            {
                return;
            }

            if (target.transform.Find("vfx_sauna_player_steam") != null)
            {
                return;
            }

            try
            {
                GameObject vfx = Instantiate(_steamVfxPrefab, _prefabContainer.transform);
                vfx.name = "vfx_sauna_player_steam";

                foreach (TimedDestruction td in vfx.GetComponentsInChildren<TimedDestruction>(true))
                {
                    DestroyImmediate(td);
                }

                // ZSyncTransform fails without ZNetView, so remove it first.
                foreach (ZSyncTransform zs in vfx.GetComponentsInChildren<ZSyncTransform>(true))
                {
                    DestroyImmediate(zs);
                }

                foreach (ZNetView nv in vfx.GetComponentsInChildren<ZNetView>(true))
                {
                    DestroyImmediate(nv);
                }

                foreach (ParticleSystem ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.loop = true;
                }

                SaunaVfxLife life = vfx.AddComponent<SaunaVfxLife>();
                life.Remaining = SteamVfxDuration;

                vfx.transform.SetParent(target.transform, false);
                vfx.transform.localPosition = new UnityEngine.Vector3(0f, PlayerVfxHeight, 0f);
                vfx.transform.localRotation = UnityEngine.Quaternion.identity;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"vfx spawn failed: {ex.Message}");
            }
        }
    }
}
