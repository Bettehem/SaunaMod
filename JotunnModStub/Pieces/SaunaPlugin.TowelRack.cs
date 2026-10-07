// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using UnityEngine;

namespace SaunaMod
{
    internal partial class SaunaPlugin
    {
        private const string TowelRackVisualRootName = "sauna_towel_rack_visual_root";

        /// <summary>
        /// Wall-mounted towel rack: wolf-pelt towels on a wooden spear rail held by two iron nails,
        /// a birch bundle on a wooden holder and a shelf with folded cloth.
        /// Built directly from vanilla meshes, like the whisks, using the transforms of the
        /// latest "wolf towel" kitbash dump.
        /// </summary>
        private void AddSaunaTowelRack()
        {
            try
            {
                PieceConfig config = new PieceConfig
                {
                    Name = "$piece_sauna_towel_rack",
                    Description = "$piece_sauna_towel_rack_desc",
                    PieceTable = PieceTables.Hammer,
                    Category = PieceCategories.Furniture,
                    CraftingStation = CraftingStations.Workbench,
                    Requirements = ParseRecipeConfig(
                        TowelRackRecipeValue,
                        DefaultTowelRackRecipe,
                        "sauna_towel_rack")
                };

                CustomPiece rack = new CustomPiece("sauna_towel_rack", true, config);
                rack.FixReference = true;

                // The technical cube from an empty CustomPiece is needed as the Piece base but must not be visible.
                MeshRenderer baseRenderer = rack.PiecePrefab.GetComponent<MeshRenderer>();
                if (baseRenderer != null)
                {
                    baseRenderer.enabled = false;
                }

                // The rack hangs on a wall like the whisks; +X points away from the wall.
                ApplyWallMountedBehavior(rack.PiecePrefab);

                GameObject visualRoot = new GameObject(TowelRackVisualRootName);
                visualRoot.transform.SetParent(rack.PiecePrefab.transform, false);

                int parts = BuildTowelRackVisual(visualRoot.transform);

                // Birch leaves indoors must not sway in the wind or get wet.
                MakeSaunaWrisksVegetationStatic(visualRoot.transform);

                foreach (Collider col in rack.PiecePrefab.GetComponentsInChildren<Collider>(true))
                {
                    if (col != null)
                    {
                        UnityEngine.Object.DestroyImmediate(col);
                    }
                }

                // Exact colliders from the Unity setup: the rail with the towels and the shelf.
                BoxCollider railCollider = visualRoot.AddComponent<BoxCollider>();
                railCollider.center = new UnityEngine.Vector3(0.07f, -0.04f, 0.03f);
                railCollider.size = new UnityEngine.Vector3(0.09f, 1.08f, 1.83f);

                BoxCollider shelfCollider = visualRoot.AddComponent<BoxCollider>();
                shelfCollider.center = new UnityEngine.Vector3(0.39f, -0.58f, 0.5f);
                shelfCollider.size = new UnityEngine.Vector3(0.52f, 0.26f, 0.69f);

                Jotunn.Logger.LogInfo(
                    $"sauna_towel_rack visual ready: parts={parts}, " +
                    $"renderers={visualRoot.GetComponentsInChildren<Renderer>(true).Length}");

                // Jotunn requires an icon at registration, but the real one is rendered
                // from the model only once the world is loaded. Borrow a vanilla icon until then.
                rack.Piece.m_icon = FindFirstPieceIcon(
                    "rug_wolf", "piece_walltorch", "piece_chair", "piece_stool");
                if (rack.Piece.m_icon == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_towel_rack: no fallback icon available; piece will still be registered");
                }

                if (!PieceManager.Instance.AddPiece(rack))
                {
                    Jotunn.Logger.LogError("sauna_towel_rack: PieceManager rejected the piece");
                    return;
                }

                _towelRackPrefab = rack.PiecePrefab;
                ApplyRecipeToPiece(_towelRackPrefab, TowelRackRecipeValue, DefaultTowelRackRecipe, "sauna_towel_rack");
                Jotunn.Logger.LogInfo("sauna_towel_rack registered");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"sauna_towel_rack failed: {ex}");
            }
        }

        /// Adds every part of the rack and returns how many were found. A missing part only
        /// leaves a gap in the model; it never prevents the piece from registering.
        private static int BuildTowelRackVisual(Transform root)
        {
            int parts = 0;

            Material rugWolf = CreateOpaqueTowelMaterial();
            Material ironpit = FindPrefabMaterial("Ironpit", "HildirIronpit_m");
            Material cartWood = FindPrefabMaterial("Cart", "cart_wood");

            // ---- towels: two cape cloths and the folded cloth, all in wolf pelt ----

            GameObject towelA = new GameObject("clothes (3)");
            towelA.transform.SetParent(root, false);
            towelA.transform.localPosition = new UnityEngine.Vector3(0.381395f, -0.602563f, 0.496746f);
            towelA.transform.localRotation = UnityEngine.Quaternion.Euler(0f, 349.6712f, 0f);
            if (AddTowelRackPart(towelA.transform, "clothes", CapeClothSources, rugWolf,
                UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity, UnityEngine.Vector3.one) != null) parts++;

            GameObject towelB = new GameObject("clothes (4)");
            towelB.transform.SetParent(root, false);
            towelB.transform.localPosition = new UnityEngine.Vector3(0.397284f, -0.488468f, 0.49374f);
            towelB.transform.localRotation = UnityEngine.Quaternion.Euler(0f, 185.6409f, 0f);
            towelB.transform.localScale = new UnityEngine.Vector3(0.813808f, 1f, 0.82058f);
            if (AddTowelRackPart(towelB.transform, "clothes", CapeClothSources, rugWolf,
                UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity, UnityEngine.Vector3.one) != null) parts++;

            if (AddFoldedClothVisual(root, rugWolf,
                new UnityEngine.Vector3(0.071f, -1.301f, -0.17726f)) != null) parts++;

            // ---- rail: wooden spear on two iron nails ----

            GameObject spear = new GameObject("WoodenSpear (1)");
            spear.transform.SetParent(root, false);
            spear.transform.localPosition = new UnityEngine.Vector3(0.068284f, 0.461796f, -0.70659f);
            spear.transform.localRotation = UnityEngine.Quaternion.Euler(89.85196f, 0f, 0f);
            spear.transform.localScale = new UnityEngine.Vector3(1f, 0.71179f, 1f);
            if (AddTowelRackPart(spear.transform, "default",
                new[] { new PrefabMeshSource("SpearWood", "attach/default") }, null,
                UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity, UnityEngine.Vector3.one) != null) parts++;

            if (AddTowelRackNail(root, "Cube.012_Cube.010_Cube.012_Cube.010 (3)", ironpit,
                new UnityEngine.Vector3(0.2328f, 0.2076f, -0.9617f)) != null) parts++;
            if (AddTowelRackNail(root, "Cube.012_Cube.010_Cube.012_Cube.010 (4)", ironpit,
                new UnityEngine.Vector3(0.2298f, 0.2184f, 0.9095f)) != null) parts++;

            // ---- shelf: one plank of a quarter wood wall ----

            // The vanilla wall mesh is baked 50 m above its pivot and the prefab offsets it back
            // by -50, so the nested transform from the Unity scene is kept as is.
            GameObject shelf = new GameObject("wood_wall_quarter (1)");
            shelf.transform.SetParent(root, false);
            shelf.transform.localPosition = new UnityEngine.Vector3(0.086639f, -0.746563f, 1.197343f);
            shelf.transform.localRotation = UnityEngine.Quaternion.Euler(270.0791f, 269.064f, 0f);
            shelf.transform.localScale = new UnityEngine.Vector3(0.65023f, 0.667516f, 1f);
            if (AddTowelRackPart(shelf.transform, "mesh",
                new[] { new PrefabMeshSource("wood_wall_quarter", "New/mesh") }, null,
                new UnityEngine.Vector3(-1.086f, -50.126f, 0.069f),
                UnityEngine.Quaternion.Euler(0f, 180f, 0f),
                new UnityEngine.Vector3(1f, 1f, 0.23484f)) != null) parts++;

            // ---- whisk holder: wooden post and peg ----

            if (AddTowelRackPrimitive(root, "Cube (1)", PrimitiveType.Cube, cartWood,
                new UnityEngine.Vector3(0.0728f, 0.2804f, 0.6543f),
                UnityEngine.Quaternion.identity,
                new UnityEngine.Vector3(0.02193f, 0.17968f, 0.029351f)) != null) parts++;
            if (AddTowelRackPrimitive(root, "Cylinder (1)", PrimitiveType.Cylinder, cartWood,
                new UnityEngine.Vector3(0.0762f, 0.0924f, 0.6544f),
                UnityEngine.Quaternion.Euler(0f, 0f, 271.5904f),
                new UnityEngine.Vector3(0.237195f, -0.013765f, 0.136465f)) != null) parts++;

            // ---- birch bundle: the same vanilla parts as the sauna whisks ----

            if (AddPrefabPathVisual(root, "Birch_Sapling (3)", "Birch_Sapling", "healthy/Birch_Sapling",
                new UnityEngine.Vector3(0.084536f, 0.330248f, 0.850062f), UnityEngine.Quaternion.Euler(2.195159f, 350.4225f, 181.0705f), new UnityEngine.Vector3(0.3095f, 0.24435f, 0.38336f)) != null) parts++;
            if (AddPrefabPathVisual(root, "Birch_Sapling (4)", "Birch_Sapling", "healthy/Birch_Sapling",
                new UnityEngine.Vector3(0.112543f, 0.328653f, 0.827066f), UnityEngine.Quaternion.Euler(1.943101f, 287.1169f, 178.5204f), new UnityEngine.Vector3(0.30811f, 0.24435f, 0.38508f)) != null) parts++;
            if (AddPrefabPathVisual(root, "Birch_Sapling (5)", "Birch_Sapling", "healthy/Birch_Sapling",
                new UnityEngine.Vector3(0.129056f, 0.330533f, 0.87372f), UnityEngine.Quaternion.Euler(358.1906f, 102.2086f, 181.6405f), new UnityEngine.Vector3(0.30813f, 0.24435f, 0.38507f)) != null) parts++;
            if (AddPrefabPathVisual(root, "Birch_Sapling (6)", "Birch_Sapling", "healthy/Birch_Sapling",
                new UnityEngine.Vector3(0.094f, 0.33f, 0.864f), UnityEngine.Quaternion.Euler(2.207808f, 349.7366f, 181.0442f), new UnityEngine.Vector3(0.30948f, 0.24435f, 0.38338f)) != null) parts++;

            if (AddPrefabPathVisual(root, "birchleafs008", "Birch_Sapling", "healthy/birchleafs008",
                new UnityEngine.Vector3(-2.162f, 5.925f, 0.166f), UnityEngine.Quaternion.Euler(15.53243f, 292.0973f, 191.0491f), new UnityEngine.Vector3(3.49723f, 3.43362f, 3.51488f)) != null) parts++;
            if (AddPrefabPathVisual(root, "birchleafs009", "Birch_Sapling", "healthy/birchleafs009",
                new UnityEngine.Vector3(-2.873327f, 4.020744f, 1.452456f), UnityEngine.Quaternion.Euler(321.338f, 78.1166f, 190.3694f), new UnityEngine.Vector3(2.978881f, 2.943581f, 2.961481f)) != null) parts++;
            if (AddPrefabPathVisual(root, "birchleafs011", "Birch_Sapling", "healthy/birchleafs011",
                new UnityEngine.Vector3(1.414513f, 4.411274f, 2.477956f), UnityEngine.Quaternion.Euler(7.521119f, 121.9268f, 204.8037f), new UnityEngine.Vector3(2.967371f, 2.926061f, 2.990501f)) != null) parts++;

            // Leather wraps around the bundle and its hanging loop.
            if (AddPrefabPathVisual(root, "hood (4)", "HelmetSweatBand", "attach/hood",
                new UnityEngine.Vector3(0.0703f, 0.4425f, 0.6471f), UnityEngine.Quaternion.Euler(88.79245f, 84.28622f, 83.51717f), new UnityEngine.Vector3(0.263562f, 0.293313f, 0.68552f)) != null) parts++;
            if (AddPrefabPathVisual(root, "hood (5)", "HelmetSweatBand", "attach/hood",
                new UnityEngine.Vector3(0.123067f, 0.293359f, 0.850935f), UnityEngine.Quaternion.Euler(13.62179f, 286.693f, 176.1621f), new UnityEngine.Vector3(0.26362f, 0.29359f, 0.20807f)) != null) parts++;
            if (AddPrefabPathVisual(root, "hood (6)", "HelmetSweatBand", "attach/hood",
                new UnityEngine.Vector3(0.065727f, 0.48118f, 0.84192f), UnityEngine.Quaternion.Euler(80.0238f, 92.42675f, 90.36179f), new UnityEngine.Vector3(0.284849f, 0.464282f, 0.224132f)) != null) parts++;
            if (AddPrefabPathVisual(root, "hood (11)", "HelmetSweatBand", "attach/hood",
                new UnityEngine.Vector3(0.06834f, 0.4831f, 0.64368f), UnityEngine.Quaternion.Euler(80.0238f, 92.42675f, 90.36179f), new UnityEngine.Vector3(0.284849f, 0.464282f, 0.224132f)) != null) parts++;
            if (AddPrefabPathVisual(root, "hood (7)", "HelmetSweatBand", "attach/hood",
                new UnityEngine.Vector3(0.104475f, 0.378261f, 0.844907f), UnityEngine.Quaternion.Euler(8.613946f, 265.5535f, 269.5052f), new UnityEngine.Vector3(1.04777f, 0.29993f, 0.05108f)) != null) parts++;
            if (AddPrefabPathVisual(root, "hood (8)", "HelmetSweatBand", "attach/hood",
                new UnityEngine.Vector3(0.096884f, 0.376862f, 0.850147f), UnityEngine.Quaternion.Euler(359.3686f, 3.018661f, 278.6051f), new UnityEngine.Vector3(1.04777f, 0.16549f, 0.05087f)) != null) parts++;
            if (AddPrefabPathVisual(root, "hood (9)", "HelmetSweatBand", "attach/hood",
                new UnityEngine.Vector3(0.125313f, 0.269306f, 0.84924f), UnityEngine.Quaternion.Euler(1.943101f, 287.1169f, 178.5204f), new UnityEngine.Vector3(0.26363f, 0.29325f, 0.2083f)) != null) parts++;
            if (AddPrefabPathVisual(root, "hood (10)", "HelmetSweatBand", "attach/hood",
                new UnityEngine.Vector3(0.126191f, 0.246174f, 0.849425f), UnityEngine.Quaternion.Euler(1.943102f, 287.1169f, 170.991f), new UnityEngine.Vector3(0.26353f, 0.29336f, 0.2083f)) != null) parts++;

            return parts;
        }

        /// The "clothes" mesh is the dropped-item model shared by capes and chest armor.
        private static readonly PrefabMeshSource[] CapeClothSources =
        {
            new PrefabMeshSource("CapeLinen", "log"),
            new PrefabMeshSource("ArmorRagsChest", "log"),
            new PrefabMeshSource("CapeDeerHide", "log")
        };

        private struct PrefabMeshSource
        {
            public readonly string Prefab;
            public readonly string Path;

            public PrefabMeshSource(string prefab, string path)
            {
                Prefab = prefab;
                Path = path;
            }
        }

        /// Copies the mesh of the first source that exists. Nothing is copied from the donor
        /// besides the mesh, its materials (unless overridden) and its shadow settings.
        private static GameObject AddTowelRackPart(
            Transform parent,
            string objectName,
            PrefabMeshSource[] sources,
            Material materialOverride,
            UnityEngine.Vector3 position,
            UnityEngine.Quaternion rotation,
            UnityEngine.Vector3 scale)
        {
            foreach (PrefabMeshSource source in sources)
            {
                GameObject prefab = PrefabManager.Instance.GetPrefab(source.Prefab);
                Transform child = prefab != null ? prefab.transform.Find(source.Path) : null;
                MeshFilter sourceMf = child != null ? child.GetComponent<MeshFilter>() : null;
                MeshRenderer sourceMr = child != null ? child.GetComponent<MeshRenderer>() : null;

                if (sourceMf == null || sourceMr == null || sourceMf.sharedMesh == null)
                {
                    continue;
                }

                return AddMeshVisual(parent, objectName, sourceMf.sharedMesh,
                    materialOverride != null ? new[] { materialOverride } : sourceMr.sharedMaterials,
                    sourceMr.shadowCastingMode, sourceMr.receiveShadows,
                    position, rotation, scale);
            }

            Jotunn.Logger.LogWarning(
                $"sauna_towel_rack: part '{objectName}' not found in " +
                string.Join(", ", Array.ConvertAll(sources, s => s.Prefab + "/" + s.Path)));
            return null;
        }

        /// One of the two nails holding the rail: the Ashlands fortress nail used for the
        /// whisks, in Hildir's iron-pit material instead of bronze.
        private static GameObject AddTowelRackNail(
            Transform parent,
            string objectName,
            Material material,
            UnityEngine.Vector3 position)
        {
            Mesh nailMesh = FindLoadedMeshByName("Cube.012_Cube.010_Cube.012_Cube.010");
            if (nailMesh == null || material == null)
            {
                Jotunn.Logger.LogWarning(
                    $"sauna_towel_rack: nail skipped (mesh={(nailMesh != null)}, material={(material != null)})");
                return null;
            }

            return AddMeshVisual(parent, objectName, nailMesh, new[] { material },
                UnityEngine.Rendering.ShadowCastingMode.On, true,
                position,
                UnityEngine.Quaternion.Euler(0.000001f, -0.000001f, 104.0243f),
                new UnityEngine.Vector3(0.143966f, 0.231964f, 0.149565f));
        }

        /// Unity's built-in cube or cylinder, as used in the kitbash scene.
        private static GameObject AddTowelRackPrimitive(
            Transform parent,
            string objectName,
            PrimitiveType type,
            Material material,
            UnityEngine.Vector3 position,
            UnityEngine.Quaternion rotation,
            UnityEngine.Vector3 scale)
        {
            if (material == null)
            {
                Jotunn.Logger.LogWarning($"sauna_towel_rack: '{objectName}' skipped, cart_wood material not found");
                return null;
            }

            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(temp);

            return AddMeshVisual(parent, objectName, mesh, new[] { material },
                UnityEngine.Rendering.ShadowCastingMode.On, true,
                position, rotation, scale);
        }

        /// The folded cloth on the shelf. Its mesh exists only on Hildir's clothes racks, which are
        /// part of the Hildir_camp location rather than a ZNetScene prefab, so it is loaded through
        /// SoftReferenceableAssets like the bucket. The loaded asset stays referenced for the session.
        private static GameObject AddFoldedClothVisual(
            Transform parent,
            Material material,
            UnityEngine.Vector3 position)
        {
            string[][] sources =
            {
                new[] { "Assets/world/Props/HildirWagon/hildir_clothesrack1.prefab", "enable/clothes1" },
                new[] { "Assets/world/Locations/Meadows/Hildir_camp.prefab", "chest1/hildir_clothesrack1/enable/clothes1" }
            };

            foreach (string[] source in sources)
            {
                GameObject asset = LoadGameObjectByExactSoftAssetPath(source[0]);
                Transform child = asset != null ? asset.transform.Find(source[1]) : null;
                MeshFilter sourceMf = child != null ? child.GetComponent<MeshFilter>() : null;
                MeshRenderer sourceMr = child != null ? child.GetComponent<MeshRenderer>() : null;

                if (sourceMf == null || sourceMf.sharedMesh == null)
                {
                    continue;
                }

                Material[] materials = material != null
                    ? new[] { material }
                    : (sourceMr != null ? sourceMr.sharedMaterials : null);
                if (materials == null)
                {
                    continue;
                }

                Jotunn.Logger.LogInfo($"sauna_towel_rack: folded cloth from {source[0]}");
                return AddMeshVisual(parent, "clothes1 (1)", sourceMf.sharedMesh, materials,
                    UnityEngine.Rendering.ShadowCastingMode.On, true,
                    position, UnityEngine.Quaternion.identity, UnityEngine.Vector3.one);
            }

            Jotunn.Logger.LogWarning("sauna_towel_rack: folded cloth mesh not found; shelf stays empty");
            return null;
        }

        private static GameObject AddMeshVisual(
            Transform parent,
            string objectName,
            Mesh mesh,
            Material[] materials,
            UnityEngine.Rendering.ShadowCastingMode shadowMode,
            bool receiveShadows,
            UnityEngine.Vector3 position,
            UnityEngine.Quaternion rotation,
            UnityEngine.Vector3 scale)
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.layer = parent.gameObject.layer;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            mr.shadowCastingMode = shadowMode;
            mr.receiveShadows = receiveShadows;

            return go;
        }

        /// Keep the wolf texture and lighting, but render the entire cloth mesh regardless of texture alpha.
        private static Material CreateOpaqueTowelMaterial()
        {
            Material source = FindPrefabMaterial("rug_wolf", "rug_wolf");
            if (source == null)
            {
                return null;
            }

            Material material = new Material(source) { name = "sauna_towel_wolf_opaque" };
            // The game's rug shader can perform alpha clipping without the Standard shader keyword.
            // A zero threshold also keeps texels with zero alpha when the shader uses clip(alpha - cutoff).
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0f);
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            return material;
        }

        /// A material of a specific vanilla prefab, found by name. The material is only referenced, never changed.
        private static Material FindPrefabMaterial(string prefabName, string materialName)
        {
            GameObject prefab = PrefabManager.Instance.GetPrefab(prefabName);
            if (prefab != null)
            {
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
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

            Material loaded = FindLoadedMaterialByName(materialName);
            if (loaded == null)
            {
                Jotunn.Logger.LogWarning($"sauna_towel_rack: material '{materialName}' not found");
            }
            return loaded;
        }
    }
}
