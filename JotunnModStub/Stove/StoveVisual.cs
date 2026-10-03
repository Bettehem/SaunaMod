// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using Jotunn.Managers;
using System.Collections.Generic;
using UnityEngine;

namespace SaunaMod
{
    /// Procedurally builds the stove: a stone dome on a stone floor, with coals and
    /// the logs of the vanilla campfire inside.
    internal static class StoveVisual
    {
        public const string DomeName = "sauna_stone_visual";
        public const string FloorName = "sauna_floor_visual";
        public const string CoalName = "sauna_coal_visual";
        public const string CampfireName = "sauna_campfire_visual";

        /// Vanilla prefabs the stove borrows meshes from.
        private const string StonePrefab = "StoneRock";
        private const string CoalPrefab = "Coal";
        private const string CampfirePrefab = "fire_pit";

        /// Fixed random seed so the stove looks identical for every player.
        private const int Seed = 12345;

        // ---- dome ----
        private const float DomeStoneSize = 0.45f;
        private const float DomeBaseRadius = 0.65f;
        private const float DomeTopRadius = 0.31f;
        private const float DomeHeight = 0.80f;
        private const int DomeLayers = 5;
        private const int DomeStonesInBaseLayer = 12;
        private const float DomeSizeJitter = 0.10f;
        private const float DomeTilt = 30f;

        /// The opening for the fire faces +X and spans the lowest layers.
        private const float OpeningWidth = 65f;
        private const int OpeningLayers = 2;

        // ---- floor of ordinary stones ----
        private const float FloorStoneSize = 0.30f;
        private const int FloorStoneCount = 28;
        private const float FloorRadius = 0.56f;
        private const float FloorOffsetY = -0.06f;

        // ---- coals under the logs ----
        // Always present; they glow while the fire burns and while the stones are hot.
        private const float CoalSize = 0.24f;
        private const int CoalCount = 12;
        private const float CoalRadius = 0.23f;
        private const float CoalOffsetY = 0.06f;

        // ---- logs from the vanilla campfire ----
        // Only the log meshes are copied; flames, light and heat stay those of the stove itself.
        private const float CampfireScale = 0.95f;
        private static readonly Vector3 CampfireOffset = new Vector3(0.08f, 0.06f, -0.04f);
        private const float CampfireRotation = 305f;

        /// Color multiplier for the logs while the fire is out, so they look burnt.
        public const float CharredLogTint = 0.8f;

        /// Parts whose renderer or mesh name contains one of these are skipped:
        /// the stone ring and the flat ash/ember quads of the vanilla campfire.
        private static readonly string[] CampfireSkipKeywords = { "stone", "rock", "quad" };

        // ---- heat bands ----
        // Dome and floor are combined with one submesh per band, so each band can be tinted
        // separately by stone heat. Weight 1 = reddens fully, 0 = never reddens.
        // Each array matches the submeshes of the mesh built last.
        private const int FloorHeatRings = 4;
        public static float[] DomeBandWeights = new float[0];
        public static float[] FloorBandWeights = new float[0];

        // ---- fire, light, and heat ----
        public const float FireOffsetY = -0.20f;

        /// IMPORTANT: zero scales the heat-zone collider to zero and the stove stops providing heat.
        public const float FireScale = 0.25f;

        /// Scale of the visible flame particle systems (see KeepFlame).
        /// Does not change the size or position of the Fireplace root or heat zone.
        public const float FlameSize = 0.50f;

        /// Multiplier for the heat-zone radius. It is scaled separately from the flames,
        /// because FireScale shrinks the entire fire node together with the heat collider.
        /// A value of 4.0 with FireScale 0.25 restores the original iron firepit heat radius.
        public const float WarmthRadius = 2.5f;

        /// Separate vertical offset for visible flames only.
        /// Useful when the lower part of the fire visually starts below the stones.
        public const float FlameOffsetY = 0.56f;

        // ---- fallback build-piece icon view ----
        // The dome opening faces +X, so the desired view is selected with Yaw.
        public const float IconYaw = 270f;
        public const float IconPitch = 0f;

        private static Mesh _domeMesh;
        private static Material[] _domeMaterials;
        private static Mesh _floorMesh;
        private static Material[] _floorMaterials;
        private static Mesh _coalMesh;
        private static Material[] _coalMaterials;

        private static readonly Dictionary<Material, Material> _heatMaterials = new Dictionary<Material, Material>();
        private static bool _campfireLogged;

        /// Drops the shared meshes so the next Build creates them again,
        /// e.g. once the vanilla prefabs are available.
        public static void Invalidate()
        {
            if (_domeMesh != null)
            {
                Object.Destroy(_domeMesh);
                _domeMesh = null;
            }

            if (_floorMesh != null)
            {
                Object.Destroy(_floorMesh);
                _floorMesh = null;
            }

            if (_coalMesh != null)
            {
                Object.Destroy(_coalMesh);
                _coalMesh = null;
            }

            _domeMaterials = null;
            _floorMaterials = null;
            _coalMaterials = null;
        }

        private static GameObject FindPrefab(string prefabName)
        {
            GameObject prefab = ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab(prefabName)
                : null;

            return prefab != null ? prefab : PrefabManager.Instance.GetPrefab(prefabName);
        }

        /// The first mesh and material of a prefab, plus its largest dimension for scaling.
        private static bool GetSource(GameObject source, out Mesh mesh, out Material material, out float meshMaxSize)
        {
            mesh = null;
            material = null;
            meshMaxSize = 0f;

            if (source == null)
            {
                return false;
            }

            MeshFilter meshFilter = source.GetComponentInChildren<MeshFilter>(true);
            MeshRenderer meshRenderer = source.GetComponentInChildren<MeshRenderer>(true);

            if (meshFilter == null || meshFilter.sharedMesh == null ||
                meshRenderer == null || meshRenderer.sharedMaterials.Length == 0)
            {
                return false;
            }

            Vector3 size = meshFilter.sharedMesh.bounds.size;
            meshMaxSize = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            if (meshMaxSize <= 0.001f)
            {
                return false;
            }

            mesh = meshFilter.sharedMesh;
            material = meshRenderer.sharedMaterials[0];
            return true;
        }

        private static CombineInstance Place(Mesh mesh, Vector3 position, Quaternion rotation, float scale)
        {
            return new CombineInstance
            {
                mesh = mesh,
                subMeshIndex = 0,
                transform = Matrix4x4.TRS(position, rotation, Vector3.one * scale)
            };
        }

        /// One shared dome mesh for all sauna stoves in the world.
        public static Mesh GetDomeMesh(out Material[] materials)
        {
            materials = _domeMaterials;

            if (_domeMesh != null && _domeMaterials != null)
            {
                return _domeMesh;
            }

            GameObject source = FindPrefab(StonePrefab);
            Mesh stoneMesh;
            Material stoneMaterial;
            float meshMaxSize;

            if (!GetSource(source, out stoneMesh, out stoneMaterial, out meshMaxSize))
            {
                return null;
            }

            float baseScale = DomeStoneSize / meshMaxSize;

            // One band per layer, so every layer can get its own heat color.
            List<List<CombineInstance>> bands = new List<List<CombineInstance>>();
            List<float> bandWeights = new List<float>();
            int stoneCount = 0;

            Random.State savedRandom = Random.state;
            Random.InitState(Seed);

            for (int layer = 0; layer < DomeLayers; layer++)
            {
                float heightFraction = (float)layer / (DomeLayers - 1);

                // Heat rises: the top layer reddens fully, the bottom layer not at all.
                List<CombineInstance> layerStones = new List<CombineInstance>();
                bands.Add(layerStones);
                bandWeights.Add(heightFraction);

                float radius = Mathf.Lerp(DomeBaseRadius, DomeTopRadius, heightFraction);
                float y = DomeHeight * heightFraction;

                int count = Mathf.Max(3, Mathf.RoundToInt(DomeStonesInBaseLayer * radius / DomeBaseRadius));
                float angleStep = 360f / count;
                float angleOffset = layer * angleStep * 0.5f;

                for (int i = 0; i < count; i++)
                {
                    float angle = angleOffset + i * angleStep;

                    if (layer < OpeningLayers && IsInOpening(angle))
                    {
                        continue;
                    }

                    float radians = angle * Mathf.Deg2Rad;
                    Vector3 position = new Vector3(Mathf.Cos(radians) * radius, y, Mathf.Sin(radians) * radius);

                    Quaternion face = Quaternion.Euler(0f, -angle, 0f);
                    Quaternion lean = Quaternion.Euler(0f, 0f, DomeTilt * heightFraction);
                    Quaternion noise = Quaternion.Euler(
                        Random.Range(-20f, 20f),
                        Random.Range(-180f, 180f),
                        Random.Range(-20f, 20f));

                    float jitter = 1f + Random.Range(-DomeSizeJitter, DomeSizeJitter);

                    layerStones.Add(Place(stoneMesh, position, face * lean * noise, baseScale * jitter));
                    stoneCount++;
                }
            }

            Random.state = savedRandom;

            Mesh combined = CombineBands("sauna_dome", bands, bandWeights, out DomeBandWeights);
            if (combined == null)
            {
                return null;
            }

            _domeMesh = combined;
            _domeMaterials = HeatMaterials(stoneMaterial, DomeBandWeights.Length);
            materials = _domeMaterials;

            Jotunn.Logger.LogInfo($"dome mesh: {stoneCount} stones in {DomeBandWeights.Length} heat bands, " +
                $"{combined.vertexCount} verts, source={source.name}");

            return _domeMesh;
        }

        /// One dense layer of ordinary stones on the bottom of the stove.
        public static Mesh GetFloorMesh(out Material[] materials)
        {
            materials = _floorMaterials;

            if (_floorMesh != null && _floorMaterials != null)
            {
                return _floorMesh;
            }

            GameObject source = FindPrefab(StonePrefab);
            Mesh stoneMesh;
            Material stoneMaterial;
            float meshMaxSize;

            if (!GetSource(source, out stoneMesh, out stoneMaterial, out meshMaxSize))
            {
                return null;
            }

            float baseScale = FloorStoneSize / meshMaxSize;

            // Rings from the center outwards: the center under the fire reddens fully,
            // the outer ring not at all.
            List<List<CombineInstance>> bands = new List<List<CombineInstance>>();
            List<float> bandWeights = new List<float>();
            for (int ring = 0; ring < FloorHeatRings; ring++)
            {
                bands.Add(new List<CombineInstance>());
                bandWeights.Add(1f - (float)ring / (FloorHeatRings - 1));
            }

            Random.State savedRandom = Random.state;
            Random.InitState(Seed + 333);

            // Sunflower distribution: points fill the circle evenly instead of clumping in the center.
            const float goldenAngle = 137.50776f;

            for (int i = 0; i < FloorStoneCount; i++)
            {
                float areaFraction = (i + 0.5f) / FloorStoneCount;
                float radius = FloorRadius * Mathf.Sqrt(areaFraction);
                float radians = (i * goldenAngle + Random.Range(-10f, 10f)) * Mathf.Deg2Rad;

                Vector3 position = new Vector3(
                    Mathf.Cos(radians) * radius,
                    FloorOffsetY + Random.Range(-0.02f, 0.02f),
                    Mathf.Sin(radians) * radius);

                // Stones lie almost flat, with random Y rotation and a slight tilt.
                Quaternion rotation = Quaternion.Euler(
                    Random.Range(-8f, 8f),
                    Random.Range(-180f, 180f),
                    Random.Range(-8f, 8f));

                float jitter = 1f + Random.Range(-0.12f, 0.12f);

                int ring = Mathf.Min(FloorHeatRings - 1, Mathf.FloorToInt(radius / FloorRadius * FloorHeatRings));
                bands[ring].Add(Place(stoneMesh, position, rotation, baseScale * jitter));
            }

            Random.state = savedRandom;

            Mesh combined = CombineBands("sauna_floor", bands, bandWeights, out FloorBandWeights);
            if (combined == null)
            {
                return null;
            }

            _floorMesh = combined;
            _floorMaterials = HeatMaterials(stoneMaterial, FloorBandWeights.Length);
            materials = _floorMaterials;

            Jotunn.Logger.LogInfo($"floor mesh: {FloorStoneCount} stones in {FloorBandWeights.Length} heat bands, " +
                $"source={source.name}");

            return _floorMesh;
        }

        /// Pile of coals in the center, under the campfire logs.
        /// Uses the emission-enabled copy of the coal material so the coals can glow.
        public static Mesh GetCoalMesh(out Material[] materials)
        {
            materials = _coalMaterials;

            if (_coalMesh != null && _coalMaterials != null)
            {
                return _coalMesh;
            }

            GameObject source = FindPrefab(CoalPrefab);
            Mesh coalMesh;
            Material coalMaterial;
            float meshMaxSize;

            if (!GetSource(source, out coalMesh, out coalMaterial, out meshMaxSize))
            {
                Jotunn.Logger.LogWarning($"coal '{CoalPrefab}' mesh not found, stove has no coals");
                return null;
            }

            float baseScale = CoalSize / meshMaxSize;
            List<CombineInstance> coals = new List<CombineInstance>();

            Random.State savedRandom = Random.state;
            Random.InitState(Seed + 777);

            for (int i = 0; i < CoalCount; i++)
            {
                Vector2 flat = Random.insideUnitCircle * CoalRadius;
                Vector3 position = new Vector3(flat.x, CoalOffsetY + Random.Range(0f, CoalSize * 0.5f), flat.y);

                Quaternion rotation = Quaternion.Euler(
                    Random.Range(-180f, 180f),
                    Random.Range(-180f, 180f),
                    Random.Range(-180f, 180f));

                float jitter = 1f + Random.Range(-0.25f, 0.25f);

                coals.Add(Place(coalMesh, position, rotation, baseScale * jitter));
            }

            Random.state = savedRandom;

            Mesh combined = new Mesh();
            combined.name = "sauna_coal";
            combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combined.CombineMeshes(coals.ToArray(), true, true);
            combined.RecalculateBounds();

            _coalMesh = combined;
            _coalMaterials = HeatMaterials(coalMaterial, 1);
            materials = _coalMaterials;

            Jotunn.Logger.LogInfo($"coal mesh: {coals.Count} pieces, source={source.name}");

            return _coalMesh;
        }

        /// Merges every band into one submesh. Empty bands are skipped together with their weights.
        private static Mesh CombineBands(string name, List<List<CombineInstance>> bands,
            List<float> bandWeights, out float[] usedWeights)
        {
            List<CombineInstance> bandMeshes = new List<CombineInstance>();
            List<float> weights = new List<float>();

            for (int i = 0; i < bands.Count; i++)
            {
                if (bands[i].Count == 0)
                {
                    continue;
                }

                Mesh bandMesh = new Mesh();
                bandMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                bandMesh.CombineMeshes(bands[i].ToArray(), true, true);

                bandMeshes.Add(new CombineInstance { mesh = bandMesh, transform = Matrix4x4.identity });
                weights.Add(bandWeights[i]);
            }

            usedWeights = weights.ToArray();

            if (bandMeshes.Count == 0)
            {
                return null;
            }

            Mesh combined = new Mesh();
            combined.name = name;
            combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combined.CombineMeshes(bandMeshes.ToArray(), false, false);
            combined.RecalculateBounds();

            foreach (CombineInstance bandMesh in bandMeshes)
            {
                Object.Destroy(bandMesh.mesh);
            }

            return combined;
        }

        /// One material slot per band. All slots share one copy of the source material
        /// with emission switched on (black by default), so the glow can be driven per band
        /// through MaterialPropertyBlocks without touching the vanilla material.
        private static Material[] HeatMaterials(Material source, int count)
        {
            Material heatMaterial;
            if (!_heatMaterials.TryGetValue(source, out heatMaterial) || heatMaterial == null)
            {
                heatMaterial = new Material(source);
                heatMaterial.name = source.name + "_sauna_heat";

                if (heatMaterial.HasProperty("_EmissionColor"))
                {
                    heatMaterial.EnableKeyword("_EMISSION");
                    heatMaterial.SetColor("_EmissionColor", Color.black);
                }

                _heatMaterials[source] = heatMaterial;

                Jotunn.Logger.LogInfo($"heat material from {source.name}: shader={source.shader.name}, " +
                    $"color={heatMaterial.HasProperty("_Color")}, emission={heatMaterial.HasProperty("_EmissionColor")}");
            }

            Material[] materials = new Material[count];
            for (int i = 0; i < count; i++)
            {
                materials[i] = heatMaterial;
            }

            return materials;
        }

        public static void Build(Transform parent)
        {
            BuildPart(parent, FloorName, GetFloorMesh);
            BuildPart(parent, DomeName, GetDomeMesh);
            BuildPart(parent, CoalName, GetCoalMesh, castShadows: false);
            BuildCampfire(parent);
        }

        /// Destroy only completes at the end of the frame, so the old object
        /// remains among the children with the same name until then. Find may return
        /// that object instead of the new one, sending the burning/extinguished state
        /// to an object that is about to disappear and leaving the new one visible.
        /// Detach and hide the old object first, then destroy it.
        private static void RemoveOld(Transform parent, string name)
        {
            Transform old = parent.Find(name);
            while (old != null)
            {
                old.gameObject.SetActive(false);
                old.SetParent(null, false);
                Object.Destroy(old.gameObject);
                old = parent.Find(name);
            }
        }

        /// Copies the log meshes of the vanilla campfire into the stove.
        /// Particle systems, lights and effect areas are not MeshRenderers, so they are never copied.
        /// The vanilla campfire shows its burning logs only while lit; here every log stays,
        /// and SaunaStove darkens them when the fire is out.
        private static void BuildCampfire(Transform parent)
        {
            RemoveOld(parent, CampfireName);

            GameObject source = FindPrefab(CampfirePrefab);
            if (source == null)
            {
                return;
            }

            // Pieces keep worn/broken variants and LODs as extra renderers; copy only the intact, closest one.
            HashSet<Renderer> excluded = new HashSet<Renderer>();

            WearNTear wearNTear = source.GetComponent<WearNTear>();
            if (wearNTear != null)
            {
                foreach (GameObject variant in new[] { wearNTear.m_worn, wearNTear.m_broken })
                {
                    if (variant != null && variant != wearNTear.m_new)
                    {
                        excluded.UnionWith(variant.GetComponentsInChildren<Renderer>(true));
                    }
                }
            }

            foreach (LODGroup lodGroup in source.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = lodGroup.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                {
                    excluded.UnionWith(lods[i].renderers);
                }
            }

            GameObject root = new GameObject(CampfireName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = CampfireOffset;
            root.transform.localRotation = Quaternion.Euler(0f, CampfireRotation, 0f);
            root.transform.localScale = Vector3.one * CampfireScale;

            Matrix4x4 worldToSource = source.transform.worldToLocalMatrix;
            List<string> partLog = new List<string>();

            foreach (MeshRenderer sourceRenderer in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null)
                {
                    continue;
                }

                string label = $"{sourceRenderer.name}/{sourceFilter.sharedMesh.name}";
                string lowerLabel = label.ToLowerInvariant();

                bool skip = excluded.Contains(sourceRenderer) || !sourceRenderer.enabled;
                foreach (string keyword in CampfireSkipKeywords)
                {
                    skip |= lowerLabel.Contains(keyword);
                }

                partLog.Add((skip ? "  skip " : "  copy ") + label);
                if (skip)
                {
                    continue;
                }

                // The part's transform relative to the campfire root.
                Matrix4x4 partMatrix = worldToSource * sourceRenderer.transform.localToWorldMatrix;

                GameObject part = new GameObject(sourceRenderer.name);
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = partMatrix.GetColumn(3);
                part.transform.localRotation = partMatrix.rotation;
                part.transform.localScale = partMatrix.lossyScale;

                part.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
                partRenderer.sharedMaterials = sourceRenderer.sharedMaterials;

                // The logs sit inside the fire; their shadows would streak across the light from the opening.
                partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            if (!_campfireLogged)
            {
                _campfireLogged = true;
                Jotunn.Logger.LogInfo($"campfire parts from {source.name}:\n" + string.Join("\n", partLog));
            }
        }

        private delegate Mesh MeshGetter(out Material[] materials);

        private static void BuildPart(Transform parent, string name, MeshGetter getMesh, bool castShadows = true)
        {
            RemoveOld(parent, name);

            Material[] materials;
            Mesh mesh = getMesh(out materials);

            if (mesh == null || materials == null)
            {
                return;
            }

            GameObject part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = Vector3.zero;
            part.transform.localRotation = Quaternion.identity;

            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
            partRenderer.sharedMaterials = materials;
            partRenderer.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// Whether this vanilla fire particle system stays visible: the low and high flames do,
        /// smoke wisps and sparks do not.
        public static bool KeepFlame(string objectName)
        {
            return objectName.ToLowerInvariant().Contains("flames");
        }

        private static bool IsInOpening(float angle)
        {
            float signedAngle = Mathf.Repeat(angle, 360f);
            if (signedAngle > 180f)
            {
                signedAngle -= 360f;
            }

            return Mathf.Abs(signedAngle) < OpeningWidth * 0.5f;
        }
    }
}
