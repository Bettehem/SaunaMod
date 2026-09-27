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
        private Sprite FindFirstPieceIcon(params string[] prefabNames)
        {
            foreach (string name in prefabNames)
            {
                GameObject prefab = PrefabManager.Instance.GetPrefab(name);
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece != null && piece.m_icon != null)
                {
                    return piece.m_icon;
                }
            }

            return null;
        }

        private void AddSaunaStove()
        {
            try
            {
                PieceConfig config = new PieceConfig
                {
                    Name = "$piece_sauna_stove",
                    Description = "$piece_sauna_stove_desc",
                    PieceTable = PieceTables.Hammer,
                    Category = PieceCategories.Furniture,
                    CraftingStation = CraftingStations.Workbench,
                    Requirements = ParseRecipeConfig(
                        StoveRecipeValue,
                        DefaultStoveRecipe,
                        "sauna_stove")
                };

                CustomPiece stove = new CustomPiece("sauna_stove", "fire_pit_iron", config);

                Fireplace fireplace = stove.PiecePrefab.GetComponent<Fireplace>();
                if (fireplace == null)
                {
                    Jotunn.Logger.LogError("Fireplace component NOT found");
                    return;
                }

                fireplace.m_disableCoverCheck = true;

                // Keep the visual and Fireplace from fire_pit_iron, but use placement and durability rules
                // from the vanilla stone pile.
                ApplyStonePileBehavior(stove.PiecePrefab);

                HideOriginalMeshes(stove.PiecePrefab);
                SetupSteam(fireplace);

                stove.PiecePrefab.AddComponent<SaunaStove>();

                if (!PieceManager.Instance.AddPiece(stove))
                {
                    Jotunn.Logger.LogError("sauna_stove: PieceManager rejected the piece");
                    return;
                }

                _stovePrefab = stove.PiecePrefab;
                ApplyRecipeToPiece(_stovePrefab, StoveRecipeValue, DefaultStoveRecipe, "sauna_stove");
                Jotunn.Logger.LogInfo("sauna_stove registered");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"sauna_stove failed: {ex}");
            }
        }

        /// Keep the appearance and Fireplace from fire_pit_iron, but copy build-piece behavior
        /// from the vanilla stone_pile. The stove then behaves like an ordinary stone pile:
        /// it requires real support, collapses if that support is removed, and is not itself load-bearing.
        private void ApplyStonePileBehavior(GameObject stovePrefab)
        {
            GameObject donor = PrefabManager.Instance.GetPrefab("stone_pile");
            if (donor == null)
            {
                Jotunn.Logger.LogWarning("stone_pile prefab not found; keeping fire_pit_iron structure profile");
                return;
            }

            Piece sourcePiece = donor.GetComponent<Piece>();
            Piece targetPiece = stovePrefab.GetComponent<Piece>();

            if (sourcePiece != null && targetPiece != null)
            {
                // Copy only placement restrictions and surface contact behavior.
                // Keep our stove name, description, recipe, category, and crafting station unchanged.
                targetPiece.m_groundPiece = sourcePiece.m_groundPiece;
                targetPiece.m_allowAltGroundPlacement = sourcePiece.m_allowAltGroundPlacement;
                targetPiece.m_groundOnly = sourcePiece.m_groundOnly;
                targetPiece.m_cultivatedGroundOnly = sourcePiece.m_cultivatedGroundOnly;
                targetPiece.m_waterPiece = sourcePiece.m_waterPiece;
                targetPiece.m_clipGround = sourcePiece.m_clipGround;
                targetPiece.m_clipEverything = sourcePiece.m_clipEverything;
                targetPiece.m_noClipping = sourcePiece.m_noClipping;
                targetPiece.m_noInWater = sourcePiece.m_noInWater;
                targetPiece.m_notOnWood = sourcePiece.m_notOnWood;
                targetPiece.m_notOnTiltingSurface = sourcePiece.m_notOnTiltingSurface;
                targetPiece.m_notOnFloor = sourcePiece.m_notOnFloor;
                targetPiece.m_inCeilingOnly = sourcePiece.m_inCeilingOnly;
                targetPiece.m_onlyInTeleportArea = sourcePiece.m_onlyInTeleportArea;
                targetPiece.m_allowedInDungeons = sourcePiece.m_allowedInDungeons;
                targetPiece.m_spaceRequirement = sourcePiece.m_spaceRequirement;

                // Also use the placement effect from the stone pile.
                targetPiece.m_placeEffect = sourcePiece.m_placeEffect;
            }
            else
            {
                Jotunn.Logger.LogWarning("stone_pile Piece component missing; placement profile not copied");
            }

            WearNTear sourceWnt = donor.GetComponent<WearNTear>();
            WearNTear targetWnt = stovePrefab.GetComponent<WearNTear>();

            if (sourceWnt != null && targetWnt != null)
            {
                // Use the same structural and damage profiles as stone_pile.
                targetWnt.m_health = sourceWnt.m_health;
                targetWnt.m_materialType = sourceWnt.m_materialType;
                targetWnt.m_damages = sourceWnt.m_damages;
                targetWnt.m_burnable = sourceWnt.m_burnable;
                targetWnt.m_noSupportWear = sourceWnt.m_noSupportWear;
                targetWnt.m_noRoofWear = sourceWnt.m_noRoofWear;
                targetWnt.m_supports = sourceWnt.m_supports;

                // Use the stone impact and destruction sounds/effects from stone_pile.
                targetWnt.m_destroyedEffect = sourceWnt.m_destroyedEffect;
                targetWnt.m_hitEffect = sourceWnt.m_hitEffect;
                targetWnt.m_switchEffect = sourceWnt.m_switchEffect;

                Jotunn.Logger.LogInfo(
                    $"sauna structure copied from stone_pile: health={targetWnt.m_health}, " +
                    $"material={targetWnt.m_materialType}, noSupportWear={targetWnt.m_noSupportWear}, " +
                    $"supports={targetWnt.m_supports}, noRoofWear={targetWnt.m_noRoofWear}");
            }
            else
            {
                Jotunn.Logger.LogWarning("stone_pile WearNTear missing; structure profile not copied");
            }
        }

        private void HideOriginalMeshes(GameObject stovePrefab)
        {
            foreach (MeshRenderer mr in stovePrefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.enabled = false;
            }

            foreach (SkinnedMeshRenderer smr in stovePrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.enabled = false;
            }
        }

        private void ApplySteamCollisions()
        {
            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(
                    SteamLayer, i, Physics.GetIgnoreLayerCollision(SmokeLayer, i));
            }

            bool smokeVsSmoke = Physics.GetIgnoreLayerCollision(SmokeLayer, SmokeLayer);
            Physics.IgnoreLayerCollision(SteamLayer, SteamLayer, smokeVsSmoke);
        }

        private void SetupSteam(Fireplace fireplace)
        {
            SmokeSpawner spawner = fireplace.m_smokeSpawner;
            if (spawner == null || spawner.m_smokePrefab == null)
            {
                Jotunn.Logger.LogError("SmokeSpawner or smoke prefab missing");
                return;
            }

            if (_prefabContainer == null)
            {
                _prefabContainer = new GameObject("SaunaModPrefabs");
                _prefabContainer.SetActive(false);
                DontDestroyOnLoad(_prefabContainer);
            }

            GameObject steam = Instantiate(spawner.m_smokePrefab, _prefabContainer.transform);
            steam.name = "vfx_sauna_steam";
            SetLayerRecursive(steam, SteamLayer);

            spawner.m_smokePrefab = steam;
            spawner.m_testMask = 1 << SteamLayer;

            SaunaStove.SteamPrefab = steam;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }
    }
}
