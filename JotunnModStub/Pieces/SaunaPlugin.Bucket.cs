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
        /// Visual model of the sauna bucket only: bucket, ladle, water, and an oak whisk inside.
        /// No interaction or gameplay logic is implemented here.
        /// </summary>
        private void AddSaunaBucket()
        {
            try
            {
                PieceConfig config = new PieceConfig
                {
                    Name = "$piece_sauna_bucket",
                    Description = "$piece_sauna_bucket_desc",
                    PieceTable = PieceTables.Hammer,
                    Category = PieceCategories.Furniture,
                    CraftingStation = CraftingStations.Workbench,
                    Requirements = ParseRecipeConfig(
                        BucketRecipeValue,
                        DefaultBucketRecipe,
                        "sauna_bucket")
                };

                CustomPiece bucket = new CustomPiece("sauna_bucket", true, config);
                bucket.FixReference = true;

                // The technical cube from empty CustomPiece is needed as the Piece base but must not be visible.
                MeshRenderer baseRenderer = bucket.PiecePrefab.GetComponent<MeshRenderer>();
                if (baseRenderer != null)
                {
                    baseRenderer.enabled = false;
                }

                ApplyBucketFloorPlacement(bucket.PiecePrefab);

                // Root Transform copied exactly from the Unity setup.
                GameObject visualRoot = new GameObject("sauna_bucket_visual_root");
                visualRoot.transform.SetParent(bucket.PiecePrefab.transform, false);
                visualRoot.transform.localPosition =
                    new UnityEngine.Vector3(-0.171f, 1.282f, 2.045f);
                visualRoot.transform.localRotation = UnityEngine.Quaternion.identity;
                visualRoot.transform.localScale = UnityEngine.Vector3.one;

                // ------------------------------------------------------------
                // BUCKET AND LADLE
                // ------------------------------------------------------------

                // The mug is not a separate ZNet prefab; it is a child of prop_Tankard.
                // Therefore resolve exactly prop_Tankard/fi_vil_container_ale_mug.
                GameObject mugVisual = AddLoadedMeshVisual(
                    visualRoot.transform,
                    "fi_vil_container_ale_mug",
                    null,
                    "prop_Tankard",
                    new UnityEngine.Vector3(0.698f, -1.287f, -1.557f),
                    UnityEngine.Quaternion.Euler(-0.000032f, 318.3131f, 0.000031f),
                    new UnityEngine.Vector3(1.069931f, 1.08225f, 1.0486f));

                // The second material slot in prop_Tankard is the mead liquid.
                // The mug is EMPTY BY DEFAULT: the liquid slot receives a fully transparent
                // private material copy. Preview or future infusion then replaces this slot
                // with the appropriate mead material.
                Renderer mugLiquidRenderer = PrepareMugEmpty(mugVisual);

                AddLoadedMeshVisual(
                    visualRoot.transform,
                    "meadcauldron_broken.003_Cube.014 (2)",
                    "meadcauldron_broken.003_Cube.014",
                    null,
                    new UnityEngine.Vector3(0.824516f, -0.732448f, -2.750395f),
                    UnityEngine.Quaternion.Euler(358.2979f, 243.1259f, 332.7032f),
                    new UnityEngine.Vector3(0.810494f, 1.256816f, 0.729256f));

                // The bucket itself is a SoftReference asset from Fantasy Interiors.
                // It may not be present among already loaded ZNetScene/Resources objects,
                // so load it through the vanilla SoftReferenceableAssets system.
                AddSoftReferencedBucketVisual(
                    visualRoot.transform,
                    new UnityEngine.Vector3(0.214f, -1.296f, -1.948f),
                    UnityEngine.Quaternion.identity,
                    new UnityEngine.Vector3(2.746935f, 2.44169f, 2.789405f));

                // The ladle bowl uses the specific "default" mesh from HotTub_Collider,
                // with material forced to woodmetal as in the Unity setup.
                AddHotTubColliderVisual(
                    visualRoot.transform,
                    "default",
                    new UnityEngine.Vector3(0.510797f, -1.104215f, -2.561304f),
                    UnityEngine.Quaternion.Euler(357.713f, 240.7826f, 57.50727f),
                    new UnityEngine.Vector3(0.15094f, 0.19033f, 0.155687f));

                // The upper normal-water surface is the new Cylinder from the Unity dump.
                GameObject waterSurface = AddBucketWaterSurface(
                    visualRoot.transform,
                    new UnityEngine.Vector3(0.211f, -0.839f, -1.942f),
                    UnityEngine.Quaternion.identity,
                    new UnityEngine.Vector3(0.828164f, 0.002846f, 0.844305f));

                // The lower Cylinder_low is hidden by default. When mead is added,
                // it is enabled and shows through the top water as a diluted mixture.
                GameObject infusionSurface = AddBucketInfusionSurface(
                    visualRoot.transform,
                    new UnityEngine.Vector3(0.211f, -0.847f, -1.942f),
                    UnityEngine.Quaternion.identity,
                    new UnityEngine.Vector3(0.828164f, 0.002846f, 0.844305f));

                // Bucket gameplay state: ZDO stores the selected mead type and UseItem
                // allows the player to pour it directly from inventory.
                if (bucket.PiecePrefab.GetComponent<SaunaBucket>() == null)
                {
                    bucket.PiecePrefab.AddComponent<SaunaBucket>();
                }

                // Bind the liquid parts. With the editor closed, the visual comes from ZDO:
                // 0 = water + empty mug; 1/2/3 = corresponding mead.
                SaunaBucketLiquidVisual bucketLiquidVisual =
                    bucket.PiecePrefab.AddComponent<SaunaBucketLiquidVisual>();
                bucketLiquidVisual.Configure(
                    waterSurface != null ? waterSurface.GetComponent<MeshRenderer>() : null,
                    infusionSurface != null ? infusionSurface.GetComponent<MeshRenderer>() : null,
                    mugLiquidRenderer,
                    1);

                // ------------------------------------------------------------
                // OAK WHISK INSIDE THE BUCKET
                // ------------------------------------------------------------

                GameObject oakWhisk = new GameObject("oak_wrisk");
                oakWhisk.transform.SetParent(visualRoot.transform, false);
                oakWhisk.transform.localPosition =
                    new UnityEngine.Vector3(-4.219f, 3.939f, 0.517f);
                oakWhisk.transform.localRotation =
                    UnityEngine.Quaternion.Euler(12.38466f, 180f, 141.7517f);
                oakWhisk.transform.localScale =
                    new UnityEngine.Vector3(3.975999f, 4.348866f, 4.108019f);

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Plane_062", "Plane_062", "Oak_Sapling",
                    new UnityEngine.Vector3(0.046f, 1.179049f, 0.825689f),
                    UnityEngine.Quaternion.Euler(314.7355f, 329.9362f, 312.5505f),
                    new UnityEngine.Vector3(0.258351f, 0.256806f, 0.259855f));

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Oak_Sapling001", "Oak_Sapling001", "Oak_Sapling",
                    new UnityEngine.Vector3(0.234228f, 1.253305f, 0.834163f),
                    UnityEngine.Quaternion.Euler(0f, 122.2157f, 0f),
                    new UnityEngine.Vector3(0.075168f, 0.097723f, 0.089222f));

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Plane_061", "Plane_061", "Oak_Sapling",
                    new UnityEngine.Vector3(0.301976f, 1.155071f, 0.864759f),
                    UnityEngine.Quaternion.Euler(351.9135f, 353.9866f, 37.26222f),
                    new UnityEngine.Vector3(0.257912f, 0.260185f, 0.256919f));

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Plane_058", "Plane_058", "Oak_Sapling",
                    new UnityEngine.Vector3(0.170801f, 1.095263f, 0.793844f),
                    UnityEngine.Quaternion.Euler(23.74482f, 52.64099f, 4.288773f),
                    new UnityEngine.Vector3(0.256095f, 0.262132f, 0.256851f));

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Plane_059", "Plane_059", "Oak_Sapling",
                    new UnityEngine.Vector3(0.222552f, 1.162431f, 0.738886f),
                    UnityEngine.Quaternion.Euler(21.43836f, 300.6433f, 324.7045f),
                    new UnityEngine.Vector3(0.258f, 0.260573f, 0.256456f));

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Plane_060", "Plane_060", "Oak_Sapling",
                    new UnityEngine.Vector3(0.2046f, 1.1921f, 0.8974f),
                    UnityEngine.Quaternion.Euler(320.7024f, 5.064512f, 353.5978f),
                    new UnityEngine.Vector3(0.25516f, 0.260683f, 0.259216f));

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Plane_060 (2)", "Plane_060", "Oak_Sapling",
                    new UnityEngine.Vector3(0.1529f, 1.2025f, 0.8251f),
                    UnityEngine.Quaternion.Euler(313.6828f, 298.696f, 342.3445f),
                    new UnityEngine.Vector3(0.25516f, 0.260683f, 0.259216f));

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Oak_Sapling001 (1)", "Oak_Sapling001", "Oak_Sapling",
                    new UnityEngine.Vector3(0.236828f, 1.251935f, 0.837939f),
                    UnityEngine.Quaternion.Euler(0f, 110.3317f, 0f),
                    new UnityEngine.Vector3(0.075259f, 0.097723f, 0.089115f));

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Oak_Sapling001 (3)", "Oak_Sapling001", "Oak_Sapling",
                    new UnityEngine.Vector3(0.228611f, 1.251603f, 0.838198f),
                    UnityEngine.Quaternion.Euler(2.105434f, 110.3317f, 2.393955f),
                    new UnityEngine.Vector3(0.075259f, 0.097723f, 0.089115f));

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Oak_Sapling001 (2)", "Oak_Sapling001", "Oak_Sapling",
                    new UnityEngine.Vector3(0.234381f, 1.254092f, 0.842921f),
                    UnityEngine.Quaternion.Euler(0f, 75.63401f, 0f),
                    new UnityEngine.Vector3(0.075292f, 0.097723f, 0.089076f));

                AddLoadedMeshVisual(
                    oakWhisk.transform, "Plane_060 (1)", "Plane_060", "Oak_Sapling",
                    new UnityEngine.Vector3(0.159711f, 1.180474f, 0.824456f),
                    UnityEngine.Quaternion.Euler(315.3681f, 291.1149f, 347.418f),
                    new UnityEngine.Vector3(0.256884f, 0.258953f, 0.25917f));

                // The oak-whisk wrapping uses the same vanilla hood
                // that already works on the birch whisks.
                AddPrefabPathVisual(
                    oakWhisk.transform, "hood (7)", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.2323f, 1.2741f, 0.833f),
                    UnityEngine.Quaternion.Euler(4.39467f, 0f, 0f),
                    new UnityEngine.Vector3(0.073999f, 0.083264f, 0.064492f));

                AddPrefabPathVisual(
                    oakWhisk.transform, "hood (6)", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.2321f, 1.2624f, 0.8348f),
                    UnityEngine.Quaternion.Euler(348.3143f, 0.114809f, 357.6877f),
                    new UnityEngine.Vector3(0.076819f, 0.082224f, 0.05776f));

                AddPrefabPathVisual(
                    oakWhisk.transform, "hood (8)", "HelmetSweatBand", "attach/hood",
                    new UnityEngine.Vector3(0.2324f, 1.2821f, 0.833f),
                    UnityEngine.Quaternion.Euler(0f, 0f, 352.4707f),
                    new UnityEngine.Vector3(0.08079f, 0.073925f, 0.063359f));

                // Oak leaves indoors should behave like an object, not a growing tree:
                // use private vegetation-material copies with wind, weather,
                // and near-camera culling disabled.
                MakeSaunaWrisksVegetationStatic(visualRoot.transform);

                // Remove technical and donor colliders only inside our Piece.
                foreach (Collider col in bucket.PiecePrefab.GetComponentsInChildren<Collider>(true))
                {
                    if (col != null)
                    {
                        UnityEngine.Object.DestroyImmediate(col);
                    }
                }

                // Exact collider from the bucket Unity setup.
                BoxCollider bucketCollider = visualRoot.AddComponent<BoxCollider>();
                bucketCollider.isTrigger = false;
                bucketCollider.center = new UnityEngine.Vector3(0.21f, -0.95f, -1.94f);
                bucketCollider.size = new UnityEngine.Vector3(0.97f, 0.7f, 1f);

                // IMPORTANT: the collider is on visualRoot, while SaunaBucket/ZNetView live
                // on the Piece root. Put a tiny interaction proxy directly on the collider
                // object so vanilla hover/E/UseItem can always resolve Interactable/Hoverable.
                if (visualRoot.GetComponent<SaunaBucketInteractionProxy>() == null)
                {
                    visualRoot.AddComponent<SaunaBucketInteractionProxy>();
                }

                // Jotunn requires an icon at registration, but the real one is rendered
                // from the model only once the world is loaded. Borrow a vanilla icon until then.
                bucket.Piece.m_icon = FindFirstPieceIcon(
                    "piece_chest_wood",
                    "piece_table_round",
                    "piece_stool",
                    "piece_cauldron");
                if (bucket.Piece.m_icon == null)
                {
                    Jotunn.Logger.LogError("sauna_bucket: no icon available");
                    return;
                }

                if (!PieceManager.Instance.AddPiece(bucket))
                {
                    Jotunn.Logger.LogError("sauna_bucket: PieceManager rejected the piece");
                    return;
                }

                _bucketPrefab = bucket.PiecePrefab;
                ApplyRecipeToPiece(_bucketPrefab, BucketRecipeValue, DefaultBucketRecipe, "sauna_bucket");

                Jotunn.Logger.LogInfo("sauna_bucket registered (gameplay + liquid infusion)");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"sauna_bucket failed: {ex}");
            }
        }

        /// <summary>
        /// Floor-piece placement profile. Can be placed on terrain or wooden floors,
        /// while vertical walls are rejected by m_notOnTiltingSurface.
        /// </summary>
        private void ApplyBucketFloorPlacement(GameObject targetPrefab)
        {
            GameObject donor = null;
            string[] donors = { "piece_table_round", "piece_stool", "piece_chest_wood", "piece_chair" };

            foreach (string donorName in donors)
            {
                donor = PrefabManager.Instance.GetPrefab(donorName);
                if (donor != null)
                {
                    break;
                }
            }

            Piece targetPiece = targetPrefab != null ? targetPrefab.GetComponent<Piece>() : null;
            Piece sourcePiece = donor != null ? donor.GetComponent<Piece>() : null;

            if (targetPiece != null && sourcePiece != null)
            {
                targetPiece.m_groundPiece = false;
                targetPiece.m_allowAltGroundPlacement = false;
                targetPiece.m_groundOnly = false;
                targetPiece.m_cultivatedGroundOnly = false;
                targetPiece.m_waterPiece = false;
                targetPiece.m_clipGround = sourcePiece.m_clipGround;
                targetPiece.m_clipEverything = sourcePiece.m_clipEverything;
                targetPiece.m_noClipping = sourcePiece.m_noClipping;
                targetPiece.m_noInWater = true;
                targetPiece.m_notOnWood = false;
                targetPiece.m_notOnTiltingSurface = true;
                targetPiece.m_notOnFloor = false;
                targetPiece.m_inCeilingOnly = false;
                targetPiece.m_onlyInTeleportArea = sourcePiece.m_onlyInTeleportArea;
                targetPiece.m_allowedInDungeons = sourcePiece.m_allowedInDungeons;
                targetPiece.m_spaceRequirement = 0f;
                targetPiece.m_placeEffect = sourcePiece.m_placeEffect;
            }

            WearNTear targetWnt = targetPrefab != null ? targetPrefab.GetComponent<WearNTear>() : null;
            WearNTear sourceWnt = donor != null ? donor.GetComponent<WearNTear>() : null;

            if (targetWnt != null && sourceWnt != null)
            {
                targetWnt.m_health = sourceWnt.m_health;
                targetWnt.m_materialType = sourceWnt.m_materialType;
                targetWnt.m_damages = sourceWnt.m_damages;
                targetWnt.m_burnable = sourceWnt.m_burnable;
                targetWnt.m_noSupportWear = sourceWnt.m_noSupportWear;
                targetWnt.m_noRoofWear = sourceWnt.m_noRoofWear;
                targetWnt.m_supports = false;
                targetWnt.m_destroyedEffect = sourceWnt.m_destroyedEffect;
                targetWnt.m_hitEffect = sourceWnt.m_hitEffect;
                targetWnt.m_switchEffect = sourceWnt.m_switchEffect;
            }
        }

        /// <summary>
        /// Creates a visual object from an already loaded vanilla MeshRenderer.
        /// Source Mesh and Material objects are not modified; they are only referenced.
        /// preferredPrefabName narrows the search when a mesh must come specifically from Oak_Sapling.
        /// </summary>
        private static GameObject AddLoadedMeshVisual(
            Transform parent,
            string objectName,
            string meshName,
            string preferredPrefabName,
            UnityEngine.Vector3 position,
            UnityEngine.Quaternion rotation,
            UnityEngine.Vector3 scale)
        {
            MeshFilter sourceMf;
            MeshRenderer sourceMr;

            if (!FindLoadedMeshRenderer(objectName, meshName, preferredPrefabName, out sourceMf, out sourceMr))
            {
                Jotunn.Logger.LogWarning(
                    $"sauna_bucket: visual source not found: object='{objectName}', mesh='{meshName}', prefab='{preferredPrefabName}'");
                return null;
            }

            GameObject go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.layer = parent.gameObject.layer;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = sourceMf.sharedMesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = sourceMr.sharedMaterials;
            mr.shadowCastingMode = sourceMr.shadowCastingMode;
            mr.receiveShadows = sourceMr.receiveShadows;

            return go;
        }

        /// <summary>
        /// Materials used for sauna-bucket liquids.
        /// All are private copies of the vanilla mead material; vanilla assets are never modified.
        /// </summary>
        private static Material _saunaBucketSourceMeadMaterial;
        private static Material _saunaBucketWaterMaterial;
        private static Material _saunaBucketEmptyLiquidMaterial;
        private static readonly Material[] _saunaBucketMeadMaterials = new Material[4];
        private static readonly Material[] _saunaBucketMugMeadMaterials = new Material[4];

        private const float SaunaWaterGlossiness = 0.72f;

        private static void RememberBucketMeadSource(Material sourceLiquid)
        {
            if (_saunaBucketSourceMeadMaterial == null && sourceLiquid != null)
            {
                _saunaBucketSourceMeadMaterial = sourceLiquid;
            }
        }

        private static Material FindBucketMeadSource()
        {
            if (_saunaBucketSourceMeadMaterial != null)
            {
                return _saunaBucketSourceMeadMaterial;
            }

            Material source =
                FindLoadedMaterialByName("mead") ??
                FindLoadedMaterialByName("Mead");

            RememberBucketMeadSource(source);
            return source;
        }

        /// <summary>
        /// Makes the mug's second material slot fully transparent.
        /// This represents an empty mug, not water.
        /// Returns the renderer so preview/infusion can later fill it with mead.
        /// </summary>
        private static Renderer PrepareMugEmpty(GameObject mugVisual)
        {
            if (mugVisual == null)
            {
                return null;
            }

            foreach (Renderer renderer in mugVisual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length < 2 || materials[1] == null)
                {
                    continue;
                }

                RememberBucketMeadSource(materials[1]);

                Material empty = GetBucketEmptyLiquidMaterial();
                if (empty == null)
                {
                    Jotunn.Logger.LogWarning(
                        $"sauna_bucket: could not create empty mug liquid from '{materials[1].name}'");
                    return renderer;
                }

                Material[] local = (Material[])materials.Clone();
                string oldName = local[1] != null ? local[1].name : "<null>";
                local[1] = empty;
                renderer.sharedMaterials = local;

                Jotunn.Logger.LogInfo(
                    $"sauna_bucket: mug slot 1 '{oldName}' -> EMPTY on renderer '{renderer.name}'");
                return renderer;
            }

            Jotunn.Logger.LogWarning(
                "sauna_bucket: mug exists, but no liquid material slot was found");
            return null;
        }

        private static void SetTransparentStandardMode(Material material)
        {
            if (material == null || !material.HasProperty("_Mode"))
            {
                return;
            }

            material.SetFloat("_Mode", 3f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }

        private static void SetMaterialColor(Material material, UnityEngine.Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            else
            {
                material.color = color;
            }
        }

        internal static Material GetBucketWaterMaterial()
        {
            if (_saunaBucketWaterMaterial != null)
            {
                return _saunaBucketWaterMaterial;
            }

            Material source = FindBucketMeadSource();
            if (source == null)
            {
                return null;
            }

            Material water = new Material(source);
            water.name = "SaunaBucket_Water";

            // Remove the amber mead texture and normal map for water.
            if (water.HasProperty("_MainTex"))
            {
                water.SetTexture("_MainTex", Texture2D.whiteTexture);
            }
            if (water.HasProperty("_BumpMap"))
            {
                water.SetTexture("_BumpMap", null);
            }
            water.DisableKeyword("_NORMALMAP");

            if (water.HasProperty("_Metallic"))
            {
                water.SetFloat("_Metallic", 0f);
            }
            if (water.HasProperty("_Glossiness"))
            {
                water.SetFloat("_Glossiness", SaunaWaterGlossiness);
            }

            SetTransparentStandardMode(water);
            _saunaBucketWaterMaterial = water;
            ApplyBucketLiquidTuningToMaterials();

            Jotunn.Logger.LogInfo(
                $"sauna_bucket: private water material created from '{source.name}', shader='{source.shader?.name}'");
            return water;
        }

        internal static Material GetBucketEmptyLiquidMaterial()
        {
            if (_saunaBucketEmptyLiquidMaterial != null)
            {
                return _saunaBucketEmptyLiquidMaterial;
            }

            Material source = FindBucketMeadSource();
            if (source == null)
            {
                return null;
            }

            Material empty = new Material(source);
            empty.name = "SaunaBucket_EmptyLiquid";

            // Keep the shader but make the contents fully invisible.
            if (empty.HasProperty("_MainTex"))
            {
                empty.SetTexture("_MainTex", Texture2D.whiteTexture);
            }
            SetTransparentStandardMode(empty);
            SetMaterialColor(empty, new UnityEngine.Color(1f, 1f, 1f, 0f));

            _saunaBucketEmptyLiquidMaterial = empty;
            return empty;
        }

        internal static Material GetBucketMeadMaterial(int type)
        {
            type = Mathf.Clamp(type, 1, 3);

            if (_saunaBucketMeadMaterials[type] != null)
            {
                return _saunaBucketMeadMaterials[type];
            }

            Material source = FindBucketMeadSource();
            if (source == null)
            {
                return null;
            }

            Material mead = new Material(source);
            mead.name = type == 1
                ? "SaunaBucket_Mead_Poison"
                : type == 2
                    ? "SaunaBucket_Mead_Frost"
                    : "SaunaBucket_Mead_Fire";

            // Unlike water, keep the vanilla mead texture here.
            SetTransparentStandardMode(mead);
            _saunaBucketMeadMaterials[type] = mead;
            ApplyBucketLiquidTuningToMaterials();
            return mead;
        }

        internal static Material GetBucketMugMeadMaterial(int type)
        {
            type = Mathf.Clamp(type, 1, 3);

            if (_saunaBucketMugMeadMaterials[type] != null)
            {
                return _saunaBucketMugMeadMaterials[type];
            }

            Material source = FindBucketMeadSource();
            if (source == null)
            {
                return null;
            }

            Material mead = new Material(source);
            mead.name = type == 1
                ? "SaunaBucket_MugMead_Poison"
                : type == 2
                    ? "SaunaBucket_MugMead_Frost"
                    : "SaunaBucket_MugMead_Fire";

            // In the mug keep the vanilla mead texture, but Alpha is always 0.70:
            // this represents visually undiluted mead.
            SetTransparentStandardMode(mead);
            SetMaterialColor(mead, SaunaBucketLiquidTuning.MugMeadColor(type));
            _saunaBucketMugMeadMaterials[type] = mead;
            return mead;
        }

        internal static void ApplyBucketLiquidTuningToMaterials()
        {
            if (_saunaBucketWaterMaterial != null)
            {
                SetMaterialColor(_saunaBucketWaterMaterial, SaunaBucketLiquidTuning.WaterColor);
            }

            for (int type = 1; type <= 3; ++type)
            {
                Material mead = _saunaBucketMeadMaterials[type];
                if (mead != null)
                {
                    SetMaterialColor(mead, SaunaBucketLiquidTuning.MeadColor(type));
                }

                Material mugMead = _saunaBucketMugMeadMaterials[type];
                if (mugMead != null)
                {
                    SetMaterialColor(mugMead, SaunaBucketLiquidTuning.MugMeadColor(type));
                }
            }

            if (_saunaBucketEmptyLiquidMaterial != null)
            {
                SetMaterialColor(
                    _saunaBucketEmptyLiquidMaterial,
                    new UnityEngine.Color(1f, 1f, 1f, 0f));
            }
        }

        internal static void RefreshBucketLiquidVisuals()
        {
            SaunaBucketLiquidVisual.RefreshAll();
        }

        private static GameObject AddBucketWaterSurface(
            Transform parent,
            UnityEngine.Vector3 position,
            UnityEngine.Quaternion rotation,
            UnityEngine.Vector3 scale)
        {
            Material waterMaterial = GetBucketWaterMaterial();
            if (waterMaterial == null)
            {
                Jotunn.Logger.LogWarning(
                    "sauna_bucket: water surface skipped because base material 'mead' was not found");
                return null;
            }

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Cylinder";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.layer = parent.gameObject.layer;

            Collider primitiveCollider = go.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(primitiveCollider);
            }

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = waterMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            Jotunn.Logger.LogInfo(
                $"sauna_bucket: water Cylinder added at {position} scale={scale}");
            return go;
        }

        private static GameObject AddBucketInfusionSurface(
            Transform parent,
            UnityEngine.Vector3 position,
            UnityEngine.Quaternion rotation,
            UnityEngine.Vector3 scale)
        {
            // The material is selected by SaunaBucketLiquidVisual.ApplyPreview().
            Material previewMead = GetBucketMeadMaterial(
                Mathf.Clamp(SaunaBucketLiquidTuning.Preview, 1, 3));

            if (previewMead == null)
            {
                Jotunn.Logger.LogWarning(
                    "sauna_bucket: infusion Cylinder_low skipped because base material 'mead' was not found");
                return null;
            }

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Cylinder_low";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.layer = parent.gameObject.layer;

            Collider primitiveCollider = go.GetComponent<Collider>();
            if (primitiveCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(primitiveCollider);
            }

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = previewMead;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = SaunaBucketLiquidTuning.Preview != 0;
            }

            Jotunn.Logger.LogInfo(
                $"sauna_bucket: infusion Cylinder_low added at {position} scale={scale}, preview={SaunaBucketLiquidTuning.Preview}");
            return go;
        }

        /// <summary>
        /// Loads the exact fi_vil_container_bucket01 asset.
        /// First use PrefabManager.Cache; starting with Jotunn 2.19.2 the cache
        /// can resolve a Mesh by SoftReference Name from the mesh list.
        /// If that fails, use the exact asset path taken from the AssetRipper project
        /// and load the GameObject directly through the SoftReferenceableAssets runtime mapping.
        /// </summary>
        private static GameObject AddSoftReferencedBucketVisual(
            Transform parent,
            UnityEngine.Vector3 position,
            UnityEngine.Quaternion rotation,
            UnityEngine.Vector3 scale)
        {
            const string softReferenceName = "fi_vil_container_bucket01";
            const string exactAssetPath =
                "Assets/3rd party/Fantasy_Interiors/Villages_&_Towns/Meshes/Props/Containers/fi_vil_container_bucket01.prefab";

            Mesh sourceMesh = null;
            Material sourceMaterial = null;
            UnityEngine.Rendering.ShadowCastingMode shadowMode =
                UnityEngine.Rendering.ShadowCastingMode.On;
            bool receiveShadows = true;

            // 1. Primary path: Jotunn cache by SoftReference Name.
            try
            {
                sourceMesh = PrefabManager.Cache.GetPrefab<Mesh>(softReferenceName);
                if (sourceMesh != null)
                {
                    Jotunn.Logger.LogInfo(
                        $"sauna_bucket: bucket mesh resolved by PrefabManager.Cache: '{sourceMesh.name}'");
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning(
                    $"sauna_bucket: PrefabManager.Cache mesh lookup failed: {ex.GetBaseException().Message}");
            }

            // 2. Fallback: exact path -> AssetID -> SoftReference<GameObject>.Load().
            GameObject sourcePrefab = null;
            if (sourceMesh == null)
            {
                sourcePrefab = LoadGameObjectByExactSoftAssetPath(exactAssetPath);
                if (sourcePrefab != null)
                {
                    MeshFilter sourceMf = sourcePrefab.GetComponentInChildren<MeshFilter>(true);
                    MeshRenderer sourceMr = sourceMf != null ? sourceMf.GetComponent<MeshRenderer>() : null;

                    if (sourceMf != null)
                    {
                        sourceMesh = sourceMf.sharedMesh;
                    }

                    if (sourceMr != null)
                    {
                        if (sourceMr.sharedMaterials != null && sourceMr.sharedMaterials.Length > 0)
                        {
                            sourceMaterial = sourceMr.sharedMaterials[0];
                        }
                        shadowMode = sourceMr.shadowCastingMode;
                        receiveShadows = sourceMr.receiveShadows;
                    }
                }
            }

            // The material on the source prefab is fi_village_wood.
            if (sourceMaterial == null)
            {
                try
                {
                    sourceMaterial = PrefabManager.Cache.GetPrefab<Material>("fi_village_wood");
                }
                catch
                {
                    // A normal fallback is available below.
                }
            }
            if (sourceMaterial == null)
            {
                sourceMaterial = FindLoadedMaterialByName("fi_village_wood");
            }

            if (sourceMesh == null)
            {
                Jotunn.Logger.LogWarning(
                    "sauna_bucket: fi_vil_container_bucket01 mesh still not found");
                return null;
            }

            if (sourceMaterial == null)
            {
                Jotunn.Logger.LogWarning(
                    "sauna_bucket: fi_village_wood material not found");
                return null;
            }

            GameObject go = new GameObject(softReferenceName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.layer = parent.gameObject.layer;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = sourceMesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = sourceMaterial;
            mr.shadowCastingMode = shadowMode;
            mr.receiveShadows = receiveShadows;

            Jotunn.Logger.LogInfo(
                $"sauna_bucket: exact bucket added: mesh='{sourceMesh.name}', material='{sourceMaterial.name}'");

            return go;
        }

        /// <summary>
        /// Finds an asset by exact path from manifest/manifest_extended rather than by name.
        /// Then creates SoftReference<GameObject> from the resolved AssetID and calls Load().
        /// Reflection is used so the project does not need a separate compile-time
        /// reference to SoftReferenceableAssets.dll.
        /// </summary>
        private static GameObject LoadGameObjectByExactSoftAssetPath(string requestedPath)
        {
            try
            {
                Type runtimeType = Type.GetType(
                    "SoftReferenceableAssets.Runtime, SoftReferenceableAssets");
                Type assetIdType = Type.GetType(
                    "SoftReferenceableAssets.AssetID, SoftReferenceableAssets");
                Type softReferenceOpenType = Type.GetType(
                    "SoftReferenceableAssets.SoftReference`1, SoftReferenceableAssets");

                if (runtimeType == null || assetIdType == null || softReferenceOpenType == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_bucket: SoftReferenceableAssets reflection types not found");
                    return null;
                }

                System.Reflection.MethodInfo mapMethod = runtimeType.GetMethod(
                    "GetAllAssetPathsInBundleMappedToAssetID",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);

                if (mapMethod == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_bucket: Runtime.GetAllAssetPathsInBundleMappedToAssetID() not found");
                    return null;
                }

                object mapObject = mapMethod.Invoke(null, null);
                System.Collections.IEnumerable entries = mapObject as System.Collections.IEnumerable;
                if (entries == null)
                {
                    Jotunn.Logger.LogWarning("sauna_bucket: asset path map is not enumerable");
                    return null;
                }

                string wanted = NormalizeAssetPath(requestedPath);
                object foundAssetId = null;
                string foundPath = null;

                foreach (object entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    Type entryType = entry.GetType();
                    System.Reflection.PropertyInfo keyProperty = entryType.GetProperty("Key");
                    System.Reflection.PropertyInfo valueProperty = entryType.GetProperty("Value");
                    if (keyProperty == null || valueProperty == null)
                    {
                        continue;
                    }

                    string path = keyProperty.GetValue(entry, null) as string;
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    string normalized = NormalizeAssetPath(path);
                    bool exact = string.Equals(normalized, wanted, StringComparison.OrdinalIgnoreCase);
                    bool suffix = normalized.EndsWith(
                        "/fi_vil_container_bucket01.prefab",
                        StringComparison.OrdinalIgnoreCase);

                    if (exact || suffix)
                    {
                        foundPath = path;
                        foundAssetId = valueProperty.GetValue(entry, null);
                        if (exact)
                        {
                            break;
                        }
                    }
                }

                if (foundAssetId == null)
                {
                    Jotunn.Logger.LogWarning(
                        $"sauna_bucket: exact asset path not present in runtime map: '{requestedPath}'");
                    return null;
                }

                Jotunn.Logger.LogInfo(
                    $"sauna_bucket: exact asset path resolved: '{foundPath}', AssetID={foundAssetId}");

                Type softReferenceType = softReferenceOpenType.MakeGenericType(typeof(GameObject));
                object softReference = null;

                foreach (System.Reflection.ConstructorInfo ctor in softReferenceType.GetConstructors())
                {
                    System.Reflection.ParameterInfo[] parameters = ctor.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == assetIdType)
                    {
                        softReference = ctor.Invoke(new[] { foundAssetId });
                        break;
                    }
                }

                if (softReference == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_bucket: SoftReference<GameObject>(AssetID) constructor not found");
                    return null;
                }

                System.Reflection.MethodInfo loadMethod =
                    softReferenceType.GetMethod("Load", Type.EmptyTypes);
                System.Reflection.PropertyInfo assetProperty =
                    softReferenceType.GetProperty("Asset");

                if (loadMethod == null || assetProperty == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_bucket: SoftReference Load/Asset API not found");
                    return null;
                }

                loadMethod.Invoke(softReference, null);
                GameObject asset = assetProperty.GetValue(softReference, null) as GameObject;

                if (asset == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_bucket: exact SoftReference loaded but Asset is null");
                    return null;
                }

                Jotunn.Logger.LogInfo(
                    $"sauna_bucket: exact path asset loaded: '{asset.name}'");
                return asset;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning(
                    $"sauna_bucket: exact-path loader failed: {ex.GetBaseException().Message}");
                return null;
            }
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim();
        }

        private static bool FindLoadedMeshRenderer(
            string objectName,
            string meshName,
            string preferredPrefabName,
            out MeshFilter sourceMf,
            out MeshRenderer sourceMr)
        {
            sourceMf = null;
            sourceMr = null;

            // First try the specific prefab when it is known.
            if (!string.IsNullOrEmpty(preferredPrefabName))
            {
                GameObject prefab = PrefabManager.Instance.GetPrefab(preferredPrefabName);
                if (prefab != null)
                {
                    foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        MeshRenderer mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
                        if (mf == null || mr == null || mf.sharedMesh == null)
                        {
                            continue;
                        }

                        bool objectMatch = string.IsNullOrEmpty(objectName) || mf.gameObject.name == objectName;
                        bool meshMatch = string.IsNullOrEmpty(meshName) || mf.sharedMesh.name == meshName;
                        if (objectMatch && meshMatch)
                        {
                            sourceMf = mf;
                            sourceMr = mr;
                            return true;
                        }
                    }
                }
            }

            // Then search all already loaded vanilla/render assets.
            MeshFilter[] filters = UnityEngine.Resources.FindObjectsOfTypeAll<MeshFilter>();
            foreach (MeshFilter mf in filters)
            {
                if (mf == null || mf.sharedMesh == null)
                {
                    continue;
                }

                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null)
                {
                    continue;
                }

                bool objectMatch = string.IsNullOrEmpty(objectName) || mf.gameObject.name == objectName;
                bool meshMatch = string.IsNullOrEmpty(meshName) || mf.sharedMesh.name == meshName;

                if (objectMatch && meshMatch)
                {
                    sourceMf = mf;
                    sourceMr = mr;
                    return true;
                }
            }

            // Final safe fallback: match mesh name without requiring object name.
            if (!string.IsNullOrEmpty(meshName))
            {
                foreach (MeshFilter mf in filters)
                {
                    if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name != meshName)
                    {
                        continue;
                    }

                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        sourceMf = mf;
                        sourceMr = mr;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// HotTub_Collider uses the generic mesh name "default", so search
        /// by hierarchy/parent to avoid selecting one of hundreds of unrelated default meshes.
        /// </summary>
        private static GameObject AddHotTubColliderVisual(
            Transform parent,
            string objectName,
            UnityEngine.Vector3 position,
            UnityEngine.Quaternion rotation,
            UnityEngine.Vector3 scale)
        {
            MeshFilter sourceMf = null;
            MeshRenderer sourceMr = null;

            // Attempt 1: inspect the HotTub_Collider asset/prefab itself.
            GameObject source = PrefabManager.Instance.GetPrefab("HotTub_Collider");
            if (source != null)
            {
                foreach (MeshFilter mf in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf != null && mf.sharedMesh != null &&
                        (mf.gameObject.name == objectName || mf.sharedMesh.name == objectName))
                    {
                        MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                        if (mr != null)
                        {
                            sourceMf = mf;
                            sourceMr = mr;
                            break;
                        }
                    }
                }
            }

            // Attempt 2: find a "default" object whose parent hierarchy contains HotTub_Collider.
            if (sourceMf == null)
            {
                foreach (MeshFilter mf in UnityEngine.Resources.FindObjectsOfTypeAll<MeshFilter>())
                {
                    if (mf == null || mf.sharedMesh == null || mf.gameObject.name != objectName)
                    {
                        continue;
                    }

                    bool hotTubAncestor = false;
                    Transform t = mf.transform.parent;
                    while (t != null)
                    {
                        if (t.name == "HotTub_Collider")
                        {
                            hotTubAncestor = true;
                            break;
                        }
                        t = t.parent;
                    }

                    if (!hotTubAncestor)
                    {
                        continue;
                    }

                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        sourceMf = mf;
                        sourceMr = mr;
                        break;
                    }
                }
            }

            if (sourceMf == null || sourceMr == null)
            {
                Jotunn.Logger.LogWarning("sauna_bucket: HotTub_Collider/default visual not found");
                return null;
            }

            Material woodmetal = FindLoadedMaterialByName("woodmetal");
            if (woodmetal == null)
            {
                Jotunn.Logger.LogWarning("sauna_bucket: material 'woodmetal' not found");
                return null;
            }

            GameObject go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.layer = parent.gameObject.layer;

            MeshFilter mfOut = go.AddComponent<MeshFilter>();
            mfOut.sharedMesh = sourceMf.sharedMesh;

            MeshRenderer mrOut = go.AddComponent<MeshRenderer>();
            mrOut.sharedMaterial = woodmetal;
            mrOut.shadowCastingMode = sourceMr.shadowCastingMode;
            mrOut.receiveShadows = sourceMr.receiveShadows;

            return go;
        }

        private static GameObject AddPrefabPathVisual(
            Transform parent,
            string objectName,
            string prefabName,
            string sourcePath,
            UnityEngine.Vector3 position,
            UnityEngine.Quaternion rotation,
            UnityEngine.Vector3 scale)
        {
            GameObject prefab = PrefabManager.Instance.GetPrefab(prefabName);
            Transform source = prefab != null ? prefab.transform.Find(sourcePath) : null;
            MeshFilter sourceMf = source != null ? source.GetComponent<MeshFilter>() : null;
            MeshRenderer sourceMr = source != null ? source.GetComponent<MeshRenderer>() : null;

            if (sourceMf == null || sourceMr == null || sourceMf.sharedMesh == null)
            {
                Jotunn.Logger.LogWarning(
                    $"sauna visual not found: {prefabName}/{sourcePath}");
                return null;
            }

            GameObject go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.layer = parent.gameObject.layer;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = sourceMf.sharedMesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();

            // v8 explicitly used the adult birch leaf material for these enlarged
            // Birch_Sapling leaf meshes. Keep the v9 direct hierarchy, but restore
            // that material override only for healthy/birchleafsXXX parts.
            Material materialOverride = null;
            if (string.Equals(prefabName, "Birch_Sapling", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(sourcePath) &&
                sourcePath.IndexOf("birchleafs", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                materialOverride = FindLoadedMaterialByName("birch_leaf");
                if (materialOverride == null)
                {
                    Jotunn.Logger.LogWarning(
                        $"sauna_wrisks: material 'birch_leaf' not found for {objectName}; " +
                        "falling back to Birch_Sapling material");
                }
            }

            if (materialOverride != null)
            {
                mr.sharedMaterial = materialOverride;
            }
            else
            {
                mr.sharedMaterials = sourceMr.sharedMaterials;
            }

            mr.shadowCastingMode = sourceMr.shadowCastingMode;
            mr.receiveShadows = sourceMr.receiveShadows;

            return go;
        }
    }
}
