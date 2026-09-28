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
        /// <summary>
        /// Two birch sauna whisks hanging on a bronze nail.
        /// All leaves and branches come from vanilla Birch_Sapling through Jotunn Kitbash.
        /// The piece uses the wall-torch placement profile, so it can be placed only on walls.
        /// </summary>
        private void AddSaunaWrisks()
        {
            try
            {
                PieceConfig config = new PieceConfig
                {
                    Name = "$piece_sauna_wrisks",
                    Description = "$piece_sauna_wrisks_desc",
                    PieceTable = PieceTables.Hammer,
                    Category = PieceCategories.Furniture,
                    CraftingStation = CraftingStations.Workbench,
                    Requirements = ParseRecipeConfig(
                        WhisksRecipeValue,
                        DefaultWhisksRecipe,
                        "sauna_wrisks")
                };

                CustomPiece wrisks = new CustomPiece("sauna_wrisks", true, config);
                wrisks.FixReference = true;

                // Empty CustomPiece contains a technical cube. Keep its components for Jotunn,
                // but never show the cube itself.
                MeshRenderer baseRenderer = wrisks.PiecePrefab.GetComponent<MeshRenderer>();
                if (baseRenderer != null)
                {
                    baseRenderer.enabled = false;
                }

                ApplyWallMountedBehavior(wrisks.PiecePrefab);

                // IMPORTANT: v9 no longer uses Jotunn Kitbash for the whisks.
                // On the tester's first world entry Kitbash produced the correct model briefly
                // (11 vegetation renderers), then the live PieceTable prefab retained only the
                // technical renderer. Build the exact same visual directly from vanilla meshes,
                // just like the already-stable bucket model. This makes the prefab complete before
                // PieceManager/ZNetScene registration and removes the first-login race entirely.
                GameObject visualRoot = new GameObject("sauna_wrisks_visual_root");
                visualRoot.transform.SetParent(wrisks.PiecePrefab.transform, false);
                visualRoot.transform.localPosition =
                    new UnityEngine.Vector3(-0.003f, -5.545f, -1.951f);
                visualRoot.transform.localRotation = UnityEngine.Quaternion.identity;
                visualRoot.transform.localScale =
                    new UnityEngine.Vector3(3.530365f, 3.416816f, 3.504501f);

                GameObject whiskA = new GameObject("birch_wrisk_A");
                whiskA.transform.SetParent(visualRoot.transform, false);
                whiskA.transform.localPosition =
                    new UnityEngine.Vector3(0.4168f, 2.8884f, 0.9607f);
                whiskA.transform.localRotation =
                    UnityEngine.Quaternion.Euler(13.21492f, 287.5618f, 165.6245f);
                whiskA.transform.localScale = UnityEngine.Vector3.one;

                GameObject whiskB = new GameObject("birch_wrisk_B");
                whiskB.transform.SetParent(visualRoot.transform, false);
                whiskB.transform.localPosition =
                    new UnityEngine.Vector3(-0.8235f, 2.6954f, 0.4606f);
                whiskB.transform.localRotation =
                    UnityEngine.Quaternion.Euler(353.8836f, 125.4292f, 162.4787f);
                whiskB.transform.localScale = UnityEngine.Vector3.one;

                int visualParts = 0;
                if (AddPrefabPathVisual(whiskA.transform, "w1_leaves_011", "Birch_Sapling", "healthy/birchleafs011",
                    new UnityEngine.Vector3(-0.278f, -0.065201f, 0.2543f), UnityEngine.Quaternion.Euler(350.2236f, 165.3578f, 23.856f), new UnityEngine.Vector3(0.85f, 0.85f, 0.85f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_branch_1", "Birch_Sapling", "healthy/Birch_Sapling",
                    new UnityEngine.Vector3(0.2479f, 1.131401f, 0.5183f), UnityEngine.Quaternion.Euler(0f, 296.7401f, 0f), new UnityEngine.Vector3(0.08797f, 0.0713f, 0.10933f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_branch_2", "Birch_Sapling", "healthy/Birch_Sapling",
                    new UnityEngine.Vector3(0.2518f, 1.132301f, 0.5088f), UnityEngine.Quaternion.identity, new UnityEngine.Vector3(0.08797f, 0.0713f, 0.10933f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_branch_3", "Birch_Sapling", "healthy/Birch_Sapling",
                    new UnityEngine.Vector3(0.2377f, 1.131401f, 0.5082f), UnityEngine.Quaternion.Euler(0f, 184.9076f, 0f), new UnityEngine.Vector3(0.08797f, 0.0713f, 0.10933f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_leaves_008", "Birch_Sapling", "healthy/birchleafs008",
                    new UnityEngine.Vector3(0.67167f, -0.501514f, 1.028361f), UnityEngine.Quaternion.Euler(346.5353f, 354.7086f, 12.39146f), new UnityEngine.Vector3(0.99943f, 0.99943f, 0.99943f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_branch_4", "Birch_Sapling", "healthy/Birch_Sapling",
                    new UnityEngine.Vector3(0.2433f, 1.131401f, 0.5169f), UnityEngine.Quaternion.Euler(0f, 297.4256f, 0f), new UnityEngine.Vector3(0.08797f, 0.0713f, 0.10933f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_leaves_009", "Birch_Sapling", "healthy/birchleafs009",
                    new UnityEngine.Vector3(0.36f, 0.028f, 1.3352f), UnityEngine.Quaternion.Euler(37.64621f, 207.2802f, 7.544939f), new UnityEngine.Vector3(0.85f, 0.85f, 0.85f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_wrap_0", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.244144f, 1.142425f, 0.508279f), UnityEngine.Quaternion.Euler(348.3143f, 0.114809f, 357.6877f), new UnityEngine.Vector3(0.07527f, 0.08557f, 0.05914f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_wrap_5", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.24951f, 1.0891f, 0.51825f), UnityEngine.Quaternion.Euler(279.2181f, 165.5444f, 208.2841f), new UnityEngine.Vector3(0.047429f, 0.08557f, 0.039869f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_wrap_3", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.24798f, 1.1176f, 0.512f), UnityEngine.Quaternion.Euler(352.6543f, 21.29594f, 91.61265f), new UnityEngine.Vector3(0.305604f, 0.08557f, 0.014521f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_wrap_4", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.24718f, 1.1179f, 0.51451f), UnityEngine.Quaternion.Euler(2.539509f, 284.0812f, 97.08021f), new UnityEngine.Vector3(0.305604f, 0.047018f, 0.014521f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_wrap_1", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.24424f, 1.149471f, 0.50776f), UnityEngine.Quaternion.identity, new UnityEngine.Vector3(0.07527f, 0.08557f, 0.05914f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskA.transform, "w1_wrap_2", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.243945f, 1.156219f, 0.50776f), UnityEngine.Quaternion.Euler(0f, 0f, 352.4707f), new UnityEngine.Vector3(0.07527f, 0.08557f, 0.05914f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_leaves_011", "Birch_Sapling", "healthy/birchleafs011",
                    new UnityEngine.Vector3(-0.26787f, -0.063463f, 0.225423f), UnityEngine.Quaternion.Euler(348.945f, 165.3668f, 23.75588f), new UnityEngine.Vector3(0.85f, 0.85f, 0.85f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_branch_1", "Birch_Sapling", "healthy/Birch_Sapling",
                    new UnityEngine.Vector3(0.2479f, 1.131401f, 0.5183f), UnityEngine.Quaternion.Euler(0f, 296.7401f, 0f), new UnityEngine.Vector3(0.08797f, 0.0713f, 0.10933f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_branch_2", "Birch_Sapling", "healthy/Birch_Sapling",
                    new UnityEngine.Vector3(0.2518f, 1.132301f, 0.5088f), UnityEngine.Quaternion.identity, new UnityEngine.Vector3(0.08797f, 0.0713f, 0.10933f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_branch_3", "Birch_Sapling", "healthy/Birch_Sapling",
                    new UnityEngine.Vector3(0.2377f, 1.131401f, 0.5082f), UnityEngine.Quaternion.Euler(0f, 184.9076f, 0f), new UnityEngine.Vector3(0.08797f, 0.0713f, 0.10933f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_leaves_008", "Birch_Sapling", "healthy/birchleafs008",
                    new UnityEngine.Vector3(0.678061f, -0.518118f, 0.964586f), UnityEngine.Quaternion.Euler(348.4668f, 355.1416f, 12.29774f), new UnityEngine.Vector3(0.99943f, 0.99943f, 0.99943f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_branch_4", "Birch_Sapling", "healthy/Birch_Sapling",
                    new UnityEngine.Vector3(0.2433f, 1.131401f, 0.5169f), UnityEngine.Quaternion.Euler(0f, 297.4256f, 0f), new UnityEngine.Vector3(0.08797f, 0.0713f, 0.10933f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_leaves_009", "Birch_Sapling", "healthy/birchleafs009",
                    new UnityEngine.Vector3(0.36f, 0.028f, 1.3352f), UnityEngine.Quaternion.Euler(37.64621f, 207.2802f, 7.544939f), new UnityEngine.Vector3(0.85f, 0.85f, 0.85f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_wrap_0", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.244144f, 1.142425f, 0.508279f), UnityEngine.Quaternion.Euler(348.3143f, 0.114809f, 357.6877f), new UnityEngine.Vector3(0.07527f, 0.08557f, 0.05914f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_wrap_1", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.24424f, 1.149471f, 0.50776f), UnityEngine.Quaternion.identity, new UnityEngine.Vector3(0.07527f, 0.08557f, 0.05914f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_wrap_2", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.243945f, 1.156219f, 0.50776f), UnityEngine.Quaternion.Euler(0f, 0f, 352.4707f), new UnityEngine.Vector3(0.07527f, 0.08557f, 0.05914f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_wrap_4", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.24276f, 1.115f, 0.51728f), UnityEngine.Quaternion.Euler(1.93357f, 221.0385f, 94.06423f), new UnityEngine.Vector3(0.42537f, 0.08557f, 0.016362f)) != null) visualParts++;
                if (AddPrefabPathVisual(whiskB.transform, "w2_wrap_5", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.2425f, 1.1148f, 0.51474f), UnityEngine.Quaternion.Euler(3.869394f, 125.6892f, 87.70084f), new UnityEngine.Vector3(0.42537f, 0.044592f, 0.016362f)) != null) visualParts++;

                // Finalization is now performed on our own permanent hierarchy, not on a
                // transient Kitbash result. Decorative nail failure is explicitly non-fatal.
                FinalizeSaunaWrisks(wrisks.PiecePrefab);

                int totalRenderers = visualRoot.GetComponentsInChildren<Renderer>(true).Length;
                Jotunn.Logger.LogInfo(
                    $"sauna_wrisks direct visual ready: parts={visualParts}, renderers={totalRenderers}");

                // Jotunn requires an icon at registration, but the real one is rendered
                // from the model only once the world is loaded. Borrow a vanilla icon until then.
                Sprite icon = FindFirstPieceIcon(
                    "piece_walltorch", "piece_chair", "piece_stool", "piece_table_round");
                _wrisksFallbackIcon = icon;
                wrisks.Piece.m_icon = icon;
                if (wrisks.Piece.m_icon == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_wrisks: no fallback icon available; piece will still be registered");
                }

                if (!PieceManager.Instance.AddPiece(wrisks))
                {
                    Jotunn.Logger.LogError("sauna_wrisks: PieceManager rejected the piece");
                    return;
                }

                _wrisksPrefab = wrisks.PiecePrefab;
                ApplyRecipeToPiece(_wrisksPrefab, WhisksRecipeValue, DefaultWhisksRecipe, "sauna_wrisks");
                Jotunn.Logger.LogInfo("sauna_wrisks registered (direct visual, no Kitbash)");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"sauna_wrisks failed: {ex}");
            }
        }

        /// <summary>
        /// Copies wall-only placement rules from the vanilla Sconce, but not its fire, mesh, or fuel logic.
        /// </summary>
        private void ApplyWallMountedBehavior(GameObject targetPrefab)
        {
            GameObject donor = PrefabManager.Instance.GetPrefab("piece_walltorch");
            if (donor == null)
            {
                Jotunn.Logger.LogWarning("sauna_wrisks: piece_walltorch not found; wall-only placement not copied");
                return;
            }

            Piece sourcePiece = donor.GetComponent<Piece>();
            Piece targetPiece = targetPrefab.GetComponent<Piece>();

            if (sourcePiece != null && targetPiece != null)
            {
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
                targetPiece.m_placeEffect = sourcePiece.m_placeEffect;

                Jotunn.Logger.LogInfo(
                    $"sauna_wrisks wall profile: groundPiece={targetPiece.m_groundPiece}, " +
                    $"groundOnly={targetPiece.m_groundOnly}, notOnFloor={targetPiece.m_notOnFloor}, " +
                    $"inCeilingOnly={targetPiece.m_inCeilingOnly}, noClipping={targetPiece.m_noClipping}");
            }

            WearNTear sourceWnt = donor.GetComponent<WearNTear>();
            WearNTear targetWnt = targetPrefab.GetComponent<WearNTear>();

            if (sourceWnt != null && targetWnt != null)
            {
                targetWnt.m_health = sourceWnt.m_health;
                targetWnt.m_materialType = sourceWnt.m_materialType;
                targetWnt.m_damages = sourceWnt.m_damages;
                targetWnt.m_burnable = sourceWnt.m_burnable;
                targetWnt.m_noSupportWear = sourceWnt.m_noSupportWear;
                targetWnt.m_noRoofWear = sourceWnt.m_noRoofWear;
                targetWnt.m_supports = sourceWnt.m_supports;
                targetWnt.m_destroyedEffect = sourceWnt.m_destroyedEffect;
                targetWnt.m_hitEffect = sourceWnt.m_hitEffect;
                targetWnt.m_switchEffect = sourceWnt.m_switchEffect;
            }
        }

        /// <summary>
        /// After kitbash completes:
        /// - disable wind, weather, and near-camera culling only on material copies used by these whisks;
        /// - remove technical and donor colliders;
        /// - add exactly two BoxColliders matching the Unity setup;
        /// - add the exact in-game bronze nail mesh.
        /// </summary>
        private static void FinalizeSaunaWrisks(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            Transform visualRoot = prefab.transform.Find("sauna_wrisks_visual_root");
            if (visualRoot == null)
            {
                Jotunn.Logger.LogWarning("sauna_wrisks: visual root not found during finalization");
                return;
            }

            // Each step is isolated. A decorative resource lookup must never abort the Piece
            // registration (this was the weak point exposed by the tester's Linux/Jotunn setup).
            try
            {
                MakeSaunaWrisksVegetationStatic(visualRoot);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning(
                    "sauna_wrisks: vegetation finalization failed (non-fatal): " +
                    ex.GetBaseException().Message);
            }

            try
            {
                AddSaunaBronzeNail(visualRoot);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning(
                    "sauna_wrisks: bronze nail skipped (non-fatal): " +
                    ex.GetBaseException().Message);
            }

            try
            {
                Collider[] oldColliders = prefab.GetComponentsInChildren<Collider>(true);
                foreach (Collider col in oldColliders)
                {
                    if (col != null)
                    {
                        UnityEngine.Object.DestroyImmediate(col);
                    }
                }

                BoxCollider box1 = visualRoot.gameObject.AddComponent<BoxCollider>();
                box1.isTrigger = false;
                box1.center = new UnityEngine.Vector3(0.003f, 1.71f, 0.555f);
                box1.size = new UnityEngine.Vector3(0.03f, 0.22f, 0.11f);

                BoxCollider box2 = visualRoot.gameObject.AddComponent<BoxCollider>();
                box2.isTrigger = false;
                box2.center = new UnityEngine.Vector3(0.003f, 1.56f, 0.56f);
                box2.size = new UnityEngine.Vector3(0.03f, 0.19f, 0.2f);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning(
                    "sauna_wrisks: collider finalization failed (non-fatal): " +
                    ex.GetBaseException().Message);
            }

            Jotunn.Logger.LogInfo(
                $"sauna_wrisks finalized: renderers={visualRoot.GetComponentsInChildren<Renderer>(true).Length}, " +
                "2 exact Unity colliders requested, static vegetation, nail optional");
        }

        /// <summary>
        /// Creates private Material copies ONLY for Custom/Vegetation inside sauna_wrisks.
        /// Vanilla materials in ObjectDB/ZNetScene are never modified.
        /// </summary>
        private static void MakeSaunaWrisksVegetationStatic(Transform visualRoot)
        {
            Dictionary<Material, Material> privateCopies =
                new Dictionary<Material, Material>();

            int rendererCount = 0;
            int materialCount = 0;

            foreach (Renderer renderer in visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null ||
                    (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)))
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    Material original = materials[i];

                    if (original == null ||
                        original.shader == null ||
                        !string.Equals(
                            original.shader.name,
                            "Custom/Vegetation",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Material localCopy;
                    if (!privateCopies.TryGetValue(original, out localCopy))
                    {
                        localCopy = new Material(original);
                        localCopy.name = "SaunaWrisks_" + original.name;

                        SetSaunaMaterialFloat(localCopy, "_SwaySpeed", 0f);
                        SetSaunaMaterialFloat(localCopy, "_SwayDistance", 0f);
                        SetSaunaMaterialFloat(localCopy, "_RippleSpeed", 0f);
                        SetSaunaMaterialFloat(localCopy, "_RippleDistance", 0f);
                        SetSaunaMaterialFloat(localCopy, "_PushDistance", 0f);

                        SetSaunaMaterialFloat(localCopy, "_CamCull", 0f);
                        localCopy.DisableKeyword("_CAMCULL_ON");

                        SetSaunaMaterialFloat(localCopy, "_AddRain", 0f);
                        SetSaunaMaterialFloat(localCopy, "_AddSnow", 0f);
                        localCopy.DisableKeyword("_ADDRAIN_ON");
                        localCopy.DisableKeyword("_ADDSNOW_ON");

                        privateCopies.Add(original, localCopy);
                        materialCount++;
                    }

                    materials[i] = localCopy;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    rendererCount++;
                }
            }

            Jotunn.Logger.LogInfo(
                $"sauna_wrisks vegetation isolated: renderers={rendererCount}, " +
                $"privateMaterials={materialCount}");
        }

        private static void SetSaunaMaterialFloat(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        /// <summary>
        /// Adds the EXACT nail mesh used in the Unity setup:
        /// Assets/GameElements/Pieces/_res/Ashlandsfortress/model/
        /// Cube.012_Cube.010_Cube.012_Cube.010.asset
        ///
        /// No vanilla geometry or material is modified:
        /// - the mesh is only read and assigned to our MeshFilter;
        /// - the "bronze" material is only read and assigned to our MeshRenderer.
        /// </summary>
        private static void AddSaunaBronzeNail(Transform visualRoot)
        {
            const string NailMeshName = "Cube.012_Cube.010_Cube.012_Cube.010";

            if (visualRoot == null || visualRoot.Find(NailMeshName) != null)
            {
                return;
            }

            Mesh nailMesh = FindLoadedMeshByName(NailMeshName);
            if (nailMesh == null)
            {
                Jotunn.Logger.LogWarning(
                    $"sauna_wrisks: exact nail mesh '{NailMeshName}' not found; nail skipped");
                return;
            }

            Material bronzeMaterial = FindLoadedMaterialByName("bronze");
            if (bronzeMaterial == null)
            {
                Jotunn.Logger.LogWarning(
                    "sauna_wrisks: material 'bronze' not found; nail skipped");
                return;
            }

            GameObject nail = new GameObject(NailMeshName);
            nail.transform.SetParent(visualRoot, false);

            // Exact Transform from the final Unity dump.
            nail.transform.localPosition =
                new UnityEngine.Vector3(0.04884f, 1.7402f, 0.56407f);
            nail.transform.localRotation =
                UnityEngine.Quaternion.Euler(0.000001f, -0.000001f, 104.0243f);
            nail.transform.localScale =
                new UnityEngine.Vector3(0.042052f, 0.06583f, 0.042678f);

            MeshFilter mf = nail.AddComponent<MeshFilter>();
            mf.sharedMesh = nailMesh;

            MeshRenderer mr = nail.AddComponent<MeshRenderer>();
            mr.sharedMaterial = bronzeMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            // The nail needs no separate collider; the whole piece uses two manually defined colliders.
            nail.layer = visualRoot.gameObject.layer;

            Jotunn.Logger.LogInfo(
                $"sauna_wrisks exact nail: mesh='{nailMesh.name}', material='{bronzeMaterial.name}', " +
                $"verts={nailMesh.vertexCount}");
        }

        /// <summary>
        /// First search the vanilla prefabs actually loaded in ZNetScene.
        /// This is the safest path because it uses the real in-game Mesh and copies nothing from the RIP project.
        /// If the mesh belongs to a location/resource asset and is not present in ZNetScene,
        /// fall back to the list of already loaded Mesh objects from Resources.FindObjectsOfTypeAll.
        /// </summary>
        private static Mesh FindLoadedMeshByName(string meshName)
        {
            if (string.IsNullOrEmpty(meshName))
            {
                return null;
            }

            if (ZNetScene.instance != null && ZNetScene.instance.m_prefabs != null)
            {
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null)
                    {
                        continue;
                    }

                    foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf != null && mf.sharedMesh != null &&
                            string.Equals(mf.sharedMesh.name, meshName, StringComparison.Ordinal))
                        {
                            Jotunn.Logger.LogInfo(
                                $"sauna_wrisks nail mesh source prefab: {prefab.name}/{GetTransformPath(prefab.transform, mf.transform)}");
                            return mf.sharedMesh;
                        }
                    }

                    foreach (SkinnedMeshRenderer smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (smr != null && smr.sharedMesh != null &&
                            string.Equals(smr.sharedMesh.name, meshName, StringComparison.Ordinal))
                        {
                            Jotunn.Logger.LogInfo(
                                $"sauna_wrisks nail mesh source prefab: {prefab.name}/{GetTransformPath(prefab.transform, smr.transform)}");
                            return smr.sharedMesh;
                        }
                    }
                }
            }

            Mesh[] loadedMeshes = UnityEngine.Resources.FindObjectsOfTypeAll<Mesh>();
            foreach (Mesh mesh in loadedMeshes)
            {
                if (mesh != null && string.Equals(mesh.name, meshName, StringComparison.Ordinal))
                {
                    Jotunn.Logger.LogInfo(
                        $"sauna_wrisks nail mesh found in loaded resources: {mesh.name}");
                    return mesh;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a vanilla material by name without changing any of its properties.
        /// Assigning sharedMaterial is safe because our Renderer only references the same material.
        /// </summary>
        private static Material FindLoadedMaterialByName(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
            {
                return null;
            }

            Material[] loadedMaterials = UnityEngine.Resources.FindObjectsOfTypeAll<Material>();
            foreach (Material material in loadedMaterials)
            {
                if (material != null &&
                    string.Equals(material.name, materialName, StringComparison.OrdinalIgnoreCase))
                {
                    return material;
                }
            }

            // Fallback to the Bronze item if the material has not appeared in the global loaded-material list yet.
            GameObject bronzePrefab = PrefabManager.Instance.GetPrefab("Bronze");
            if (bronzePrefab != null)
            {
                foreach (Renderer renderer in bronzePrefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material != null &&
                            string.Equals(material.name, materialName, StringComparison.OrdinalIgnoreCase))
                        {
                            return material;
                        }
                    }
                }
            }

            return null;
        }

        private static string GetTransformPath(Transform root, Transform target)
        {
            if (root == null || target == null || target == root)
            {
                return string.Empty;
            }

            string path = target.name;
            Transform current = target.parent;

            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
