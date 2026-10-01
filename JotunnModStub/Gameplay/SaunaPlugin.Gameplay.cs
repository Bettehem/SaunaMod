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
        private void Update()
        {
            if (_steaming == null || _wellSteamed == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                _steamTime = 0f;
                _gapTime = 0f;
                _timeTier = 0;
                _worldReady = false;
                ResetSkinRedness();
                return;
            }

            if (!_worldReady)
            {
                // ObjectDB may not be ready yet, so simply wait for the next frame.
                // Without this check the ready flag could be set while initialization never runs.
                if (ObjectDB.instance == null)
                {
                    return;
                }

                _worldReady = true;

                ApplySteamCollisions();
                EnsureIcons();
                ReadWetPenalty();
                FindSteamVfx();

                StoveVisual.CollectCandidates();
                SaunaStove.RebuildAll();

                // Player.OnSpawned normally refreshes known/build pieces through Jotunn, but
                // the kitbashed whisks have shown a reproducible first-join-only miss on a
                // dedicated server. Rebind the authoritative prefab and refresh the local
                // availability list once the local player and ObjectDB are both ready.
                EnsureSaunaWrisksRuntimeRegistration(player);
                StartCoroutine(RefreshWrisksAfterSpawn(player));

                RefreshPieceIcon();
                RefreshWrisksIcon();
                RefreshBucketIcon();
            }

            SaunaEditor.Update();

            // While the player is steaming, keep Well Steamed's timer frozen.
            // Correct it every frame rather than every half-second because the game adds time
            // each frame, and infrequent correction makes the timer visibly jump.
            FreezeWellSteamed(player);

            _checkTimer += Time.deltaTime;
            if (_checkTimer < CheckInterval)
            {
                return;
            }

            float elapsed = _checkTimer;
            _checkTimer = 0f;

            SEMan seman = player.GetSEMan();

            UpdateSkinRedness(player, seman, elapsed);

            int steamMask = 1 << SteamLayer;
            UnityEngine.Vector3 head = player.GetTopPoint();
            UnityEngine.Vector3 feet = player.transform.position;

            // Steaming only counts under a roof. A cloud may touch the player outdoors,
            // but that is not treated as a sauna. Vanilla Shelter is
            // the same shelter check used by the game itself.
            bool sheltered = seman.HaveStatusEffect(SEMan.s_statusEffectShelter);

            bool inSteam = sheltered &&
                (Physics.CheckSphere(head + UnityEngine.Vector3.up * 0.1f, DetectRadius, steamMask) ||
                 Physics.CheckSphere(head - UnityEngine.Vector3.up * 0.5f, DetectRadius, steamMask) ||
                 Physics.CheckSphere(feet + UnityEngine.Vector3.up * 0.5f, DetectRadius, steamMask));

            List<ItemDrop.ItemData> equippedItems = player.m_inventory.GetEquippedItems();
            equippedItems.RemoveAll(item =>
                    (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Tool ||
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ||
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow ||
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield ||
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch ||
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo ||
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket ||
                    item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility) &&
                    item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shoulder
                    );
            bool wearingClothes = equippedItems.Count > 0;

            if (inSteam)
            {
                if (!seman.HaveStatusEffect(_steaming.NameHash()))
                {
                    seman.AddStatusEffect(_steaming, false, 0, 0f, -1);
                }

                // Check whether player is wearing clothing/armor and apply a "Too Hot" effect on them to encourage using the sauna naked.
                if (wearingClothes)
                {
                    if (!seman.HaveStatusEffect(_tooHot.NameHash()))
                    {
                        player.Message(MessageHud.MessageType.Center,
                                _loc.TryTranslate("$msg_sauna_too_hot"));
                        seman.AddStatusEffect(_tooHot);
                    }
                }
                else
                {
                    // Healing happens only while physically inside a real steam cloud. Heal itself clamps to max health.
                    player.Heal(SteamHealPerSecond * elapsed, false);

                    // The player returned to steam within the grace period, so count the short gap
                    // exactly as if contact had never been interrupted.
                    _steamTime += elapsed + _gapTime;
                    _gapTime = 0f;

                    if (_steamTime >= SteamTimeToBuff)
                    {
                        _steamTime = 0f;
                        GrantWellSteamed(player, seman);
                    }
                }
            }
            else if (_gapTime + elapsed < SteamTuning.Grace &&
                     seman.HaveStatusEffect(_steaming.NameHash()))
            {
                // Steam contact was briefly lost, for example because a cloud drifted away.
                // Keep the effect and progress unchanged and only remember the gap duration.
                _gapTime += elapsed;
            }
            else
            {
                if (seman.HaveStatusEffect(_tooHot.NameHash()))
                {
                    seman.RemoveStatusEffect(_tooHot.NameHash(), true);
                }
                seman.RemoveStatusEffect(_steaming.NameHash(), true);
                _steamTime = 0f;
                _gapTime = 0f;
                _timeTier = 0;   // The player stayed out of steam too long, so restart tier progression.
            }

            if (!wearingClothes)
            {
                if (seman.HaveStatusEffect(_tooHot.NameHash()))
                {
                    seman.RemoveStatusEffect(_tooHot.NameHash(), true);
                }
            }
        }

        /// Subtracts exactly the amount of time the game added to Well Steamed this frame.
        /// From the player's perspective the timer appears frozen.
        private void FreezeWellSteamed(Player player)
        {
            if (_steaming == null)
            {
                return;
            }

            SEMan seman = player.GetSEMan();

            if (!seman.HaveStatusEffect(_steaming.NameHash()))
            {
                return;
            }

            StatusEffect live = seman.GetStatusEffect(WellSteamedHash);

            if (live != null)
            {
                live.m_time = Mathf.Max(0f, live.m_time - Time.deltaTime);
            }
        }

        private void GrantWellSteamed(Player player, SEMan seman)
        {
            StatusEffect existing = seman.GetStatusEffect(WellSteamedHash);
            float remaining = existing != null ? existing.GetRemaningTime() : 0f;

            // TWO INDEPENDENT dimensions:
            // TimeTier = duration already earned for the effect (300/600/900 s),
            // SaunaTier = sauna equipment level (stove / +whisks / +bucket).
            SE_WellSteamed existingSteamed = existing as SE_WellSteamed;
            int reached = existingSteamed != null ? existingSteamed.TimeTier : 0;
            int previousSaunaTier = existingSteamed != null
                ? Mathf.Clamp(existingSteamed.SaunaTier, 1, 3)
                : 1;
            int currentSaunaTier = SaunaStove.GetWellSteamedSaunaTierNear(player.transform.position);

            // Do not downgrade a higher sauna tier already earned by the active effect
            // if the player later steams at a less equipped sauna.
            int saunaTier = Mathf.Max(previousSaunaTier, currentSaunaTier);

            // Compatibility fallback for an effect created by an older mod version:
            // old effects had no stored time tier, so reconstruct it from remaining time.
            // Add one second of tolerance for fractions, otherwise 299.98 would not reach 300.
            for (int i = 0; i < WellSteamedTimeTiers.Length; i++)
            {
                if (remaining + 1f >= WellSteamedTimeTiers[i])
                {
                    reached = Mathf.Max(reached, i + 1);
                }
            }

            // Never shorten an already earned active 15-minute effect.
            int maxEarnableTimeTier = MaxEarnableTimeTier(currentSaunaTier);

            _timeTier = Mathf.Max(
                reached,
                Mathf.Min(reached + 1, maxEarnableTimeTier));
            _timeTier = Mathf.Clamp(_timeTier, 1, WellSteamedTimeTiers.Length);

            // A newly earned tier must never shorten time that is already active.
            float ttl = Mathf.Max(WellSteamedTimeTiers[_timeTier - 1], remaining);

            seman.AddStatusEffect(_wellSteamed, true, 0, 0f, -1);
            seman.RemoveStatusEffect(SEMan.s_statusEffectCold, true);

            StatusEffect live = seman.GetStatusEffect(WellSteamedHash);
            if (live != null)
            {
                live.m_ttl = ttl;

                SE_WellSteamed liveSteamed = live as SE_WellSteamed;
                if (liveSteamed != null)
                {
                    liveSteamed.TimeTier = _timeTier;
                    liveSteamed.SaunaTier = saunaTier;
                }

                // Sauna tier is displayed by a separate vanilla star on the HUD icon.
                // Keep the base tooltip free of whisk-specific claims. The extra line
                // appears only after the effect was earned from a sauna with whisks.
                live.m_name = "$se_sauna_wellsteamed";
                live.m_tooltip = saunaTier >= 2
                    ? "$se_sauna_wellsteamed_tooltip_whisks"
                    : "$se_sauna_wellsteamed_tooltip";
            }

            if (_timeTier > reached && _timeTier > 1)
            {
                if (_timeTier >= maxEarnableTimeTier)
                {
                    player.Message(MessageHud.MessageType.Center,
                        _loc.TryTranslate("$msg_sauna_max_tier"));
                }
                else
                {
                    player.Message(MessageHud.MessageType.TopLeft,
                        _loc.TryTranslate("$msg_sauna_tier"));
                }
            }

            SaunaPlayer net = player.GetComponent<SaunaPlayer>();
            if (net != null)
            {
                net.Broadcast();
            }
            else
            {
                SpawnSteamVfxOn(player.gameObject);
            }

            Jotunn.Logger.LogInfo($"Well Steamed: timeTier={_timeTier} (had {reached}), " +
                $"saunaTier={saunaTier} (near={currentSaunaTier}, had={previousSaunaTier}), " +
                $"ttl={ttl:0}, was={remaining:0}");
        }
    }
}
