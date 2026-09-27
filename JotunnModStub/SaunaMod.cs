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
    /// Well Steamed cancels the Wet penalties to health, stamina, and eitr regeneration.
    /// It only removes the penalty by dividing by the same multiplier applied by Wet; it does not grant a bonus above normal.
    internal class SE_WellSteamed : SE_Stats
    {
        /// Reached TIME duration tier, 1..3.
        /// This is NOT the sauna tier: TimeTier only represents 300/600/900 seconds.
        /// It is stored directly in the effect because it cannot be derived reliably from remaining time:
        /// the remaining time is almost never exactly on a tier boundary.
        public int TimeTier;

        /// Tier of the sauna itself, 1..3. Do not confuse it with TimeTier:
        /// 1 = stove only, 2 = stove + whisks, 3 = stove + whisks + bucket.
        /// At the moment the tiers differ only in displayed state; the core effect mechanics are the same.
        public int SaunaTier = 1;

        public static float WetStaminaMultiplier = 1f;
        public static float WetHealthMultiplier = 1f;
        public static float WetEitrMultiplier = 1f;

        public override void ModifyStaminaRegen(ref float staminaRegen)
        {
            base.ModifyStaminaRegen(ref staminaRegen);

            if (SaunaTier >= 2 && IsWet() && WetStaminaMultiplier > 0f && WetStaminaMultiplier < 1f)
            {
                staminaRegen /= WetStaminaMultiplier;
            }
        }

        public override void ModifyHealthRegen(ref float healthRegen)
        {
            base.ModifyHealthRegen(ref healthRegen);

            if (SaunaTier >= 2 && IsWet() && WetHealthMultiplier > 0f && WetHealthMultiplier < 1f)
            {
                healthRegen /= WetHealthMultiplier;
            }
        }

        public override void ModifyEitrRegen(ref float eitrRegen)
        {
            base.ModifyEitrRegen(ref eitrRegen);

            if (SaunaTier >= 2 && IsWet() && WetEitrMultiplier > 0f && WetEitrMultiplier < 1f)
            {
                eitrRegen /= WetEitrMultiplier;
            }
        }

        private bool IsWet()
        {
            return m_character != null
                && m_character.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet);
        }
    }

    /// Procedurally builds the stone dome and the pile of hot stones inside it.
    internal static class StoveVisual
    {
        public const string DomeName = "sauna_stone_visual";
        public const string FloorName = "sauna_floor_visual";
        public const string LavaName = "sauna_lava_visual";
        public const string CoalName = "sauna_coal_visual";

        public static readonly List<GameObject> Candidates = new List<GameObject>();
        public static readonly List<GameObject> LavaCandidates = new List<GameObject>();

        /// Look up by name because list indexes may shift after patches or other mods.
        public const string PreferredStone = "StoneRock";
        public const string PreferredLava = "UnstableLavaRock";

        /// What replaces the hot stones when the stove goes out.
        public const string PreferredCoal = "Coal";

        private static readonly string[] LavaKeywords =
        {
            "lava", "magma", "ember", "molten", "glowing", "coal"
        };

        // ---- dome ----
        public static int StoneIndex = 0;
        public static float StoneSize = 0.45f;
        public static float BaseRadius = 0.65f;
        public static float TopRadius = 0.31f;
        public static float Height = 0.80f;
        public static int Layers = 5;
        public static int CountBase = 12;
        public static float OpeningWidth = 65f;
        public static int OpeningLayers = 2;
        public static float SizeJitter = 0.10f;
        public static float Tilt = 30f;
        public static int Seed = 12345;

        // ---- layer of ordinary stones on the bottom ----
        // Uses the same stone prefab as the dome, but size and density are configured separately.
        public static float FloorStoneSize = 0.30f;
        public static int FloorCount = 28;
        public static float FloorRadius = 0.56f;
        public static float FloorOffsetY = -0.06f;

        // ---- hot stones inside ----
        public static int LavaIndex = 0;
        public static float LavaSize = 0.24f;
        public static int LavaCount = 12;
        public static float LavaRadius = 0.33f;
        public static float LavaOffsetY = 0.04f;

        // ---- cooled coals replacing the hot stones ----
        // The pile uses the same positions as the hot stones,
        // so extinguishing changes only the appearance, not the shape.
        public static float CoalSize = 0.20f;

        // ---- fire, light, and heat ----
        public static float FireOffsetY = -0.20f;

        /// IMPORTANT: zero scales the heat-zone collider to zero and the stove stops providing heat.
        /// Use FlameSet, not scale, to hide the flames.
        public static float FireScale = 0.25f;

        /// Scale of the visible flame particle systems enabled by FlameSet.
        /// Does not change the size or position of the Fireplace root or heat zone.
        public static float FlameSize = 0.50f;

        /// Multiplier for the heat-zone radius. It is scaled separately from the flames,
        /// because FireScale shrinks the entire fire node together with the heat collider.
        /// A value of 4.0 with FireScale 0.25 restores the original iron firepit heat radius.
        public static float WarmthRadius = 2.5f;

        /// Separate vertical offset for visible flames only.
        /// Useful when the lower part of the fire visually starts below the stones.
        public static float FlameOffsetY = 0.56f;

        // ---- autogenerated build-piece icon view ----
        // The dome opening faces +X, so the desired view is selected with Yaw.
        public static float IconYaw = 270f;
        public static float IconPitch = 0f;

        /// 1 = try to include the flame in the icon. Particle systems need simulation time,
        /// so the result may depend on the Jotunn version.
        public static int IconFlame = 0;

        /// 0 = no flames, 1 = low flames, 2 = low + high flames, 3 = everything including smoke wisps.
        public static int FlameSet = 2;

        private static Mesh _domeMesh;
        private static Material[] _domeMaterials;
        private static Mesh _floorMesh;
        private static Material[] _floorMaterials;
        private static Mesh _lavaMesh;
        private static Material[] _lavaMaterials;
        private static Mesh _coalMesh;
        private static Material[] _coalMaterials;

        public static void CollectCandidates()
        {
            Candidates.Clear();
            LavaCandidates.Clear();

            if (ZNetScene.instance == null)
            {
                return;
            }

            List<KeyValuePair<float, GameObject>> stones = new List<KeyValuePair<float, GameObject>>();
            List<KeyValuePair<float, GameObject>> lava = new List<KeyValuePair<float, GameObject>>();

            foreach (GameObject p in ZNetScene.instance.m_prefabs)
            {
                if (p == null)
                {
                    continue;
                }

                MeshFilter mf = p.GetComponentInChildren<MeshFilter>(true);
                if (mf == null || mf.sharedMesh == null)
                {
                    continue;
                }

                UnityEngine.Vector3 size = mf.sharedMesh.bounds.size;
                float max = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

                if (max < 0.1f || max > 4f)
                {
                    continue;
                }

                string lower = p.name.ToLowerInvariant();

                if (lower.Contains("rock") || lower.Contains("stone"))
                {
                    stones.Add(new KeyValuePair<float, GameObject>(max, p));
                }

                foreach (string key in LavaKeywords)
                {
                    if (lower.Contains(key))
                    {
                        lava.Add(new KeyValuePair<float, GameObject>(max, p));
                        break;
                    }
                }
            }

            stones.Sort((a, b) => a.Key.CompareTo(b.Key));
            lava.Sort((a, b) => a.Key.CompareTo(b.Key));

            foreach (KeyValuePair<float, GameObject> kv in stones)
            {
                Candidates.Add(kv.Value);
            }

            foreach (KeyValuePair<float, GameObject> kv in lava)
            {
                LavaCandidates.Add(kv.Value);
            }

            StoneIndex = FindByName(Candidates, PreferredStone, StoneIndex, "stone");
            LavaIndex = FindByName(LavaCandidates, PreferredLava, LavaIndex, "lava");

            Invalidate();
        }

        private static int FindByName(List<GameObject> list, string wanted, int fallback, string label)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].name == wanted)
                {
                    Jotunn.Logger.LogInfo($"{label} '{wanted}' found at index {i} of {list.Count}");
                    return i;
                }
            }

            Jotunn.Logger.LogWarning($"{label} '{wanted}' NOT found among {list.Count}, using index {fallback}");
            return fallback;
        }

        public static string CurrentStoneName()
        {
            if (Candidates.Count == 0)
            {
                return "none";
            }

            return Candidates[Mathf.Clamp(StoneIndex, 0, Candidates.Count - 1)].name;
        }

        public static string CurrentLavaName()
        {
            List<GameObject> list = LavaCandidates.Count > 0 ? LavaCandidates : Candidates;

            if (list.Count == 0)
            {
                return "none";
            }

            return list[Mathf.Clamp(LavaIndex, 0, list.Count - 1)].name;
        }

        public static void Invalidate()
        {
            if (_domeMesh != null)
            {
                UnityEngine.Object.Destroy(_domeMesh);
                _domeMesh = null;
            }

            if (_floorMesh != null)
            {
                UnityEngine.Object.Destroy(_floorMesh);
                _floorMesh = null;
            }

            if (_lavaMesh != null)
            {
                UnityEngine.Object.Destroy(_lavaMesh);
                _lavaMesh = null;
            }

            if (_coalMesh != null)
            {
                UnityEngine.Object.Destroy(_coalMesh);
                _coalMesh = null;
            }

            _domeMaterials = null;
            _floorMaterials = null;
            _lavaMaterials = null;
            _coalMaterials = null;
        }

        private static bool GetSource(GameObject source, out Mesh mesh, out Material[] materials, out float meshMax)
        {
            mesh = null;
            materials = null;
            meshMax = 0f;

            if (source == null)
            {
                return false;
            }

            MeshFilter mf = source.GetComponentInChildren<MeshFilter>(true);
            MeshRenderer mr = source.GetComponentInChildren<MeshRenderer>(true);

            if (mf == null || mf.sharedMesh == null || mr == null || mr.sharedMaterials.Length == 0)
            {
                return false;
            }

            UnityEngine.Vector3 size = mf.sharedMesh.bounds.size;
            meshMax = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            if (meshMax <= 0.001f)
            {
                return false;
            }

            mesh = mf.sharedMesh;
            materials = new[] { mr.sharedMaterials[0] };
            return true;
        }

        /// One shared dome mesh for all sauna stoves in the world.
        public static Mesh GetDomeMesh(out Material[] materials)
        {
            materials = _domeMaterials;

            if (_domeMesh != null && _domeMaterials != null)
            {
                return _domeMesh;
            }

            if (Candidates.Count == 0)
            {
                return null;
            }

            GameObject source = Candidates[Mathf.Clamp(StoneIndex, 0, Candidates.Count - 1)];

            Mesh srcMesh;
            Material[] srcMaterials;
            float meshMax;

            if (!GetSource(source, out srcMesh, out srcMaterials, out meshMax))
            {
                return null;
            }

            float baseScale = StoneSize / meshMax;

            List<CombineInstance> parts = new List<CombineInstance>();

            // Fixed random seed so the stove looks identical for every player.
            UnityEngine.Random.State saved = UnityEngine.Random.state;
            UnityEngine.Random.InitState(Seed);

            int layers = Mathf.Max(1, Layers);
            int countBase = Mathf.Max(3, CountBase);

            for (int layer = 0; layer < layers; layer++)
            {
                float t = layers > 1 ? (float)layer / (layers - 1) : 0f;
                float radius = Mathf.Lerp(BaseRadius, TopRadius, t);
                float y = Height * t;

                int count = Mathf.Max(3, Mathf.RoundToInt(countBase * radius / Mathf.Max(0.01f, BaseRadius)));
                float step = 360f / count;
                float offset = layer * step * 0.5f;

                for (int i = 0; i < count; i++)
                {
                    float angle = offset + i * step;

                    if (layer < OpeningLayers && IsInOpening(angle))
                    {
                        continue;
                    }

                    float rad = angle * Mathf.Deg2Rad;
                    UnityEngine.Vector3 pos = new UnityEngine.Vector3(
                        Mathf.Cos(rad) * radius,
                        y,
                        Mathf.Sin(rad) * radius);

                    UnityEngine.Quaternion face = UnityEngine.Quaternion.Euler(0f, -angle, 0f);
                    UnityEngine.Quaternion lean = UnityEngine.Quaternion.Euler(0f, 0f, Tilt * t);
                    UnityEngine.Quaternion noise = UnityEngine.Quaternion.Euler(
                        UnityEngine.Random.Range(-20f, 20f),
                        UnityEngine.Random.Range(-180f, 180f),
                        UnityEngine.Random.Range(-20f, 20f));

                    float jitter = 1f + UnityEngine.Random.Range(-SizeJitter, SizeJitter);

                    CombineInstance ci = new CombineInstance();
                    ci.mesh = srcMesh;
                    ci.subMeshIndex = 0;
                    ci.transform = UnityEngine.Matrix4x4.TRS(
                        pos,
                        face * lean * noise,
                        UnityEngine.Vector3.one * baseScale * jitter);

                    parts.Add(ci);
                }
            }

            UnityEngine.Random.state = saved;

            if (parts.Count == 0)
            {
                return null;
            }

            Mesh combined = new Mesh();
            combined.name = "sauna_dome";
            combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combined.CombineMeshes(parts.ToArray(), true, true);
            combined.RecalculateBounds();

            _domeMesh = combined;
            _domeMaterials = srcMaterials;
            materials = _domeMaterials;

            Jotunn.Logger.LogInfo($"dome mesh: {parts.Count} stones, {combined.vertexCount} verts, " +
                $"source={source.name}");

            return _domeMesh;
        }

        /// One dense layer of ordinary stones on the bottom of the stove.
        /// The layout is deterministic, so it looks the same for every player.
        public static Mesh GetFloorMesh(out Material[] materials)
        {
            materials = _floorMaterials;

            if (_floorMesh != null && _floorMaterials != null)
            {
                return _floorMesh;
            }

            if (Candidates.Count == 0 || FloorCount <= 0)
            {
                return null;
            }

            GameObject source = Candidates[Mathf.Clamp(StoneIndex, 0, Candidates.Count - 1)];

            Mesh srcMesh;
            Material[] srcMaterials;
            float meshMax;

            if (!GetSource(source, out srcMesh, out srcMaterials, out meshMax))
            {
                return null;
            }

            float baseScale = FloorStoneSize / meshMax;
            List<CombineInstance> parts = new List<CombineInstance>();

            UnityEngine.Random.State saved = UnityEngine.Random.state;
            UnityEngine.Random.InitState(Seed + 333);

            // Sunflower distribution: points fill the circle evenly instead of clumping in the center.
            const float goldenAngle = 137.50776f;

            for (int i = 0; i < FloorCount; i++)
            {
                float t = (i + 0.5f) / Mathf.Max(1f, FloorCount);
                float radius = FloorRadius * Mathf.Sqrt(t);
                float angle = i * goldenAngle + UnityEngine.Random.Range(-10f, 10f);
                float rad = angle * Mathf.Deg2Rad;

                UnityEngine.Vector3 pos = new UnityEngine.Vector3(
                    Mathf.Cos(rad) * radius,
                    FloorOffsetY + UnityEngine.Random.Range(-0.02f, 0.02f),
                    Mathf.Sin(rad) * radius);

                // Stones lie almost flat, with random Y rotation and a slight tilt.
                UnityEngine.Quaternion rot = UnityEngine.Quaternion.Euler(
                    UnityEngine.Random.Range(-8f, 8f),
                    UnityEngine.Random.Range(-180f, 180f),
                    UnityEngine.Random.Range(-8f, 8f));

                float jitter = 1f + UnityEngine.Random.Range(-0.12f, 0.12f);

                CombineInstance ci = new CombineInstance();
                ci.mesh = srcMesh;
                ci.subMeshIndex = 0;
                ci.transform = UnityEngine.Matrix4x4.TRS(
                    pos, rot, UnityEngine.Vector3.one * baseScale * jitter);

                parts.Add(ci);
            }

            UnityEngine.Random.state = saved;

            if (parts.Count == 0)
            {
                return null;
            }

            Mesh combined = new Mesh();
            combined.name = "sauna_floor";
            combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combined.CombineMeshes(parts.ToArray(), true, true);
            combined.RecalculateBounds();

            _floorMesh = combined;
            _floorMaterials = srcMaterials;
            materials = _floorMaterials;

            Jotunn.Logger.LogInfo($"floor mesh: {parts.Count} stones, source={source.name}");

            return _floorMesh;
        }

        /// Pile of hot stones in the center.
        /// The cooled pile uses coal at the same positions as the hot stones.
        public static Mesh GetCoalMesh(out Material[] materials)
        {
            materials = _coalMaterials;

            if (_coalMesh != null && _coalMaterials != null)
            {
                return _coalMesh;
            }

            if (LavaCount <= 0)
            {
                return null;
            }

            GameObject source = ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab(PreferredCoal)
                : null;

            if (source == null)
            {
                source = PrefabManager.Instance.GetPrefab(PreferredCoal);
            }

            Mesh srcMesh;
            Material[] srcMaterials;
            float meshMax;

            if (!GetSource(source, out srcMesh, out srcMaterials, out meshMax))
            {
                Jotunn.Logger.LogWarning($"coal '{PreferredCoal}' mesh not found, cold stove keeps lava look");
                return null;
            }

            float baseScale = CoalSize / meshMax;

            List<CombineInstance> parts = new List<CombineInstance>();

            // Use the same seed and random-number order as GetLavaMesh,
            // so the coals appear exactly where the hot stones were.
            UnityEngine.Random.State saved = UnityEngine.Random.state;
            UnityEngine.Random.InitState(Seed + 777);

            for (int i = 0; i < LavaCount; i++)
            {
                UnityEngine.Vector2 flat = UnityEngine.Random.insideUnitCircle * LavaRadius;

                UnityEngine.Vector3 pos = new UnityEngine.Vector3(
                    flat.x,
                    LavaOffsetY + UnityEngine.Random.Range(0f, LavaSize * 0.5f),
                    flat.y);

                UnityEngine.Quaternion rot = UnityEngine.Quaternion.Euler(
                    UnityEngine.Random.Range(-180f, 180f),
                    UnityEngine.Random.Range(-180f, 180f),
                    UnityEngine.Random.Range(-180f, 180f));

                float jitter = 1f + UnityEngine.Random.Range(-0.25f, 0.25f);

                CombineInstance ci = new CombineInstance();
                ci.mesh = srcMesh;
                ci.subMeshIndex = 0;
                ci.transform = UnityEngine.Matrix4x4.TRS(
                    pos, rot, UnityEngine.Vector3.one * baseScale * jitter);

                parts.Add(ci);
            }

            UnityEngine.Random.state = saved;

            Mesh combined = new Mesh();
            combined.name = "sauna_coal";
            combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combined.CombineMeshes(parts.ToArray(), true, true);
            combined.RecalculateBounds();

            _coalMesh = combined;
            _coalMaterials = srcMaterials;
            materials = _coalMaterials;

            Jotunn.Logger.LogInfo($"coal mesh: {parts.Count} pieces, source={source.name}");

            return _coalMesh;
        }

        public static Mesh GetLavaMesh(out Material[] materials)
        {
            materials = _lavaMaterials;

            if (_lavaMesh != null && _lavaMaterials != null)
            {
                return _lavaMesh;
            }

            List<GameObject> list = LavaCandidates.Count > 0 ? LavaCandidates : Candidates;

            if (list.Count == 0 || LavaCount <= 0)
            {
                return null;
            }

            GameObject source = list[Mathf.Clamp(LavaIndex, 0, list.Count - 1)];

            Mesh srcMesh;
            Material[] srcMaterials;
            float meshMax;

            if (!GetSource(source, out srcMesh, out srcMaterials, out meshMax))
            {
                return null;
            }

            float baseScale = LavaSize / meshMax;

            List<CombineInstance> parts = new List<CombineInstance>();

            UnityEngine.Random.State saved = UnityEngine.Random.state;
            UnityEngine.Random.InitState(Seed + 777);

            for (int i = 0; i < LavaCount; i++)
            {
                UnityEngine.Vector2 flat = UnityEngine.Random.insideUnitCircle * LavaRadius;

                UnityEngine.Vector3 pos = new UnityEngine.Vector3(
                    flat.x,
                    LavaOffsetY + UnityEngine.Random.Range(0f, LavaSize * 0.5f),
                    flat.y);

                UnityEngine.Quaternion rot = UnityEngine.Quaternion.Euler(
                    UnityEngine.Random.Range(-180f, 180f),
                    UnityEngine.Random.Range(-180f, 180f),
                    UnityEngine.Random.Range(-180f, 180f));

                float jitter = 1f + UnityEngine.Random.Range(-0.25f, 0.25f);

                CombineInstance ci = new CombineInstance();
                ci.mesh = srcMesh;
                ci.subMeshIndex = 0;
                ci.transform = UnityEngine.Matrix4x4.TRS(
                    pos, rot, UnityEngine.Vector3.one * baseScale * jitter);

                parts.Add(ci);
            }

            UnityEngine.Random.state = saved;

            Mesh combined = new Mesh();
            combined.name = "sauna_lava";
            combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            combined.CombineMeshes(parts.ToArray(), true, true);
            combined.RecalculateBounds();

            _lavaMesh = combined;
            _lavaMaterials = srcMaterials;
            materials = _lavaMaterials;

            Jotunn.Logger.LogInfo($"lava mesh: {parts.Count} stones, source={source.name}");

            return _lavaMesh;
        }

        public static void Build(Transform parent)
        {
            BuildPart(parent, FloorName, GetFloorMesh);
            BuildPart(parent, DomeName, GetDomeMesh);
            BuildPart(parent, LavaName, GetLavaMesh);
            BuildPart(parent, CoalName, GetCoalMesh);
        }

        private delegate Mesh MeshGetter(out Material[] materials);

        private static void BuildPart(Transform parent, string name, MeshGetter getter)
        {
            // Destroy only completes at the end of the frame, so the old object
            // remains among the children with the same name until then. Find may return
            // that object instead of the new one, sending the burning/extinguished state
            // to an object that is about to disappear and leaving the new one visible.
            // Detach and hide the old object first, then destroy it.
            Transform old = parent.Find(name);
            while (old != null)
            {
                old.gameObject.SetActive(false);
                old.SetParent(null, false);
                UnityEngine.Object.Destroy(old.gameObject);
                old = parent.Find(name);
            }

            Material[] materials;
            Mesh mesh = getter(out materials);

            if (mesh == null || materials == null)
            {
                return;
            }

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = UnityEngine.Vector3.zero;
            go.transform.localRotation = UnityEngine.Quaternion.identity;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        /// Whether this particle system should be visible for the current FlameSet.
        public static bool KeepFlame(string objectName)
        {
            if (FlameSet <= 0)
            {
                return false;
            }

            if (FlameSet >= 3)
            {
                return true;
            }

            string lower = objectName.ToLowerInvariant();

            if (FlameSet >= 2 && lower.Contains("flames"))
            {
                return true;
            }

            return lower.Contains("low_flames");
        }

        private static bool IsInOpening(float angle)
        {
            float a = Mathf.Repeat(angle, 360f);
            if (a > 180f)
            {
                a -= 360f;
            }

            return Mathf.Abs(a) < OpeningWidth * 0.5f;
        }
    }

    /// Steam-cloud behavior. Applied to each cloud immediately after it spawns,
    /// so editor changes take effect on the next pour.
    ///
    /// Smoke.CustomUpdate moves each cloud by pulling its velocity toward m_vel
    /// with strength m_force every frame. The vertical target weakens with age,
    /// and cloud mass also drops, making old steam lighter and less energetic.
    internal static class SteamTuning
    {
        /// Seconds a cloud lives before it begins to fade. Keep this at least 25,
        /// otherwise one pour may not last long enough to gain Well Steamed.
        public const float MinLifetime = 25f;
        public static float Lifetime = 40f;

        /// Seconds a cloud spends fading after its lifetime ends.
        public static float FadeTime = 4.4f;

        /// Horizontal spreading speed. This is the main control for steam distribution.
        public static float Spread = 1.0f;

        /// Vertical rise speed. Lower values keep steam near body level instead of the ceiling.
        public static float Rise = 1.45f;

        /// Movement responsiveness: how quickly a cloud reaches its target velocity.
        public static float Force = 0.25f;

        /// How quickly overlapping clouds push away from one another.
        /// Vanilla uses 1.0, which can leave a fresh pour packed into a dense cluster.
        public static float Push = 0.5f;

        /// Physical and detection size of the cloud. This does not change its visual sprite size,
        /// because the game uses one visual smoke size globally.
        public static float CloudSize = 2.9f;

        /// Seconds for which steaming remembers brief loss of steam contact.
        /// Re-entering steam within this time preserves progress and counts the missed seconds.
        /// Otherwise steaming progress resets.
        public static float Grace = 3f;

        /// Base number of clouds per pour for stove-only or stove + whisks.
        public static int CloudsPerPour = 30;

        /// A full bucket at SaunaTier 3 creates a stronger steam burst.
        public static int CloudsPerPourWithBucket = 50;

        /// Global cap for smoke and steam clouds together.
        /// IMPORTANT: the game renderer shows no more than about 100 particles per world area,
        /// so values above 100 in one room may make steam invisible while it still functions.
        public static int MaxClouds = 100;

        /// Configures a freshly instantiated steam cloud.
        /// This runs after Awake and overrides the values set there.
        public static void Apply(GameObject cloud)
        {
            Smoke smoke = cloud.GetComponent<Smoke>();
            if (smoke == null)
            {
                return;
            }

            smoke.m_ttl = Mathf.Max(MinLifetime, Lifetime);
            smoke.m_fadetime = Mathf.Max(0.1f, FadeTime);
            smoke.m_force = Force;

            // Smoke.Awake forces vertical rise to 1.0 and ignores the prefab setting,
            // so the target velocity is built manually here.
            float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            smoke.m_vel = new UnityEngine.Vector3(
                Mathf.Cos(angle) * Spread,
                Rise,
                Mathf.Sin(angle) * Spread);

            if (smoke.m_body != null)
            {
                smoke.m_body.maxDepenetrationVelocity = Push;
            }

            cloud.transform.localScale = UnityEngine.Vector3.one * Mathf.Max(0.1f, CloudSize);
        }
    }

    /// Rendering parameters for autogenerated icons of new pieces.
    /// While the editor is enabled, changes apply live and immediately redraw hammer icons.
    internal static class SaunaPieceIconTuning
    {
        // Icon camera angles captured from the final editor dump.
        // Rendering is centered using Renderer.bounds after rotation,
        // so the model stays centered at these angles.
        public static float WrisksYaw = 285f;
        public static float WrisksPitch = 0f;
        public static float WrisksRoll = -15f;
        public static float WrisksOffsetX = 0f;
        public static float WrisksOffsetY = 0f;
        public static float WrisksDistance = 0f;
        public static float WrisksScale = 2.15f;

        public static float BucketYaw = -110f;
        public static float BucketPitch = -10f;
        public static float BucketRoll = -25f;
        public static float BucketOffsetX = 0f;
        public static float BucketOffsetY = 0f;
        public static float BucketDistance = 0f;
        public static float BucketScale = 1f;
    }

    /// Editor panel. Exists only while the F7 live editor is open.

    /// <summary>
    /// Visual settings for sauna-bucket liquids.
    /// Preview is used only by the editor:
    /// 0 = normal water, 1 = poison mead, 2 = frost mead, 3 = fire mead.
    /// When the editor is closed, the visual comes from the bucket's real ZDO state.
    /// </summary>
    internal static class SaunaBucketLiquidTuning
    {
        public static int Preview = 0;

        // Values captured from the user's final visual setup.
        public static float WaterR = 0.16f;
        public static float WaterG = 0.22f;
        public static float WaterB = 0.22f;
        public static float WaterA = 0.48f;

        // Mead in the bucket is intentionally shown as heavily diluted with water.
        public static float PoisonR = 0.44f;
        public static float PoisonG = 0.56f;
        public static float PoisonB = 0.20f;
        public static float PoisonA = 0.08f;

        public static float FrostR = 0.12f;
        public static float FrostG = 0.68f;
        public static float FrostB = 1.00f;
        public static float FrostA = 0.08f;

        public static float FireR = 0.68f;
        public static float FireG = 0.38f;
        public static float FireB = 0.10f;
        public static float FireA = 0.08f;

        // In the mug the mead is undiluted: same RGB with fixed opacity,
        // independent of the alpha used for the diluted layer in the bucket.
        public const float MugMeadAlpha = 0.70f;

        public static UnityEngine.Color WaterColor =>
            new UnityEngine.Color(WaterR, WaterG, WaterB, WaterA);

        public static UnityEngine.Color MeadColor(int type)
        {
            switch (type)
            {
                case 1:
                    return new UnityEngine.Color(PoisonR, PoisonG, PoisonB, PoisonA);
                case 2:
                    return new UnityEngine.Color(FrostR, FrostG, FrostB, FrostA);
                case 3:
                    return new UnityEngine.Color(FireR, FireG, FireB, FireA);
                default:
                    return UnityEngine.Color.clear;
            }
        }

        public static UnityEngine.Color MugMeadColor(int type)
        {
            UnityEngine.Color color = MeadColor(type);
            color.a = MugMeadAlpha;
            return color;
        }
    }

    /// <summary>
    /// Stores references to the liquid parts of one bucket instance.
    /// This lets the editor switch water/mead live
    /// and refresh buckets that are already placed in the world.
    /// </summary>
    internal class SaunaBucketLiquidVisual : MonoBehaviour
    {
        private static readonly HashSet<SaunaBucketLiquidVisual> Instances =
            new HashSet<SaunaBucketLiquidVisual>();

        [SerializeField] private MeshRenderer _waterSurface;
        [SerializeField] private MeshRenderer _infusionSurface;
        [SerializeField] private Renderer _mugRenderer;
        [SerializeField] private int _mugLiquidSlot = 1;

        private void Awake()
        {
            Instances.Add(this);
            ApplyPreview();
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
        }

        public void Configure(
            MeshRenderer waterSurface,
            MeshRenderer infusionSurface,
            Renderer mugRenderer,
            int mugLiquidSlot)
        {
            _waterSurface = waterSurface;
            _infusionSurface = infusionSurface;
            _mugRenderer = mugRenderer;
            _mugLiquidSlot = mugLiquidSlot;
            ApplyPreview();
        }

        /// <summary>
        /// Re-resolve the liquid renderers from the ACTUAL placed instance.
        /// Jotunn/Unity can clone a runtime-built Piece without preserving every
        /// private component reference exactly as it existed on the construction
        /// prefab. The gameplay ZDO can therefore be correct while the old cached
        /// renderers still point at the prefab (or are null). Names are unique inside
        /// our bucket model, so rebinding by hierarchy is deterministic.
        /// </summary>
        private void ResolveRuntimeReferences()
        {
            MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            Renderer mug = null;

            foreach (MeshRenderer mr in meshRenderers)
            {
                if (mr == null)
                {
                    continue;
                }

                string objectName = mr.gameObject.name;
                if (objectName == "Cylinder")
                {
                    _waterSurface = mr;
                }
                else if (objectName == "Cylinder_low")
                {
                    _infusionSurface = mr;
                }
                else if (objectName == "fi_vil_container_ale_mug")
                {
                    Material[] mats = mr.sharedMaterials;
                    if (mats != null && mats.Length > _mugLiquidSlot)
                    {
                        mug = mr;
                    }
                }
            }

            if (mug != null)
            {
                _mugRenderer = mug;
            }
        }

        public void ApplyPreview()
        {
            // IMPORTANT: resolve against the live Piece every time. This makes the
            // visual independent of serialized/runtime prefab references.
            ResolveRuntimeReferences();
            SaunaPlugin.ApplyBucketLiquidTuningToMaterials();

            if (_waterSurface != null)
            {
                Material water = SaunaPlugin.GetBucketWaterMaterial();
                if (water != null)
                {
                    _waterSurface.sharedMaterial = water;
                }
                _waterSurface.enabled = true;
            }

            SaunaBucket bucket = GetComponent<SaunaBucket>();

            // Preview overrides the real contents only while the Bucket editor
            // section itself is selected. Everywhere else the live ZDO state wins.
            int preview = SaunaEditor.BucketLiquidPreviewActive
                ? Mathf.Clamp(SaunaBucketLiquidTuning.Preview, 0, 3)
                : bucket != null ? bucket.GetInfusionType() : 0;

            if (_infusionSurface != null)
            {
                if (preview == 0)
                {
                    _infusionSurface.enabled = false;
                }
                else
                {
                    Material mead = SaunaPlugin.GetBucketMeadMaterial(preview);
                    if (mead != null)
                    {
                        _infusionSurface.sharedMaterial = mead;
                        _infusionSurface.enabled = true;
                    }
                }
            }

            if (_mugRenderer != null)
            {
                Material[] mats = _mugRenderer.sharedMaterials;
                if (mats != null &&
                    _mugLiquidSlot >= 0 &&
                    _mugLiquidSlot < mats.Length)
                {
                    Material liquid = preview == 0
                        ? SaunaPlugin.GetBucketEmptyLiquidMaterial()
                        : SaunaPlugin.GetBucketMugMeadMaterial(preview);

                    if (liquid != null)
                    {
                        Material[] local = (Material[])mats.Clone();
                        local[_mugLiquidSlot] = liquid;
                        _mugRenderer.sharedMaterials = local;
                    }
                }
            }

            if (preview != 0 && (_infusionSurface == null || _mugRenderer == null))
            {
                Jotunn.Logger.LogWarning(
                    $"sauna bucket visual: infusion={preview}, " +
                    $"Cylinder_low={_infusionSurface != null}, mug={_mugRenderer != null}");
            }
        }

        public static void RefreshAll()
        {
            SaunaPlugin.ApplyBucketLiquidTuningToMaterials();

            // Use a copy because refreshing an icon may temporarily create or destroy
            // cloned prefabs and therefore change Instances during iteration.
            SaunaBucketLiquidVisual[] copy = new SaunaBucketLiquidVisual[Instances.Count];
            Instances.CopyTo(copy);

            foreach (SaunaBucketLiquidVisual visual in copy)
            {
                if (visual != null)
                {
                    visual.ApplyPreview();
                }
            }
        }
    }


    /// <summary>
    /// Synergy between the sauna bucket and resistance meads.
    /// DurationMultiplier is applied to the normal duration of the vanilla effect.
    /// </summary>
    internal static class SaunaMeadTuning
    {
        public static float DurationMultiplier = 1.20f;
        public const float EffectRadius = 8f;
    }

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

    /// <summary>
    /// Gameplay state of the sauna bucket. Mead type is stored in ZDO,
    /// so it survives world saves and synchronizes between clients.
    /// </summary>
    internal class SaunaBucket : MonoBehaviour, Interactable, Hoverable
    {
        public const string ZdoInfusion = "sauna_infusion";

        private static readonly HashSet<SaunaBucket> Instances =
            new HashSet<SaunaBucket>();

        private ZNetView m_nview;
        private SaunaBucketLiquidVisual m_visual;
        private int m_lastVisualType = -1;
        private float m_poll;

        private void Awake()
        {
            Instances.Add(this);
            m_nview = GetComponent<ZNetView>();
            m_visual = GetComponent<SaunaBucketLiquidVisual>();
        }

        private void Start()
        {
            RefreshVisual(true);
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
        }

        private void Update()
        {
            m_poll -= Time.deltaTime;
            if (m_poll > 0f)
            {
                return;
            }

            m_poll = 0.25f;
            RefreshVisual(false);
        }

        public int GetInfusionType()
        {
            if (m_nview == null || !m_nview.IsValid() || m_nview.GetZDO() == null)
            {
                return 0;
            }

            return Mathf.Clamp(m_nview.GetZDO().GetInt(ZdoInfusion, 0), 0, 3);
        }

        private void SetInfusionType(int type)
        {
            type = Mathf.Clamp(type, 0, 3);

            if (m_nview == null || !m_nview.IsValid() || m_nview.GetZDO() == null)
            {
                return;
            }

            if (!m_nview.IsOwner())
            {
                m_nview.ClaimOwnership();
            }

            m_nview.GetZDO().Set(ZdoInfusion, type);
            m_lastVisualType = -1;
            RefreshVisual(true);
        }

        private void RefreshVisual(bool force)
        {
            int type = GetInfusionType();
            if (!force && type == m_lastVisualType)
            {
                return;
            }

            m_lastVisualType = type;
            if (m_visual == null)
            {
                m_visual = GetComponent<SaunaBucketLiquidVisual>();
            }

            if (m_visual != null)
            {
                m_visual.ApplyPreview();
            }
        }

        private static string LocalizeHover(string text)
        {
            return Localization.instance != null
                ? Localization.instance.Localize(text)
                : text;
        }

        public string GetHoverName()
        {
            return LocalizeHover("$piece_sauna_bucket");
        }

        public float GetHoverOffset()
        {
            return 0f;
        }

        public string GetHoverText()
        {
            int infusion = GetInfusionType();
            if (infusion != SaunaMeadSystem.None)
            {
                string bucketName = LocalizeHover("$piece_sauna_bucket");
                string contains = LocalizeHover("$msg_sauna_bucket_contains");
                return bucketName + "\n" + contains + ": " +
                    SaunaMeadSystem.LocalizedName(infusion);
            }

            // Hoverable.GetHoverText is rendered as-is. Unlike many vanilla Piece
            // paths, this custom proxy is not localized again by the HUD, so resolve
            // every token here (including the vanilla $KEY_Use token).
            return LocalizeHover(
                "$piece_sauna_bucket\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] $msg_sauna_bucket_use_mead");
        }

        // E is useful even without a special item-targeting gesture: if the player
        // carries only one supported resistance-mead TYPE, use one bottle directly.
        // If several different types are present, do not guess — the player chooses
        // one by using that mead from the hotbar while aiming at the bucket.
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user == null)
            {
                return false;
            }

            if (GetInfusionType() != SaunaMeadSystem.None)
            {
                user.Message(MessageHud.MessageType.Center, "$msg_sauna_bucket_full");
                return true;
            }

            bool multipleTypes;
            ItemDrop.ItemData onlyMead = SaunaMeadSystem.FindSingleAvailableMead(
                user, out multipleTypes);

            if (onlyMead != null)
            {
                return TryUseMead(user, onlyMead);
            }

            user.Message(
                MessageHud.MessageType.Center,
                multipleTypes ? "$msg_sauna_bucket_choose_mead" : "$msg_sauna_bucket_no_mead");
            return true;
        }

        public bool Interact(Humanoid user, bool hold)
        {
            return Interact(user, hold, false);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return TryUseMead(user, item);
        }

        public bool TryUseMead(Humanoid user, ItemDrop.ItemData item)
        {
            if (user == null || item == null)
            {
                return false;
            }

            int type = SaunaMeadSystem.GetType(item);
            if (type == SaunaMeadSystem.None)
            {
                if (SaunaMeadSystem.IsMeadLike(item))
                {
                    user.Message(MessageHud.MessageType.Center, "$msg_sauna_bucket_resistance_only");
                    return true;
                }

                return false;
            }

            if (GetInfusionType() != SaunaMeadSystem.None)
            {
                user.Message(MessageHud.MessageType.Center, "$msg_sauna_bucket_full");
                return true;
            }

            if (!user.GetInventory().RemoveItem(item, 1))
            {
                return false;
            }

            SetInfusionType(type);

            string fmt = Localization.instance != null
                ? Localization.instance.Localize("$msg_sauna_bucket_filled")
                : "Added {0} to the sauna bucket";
            user.Message(
                MessageHud.MessageType.Center,
                string.Format(fmt, SaunaMeadSystem.LocalizedName(type)));

            Jotunn.Logger.LogInfo(
                $"sauna bucket: infused type={type} by '{(user is Player p ? p.GetPlayerName() : user.name)}'");
            return true;
        }

        public static int ConsumeLinkedInfusion(SaunaStove stove)
        {
            if (stove == null || stove.GetWellSteamedSaunaTier() < 3)
            {
                return SaunaMeadSystem.None;
            }

            SaunaBucket best = null;
            float bestDistance = float.MaxValue;

            foreach (SaunaBucket bucket in Instances)
            {
                if (bucket == null)
                {
                    continue;
                }

                int type = bucket.GetInfusionType();
                if (type == SaunaMeadSystem.None)
                {
                    continue;
                }

                SaunaStove linked = SaunaStove.FindClosestForVisualLink(
                    bucket.transform.position,
                    SaunaVisualLink.MaxLinkDistance);

                if (linked != stove)
                {
                    continue;
                }

                float distance = UnityEngine.Vector3.Distance(
                    bucket.transform.position,
                    stove.transform.position);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = bucket;
                }
            }

            if (best == null)
            {
                return SaunaMeadSystem.None;
            }

            int infusion = best.GetInfusionType();
            best.SetInfusionType(SaunaMeadSystem.None);
            return infusion;
        }
    }

    /// <summary>
    /// Interaction proxy lives on the SAME GameObject as the bucket collider.
    /// Valheim's hover/interact raycast can resolve interfaces on the hit object itself,
    /// while the real SaunaBucket state stays safely on the Piece root with its ZNetView.
    /// </summary>
    internal class SaunaBucketInteractionProxy : MonoBehaviour, Interactable, Hoverable
    {
        private SaunaBucket Target => GetComponentInParent<SaunaBucket>();

        public string GetHoverName()
        {
            SaunaBucket target = Target;
            if (target != null)
            {
                return target.GetHoverName();
            }

            return Localization.instance != null
                ? Localization.instance.Localize("$piece_sauna_bucket")
                : "$piece_sauna_bucket";
        }

        public string GetHoverText()
        {
            SaunaBucket target = Target;
            if (target != null)
            {
                return target.GetHoverText();
            }

            return Localization.instance != null
                ? Localization.instance.Localize("$piece_sauna_bucket")
                : "$piece_sauna_bucket";
        }

        public float GetHoverOffset()
        {
            SaunaBucket target = Target;
            return target != null ? target.GetHoverOffset() : 0f;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            SaunaBucket target = Target;
            return target != null && target.Interact(user, hold, alt);
        }

        public bool Interact(Humanoid user, bool hold)
        {
            return Interact(user, hold, false);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            SaunaBucket target = Target;
            return target != null && target.UseItem(user, item);
        }
    }

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

    internal class SaunaEditorGui : MonoBehaviour
    {
        private void OnGUI()
        {
            GUI.Box(new Rect(10f, 10f, 660f, 132f), "");
            GUI.Label(new Rect(20f, 16f, 640f, 116f), SaunaEditor.StatusLine());
        }
    }

    /// Live editor for the test build.
    /// PgUp/PgDn — section, ←/→ — parameter, ↑/↓ — value.
    internal static class SaunaEditor
    {
        public static bool Active;

        private static int _sectionIndex = 4; // Open directly on the Steam section for tester convenience.
        private static int _entryIndex;
        private static GameObject _gui;

        private class Entry
        {
            public string Name;
            public Func<float> Get;
            public Action<float> Set;
            public float Step;
            public bool Integer;
            public bool AllowNegative;
            public bool ActionOnly;
            public Action Trigger;
            public Action Changed;
        }

        private class Section
        {
            public string Name;
            public Entry[] Entries;
        }

        private static readonly Action RefreshStoveModelAndIcon = () =>
        {
            StoveVisual.Invalidate();
            SaunaStove.RebuildAll();
            SaunaPlugin.RefreshPieceIcon();
        };

        private static readonly Action RefreshStoveIcon = () => SaunaPlugin.RefreshPieceIcon();
        private static readonly Action RefreshWrisksIcon = () => SaunaPlugin.RefreshWrisksIcon();
        private static readonly Action RefreshBucketIcon = () => SaunaPlugin.RefreshBucketIcon();
        private static readonly Action RefreshBucketLiquidsAndIcon = () =>
        {
            SaunaPlugin.RefreshBucketLiquidVisuals();
            SaunaPlugin.RefreshBucketIcon();
        };

        private static readonly Section[] Sections =
        {
            new Section
            {
                Name = "Sauna Stove",
                Entries = new[]
                {
                    new Entry { Name = "dome.StoneIndex",    Get = () => StoveVisual.StoneIndex,    Set = v => StoveVisual.StoneIndex = (int)v,    Step = 1f,    Integer = true, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.StoneSize",     Get = () => StoveVisual.StoneSize,     Set = v => StoveVisual.StoneSize = v,          Step = 0.02f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.BaseRadius",    Get = () => StoveVisual.BaseRadius,    Set = v => StoveVisual.BaseRadius = v,         Step = 0.05f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.TopRadius",     Get = () => StoveVisual.TopRadius,     Set = v => StoveVisual.TopRadius = v,          Step = 0.02f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.Height",        Get = () => StoveVisual.Height,        Set = v => StoveVisual.Height = v,             Step = 0.05f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.Layers",        Get = () => StoveVisual.Layers,        Set = v => StoveVisual.Layers = (int)v,        Step = 1f,    Integer = true, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.CountBase",     Get = () => StoveVisual.CountBase,     Set = v => StoveVisual.CountBase = (int)v,     Step = 1f,    Integer = true, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.OpeningWidth",  Get = () => StoveVisual.OpeningWidth,  Set = v => StoveVisual.OpeningWidth = v,       Step = 5f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.OpeningLayers", Get = () => StoveVisual.OpeningLayers, Set = v => StoveVisual.OpeningLayers = (int)v, Step = 1f,    Integer = true, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.SizeJitter",    Get = () => StoveVisual.SizeJitter,    Set = v => StoveVisual.SizeJitter = v,         Step = 0.05f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.Tilt",          Get = () => StoveVisual.Tilt,          Set = v => StoveVisual.Tilt = v,               Step = 5f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "dome.Seed",          Get = () => StoveVisual.Seed,          Set = v => StoveVisual.Seed = (int)v,          Step = 1f,    Integer = true, Changed = RefreshStoveModelAndIcon },

                    new Entry { Name = "floor.StoneSize",    Get = () => StoveVisual.FloorStoneSize, Set = v => StoveVisual.FloorStoneSize = v,      Step = 0.02f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "floor.Count",        Get = () => StoveVisual.FloorCount,     Set = v => StoveVisual.FloorCount = (int)v,    Step = 1f,    Integer = true, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "floor.Radius",       Get = () => StoveVisual.FloorRadius,    Set = v => StoveVisual.FloorRadius = v,         Step = 0.02f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "floor.OffsetY",      Get = () => StoveVisual.FloorOffsetY,   Set = v => StoveVisual.FloorOffsetY = v,        Step = 0.02f, AllowNegative = true, Changed = RefreshStoveModelAndIcon },

                    new Entry { Name = "lava.Index",         Get = () => StoveVisual.LavaIndex,      Set = v => StoveVisual.LavaIndex = (int)v,      Step = 1f,    Integer = true, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "lava.Size",          Get = () => StoveVisual.LavaSize,       Set = v => StoveVisual.LavaSize = v,            Step = 0.02f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "lava.Count",         Get = () => StoveVisual.LavaCount,      Set = v => StoveVisual.LavaCount = (int)v,      Step = 1f,    Integer = true, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "lava.Radius",        Get = () => StoveVisual.LavaRadius,     Set = v => StoveVisual.LavaRadius = v,          Step = 0.02f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "lava.OffsetY",       Get = () => StoveVisual.LavaOffsetY,    Set = v => StoveVisual.LavaOffsetY = v,         Step = 0.02f, AllowNegative = true, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "coal.Size",          Get = () => StoveVisual.CoalSize,       Set = v => StoveVisual.CoalSize = v,            Step = 0.02f, Changed = RefreshStoveModelAndIcon },

                    new Entry { Name = "fire.OffsetY",       Get = () => StoveVisual.FireOffsetY,    Set = v => StoveVisual.FireOffsetY = v,         Step = 0.05f, AllowNegative = true, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "fire.Scale",         Get = () => StoveVisual.FireScale,      Set = v => StoveVisual.FireScale = v,           Step = 0.05f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "fire.FlameSize",     Get = () => StoveVisual.FlameSize,      Set = v => StoveVisual.FlameSize = v,           Step = 0.05f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "fire.FlameOffsetY",  Get = () => StoveVisual.FlameOffsetY,   Set = v => StoveVisual.FlameOffsetY = v,        Step = 0.02f, AllowNegative = true, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "fire.WarmthRadius",  Get = () => StoveVisual.WarmthRadius,   Set = v => StoveVisual.WarmthRadius = v,        Step = 0.5f, Changed = RefreshStoveModelAndIcon },
                    new Entry { Name = "fire.FlameSet",      Get = () => StoveVisual.FlameSet,       Set = v => StoveVisual.FlameSet = (int)v,       Step = 1f,    Integer = true, Changed = RefreshStoveModelAndIcon },

                    new Entry { Name = "icon.Yaw",           Get = () => StoveVisual.IconYaw,        Set = v => StoveVisual.IconYaw = v,             Step = 5f, AllowNegative = true, Changed = RefreshStoveIcon },
                    new Entry { Name = "icon.Pitch",         Get = () => StoveVisual.IconPitch,      Set = v => StoveVisual.IconPitch = v,           Step = 5f, AllowNegative = true, Changed = RefreshStoveIcon },
                    new Entry { Name = "icon.Flame",         Get = () => StoveVisual.IconFlame,      Set = v => StoveVisual.IconFlame = (int)v,      Step = 1f, Integer = true, Changed = RefreshStoveIcon }
                }
            },
            new Section
            {
                Name = "Sauna Whisks",
                Entries = new[]
                {
                    new Entry { Name = "wrisks.icon.Yaw",      Get = () => SaunaPieceIconTuning.WrisksYaw,      Set = v => SaunaPieceIconTuning.WrisksYaw = v,      Step = 5f, AllowNegative = true, Changed = RefreshWrisksIcon },
                    new Entry { Name = "wrisks.icon.Pitch",    Get = () => SaunaPieceIconTuning.WrisksPitch,    Set = v => SaunaPieceIconTuning.WrisksPitch = v,    Step = 5f, AllowNegative = true, Changed = RefreshWrisksIcon },
                    new Entry { Name = "wrisks.icon.Roll",     Get = () => SaunaPieceIconTuning.WrisksRoll,     Set = v => SaunaPieceIconTuning.WrisksRoll = v,     Step = 5f, AllowNegative = true, Changed = RefreshWrisksIcon },
                    new Entry { Name = "wrisks.icon.OffsetX",  Get = () => SaunaPieceIconTuning.WrisksOffsetX,  Set = v => SaunaPieceIconTuning.WrisksOffsetX = v,  Step = 0.05f, AllowNegative = true, Changed = RefreshWrisksIcon },
                    new Entry { Name = "wrisks.icon.OffsetY",  Get = () => SaunaPieceIconTuning.WrisksOffsetY,  Set = v => SaunaPieceIconTuning.WrisksOffsetY = v,  Step = 0.05f, AllowNegative = true, Changed = RefreshWrisksIcon },
                    new Entry { Name = "wrisks.icon.Distance", Get = () => SaunaPieceIconTuning.WrisksDistance, Set = v => SaunaPieceIconTuning.WrisksDistance = v, Step = 0.10f, AllowNegative = true, Changed = RefreshWrisksIcon },
                    new Entry { Name = "wrisks.icon.Scale",    Get = () => SaunaPieceIconTuning.WrisksScale,    Set = v => SaunaPieceIconTuning.WrisksScale = Mathf.Max(0.05f, v), Step = 0.05f, Changed = RefreshWrisksIcon }
                }
            },
            new Section
            {
                Name = "Bucket",
                Entries = new[]
                {
                    new Entry { Name = "bucket.icon.Yaw",      Get = () => SaunaPieceIconTuning.BucketYaw,      Set = v => SaunaPieceIconTuning.BucketYaw = v,      Step = 5f, AllowNegative = true, Changed = RefreshBucketIcon },
                    new Entry { Name = "bucket.icon.Pitch",    Get = () => SaunaPieceIconTuning.BucketPitch,    Set = v => SaunaPieceIconTuning.BucketPitch = v,    Step = 5f, AllowNegative = true, Changed = RefreshBucketIcon },
                    new Entry { Name = "bucket.icon.Roll",     Get = () => SaunaPieceIconTuning.BucketRoll,     Set = v => SaunaPieceIconTuning.BucketRoll = v,     Step = 5f, AllowNegative = true, Changed = RefreshBucketIcon },
                    new Entry { Name = "bucket.icon.OffsetX",  Get = () => SaunaPieceIconTuning.BucketOffsetX,  Set = v => SaunaPieceIconTuning.BucketOffsetX = v,  Step = 0.05f, AllowNegative = true, Changed = RefreshBucketIcon },
                    new Entry { Name = "bucket.icon.OffsetY",  Get = () => SaunaPieceIconTuning.BucketOffsetY,  Set = v => SaunaPieceIconTuning.BucketOffsetY = v,  Step = 0.05f, AllowNegative = true, Changed = RefreshBucketIcon },
                    new Entry { Name = "bucket.icon.Distance", Get = () => SaunaPieceIconTuning.BucketDistance, Set = v => SaunaPieceIconTuning.BucketDistance = v, Step = 0.10f, AllowNegative = true, Changed = RefreshBucketIcon },
                    new Entry { Name = "bucket.icon.Scale",    Get = () => SaunaPieceIconTuning.BucketScale,    Set = v => SaunaPieceIconTuning.BucketScale = Mathf.Max(0.05f, v), Step = 0.05f, Changed = RefreshBucketIcon },

                    // 0 = normal bucket with water and an empty mug;
                    // 1/2/3 = visual preview of the corresponding mead.
                    new Entry { Name = "bucket.liquid.Preview", Get = () => SaunaBucketLiquidTuning.Preview, Set = v => SaunaBucketLiquidTuning.Preview = Mathf.Clamp((int)v, 0, 3), Step = 1f, Integer = true, Changed = RefreshBucketLiquidsAndIcon },

                    new Entry { Name = "bucket.water.R", Get = () => SaunaBucketLiquidTuning.WaterR, Set = v => SaunaBucketLiquidTuning.WaterR = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.water.G", Get = () => SaunaBucketLiquidTuning.WaterG, Set = v => SaunaBucketLiquidTuning.WaterG = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.water.B", Get = () => SaunaBucketLiquidTuning.WaterB, Set = v => SaunaBucketLiquidTuning.WaterB = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.water.A", Get = () => SaunaBucketLiquidTuning.WaterA, Set = v => SaunaBucketLiquidTuning.WaterA = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },

                    new Entry { Name = "bucket.poison.R", Get = () => SaunaBucketLiquidTuning.PoisonR, Set = v => SaunaBucketLiquidTuning.PoisonR = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.poison.G", Get = () => SaunaBucketLiquidTuning.PoisonG, Set = v => SaunaBucketLiquidTuning.PoisonG = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.poison.B", Get = () => SaunaBucketLiquidTuning.PoisonB, Set = v => SaunaBucketLiquidTuning.PoisonB = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.poison.A", Get = () => SaunaBucketLiquidTuning.PoisonA, Set = v => SaunaBucketLiquidTuning.PoisonA = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },

                    new Entry { Name = "bucket.frost.R", Get = () => SaunaBucketLiquidTuning.FrostR, Set = v => SaunaBucketLiquidTuning.FrostR = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.frost.G", Get = () => SaunaBucketLiquidTuning.FrostG, Set = v => SaunaBucketLiquidTuning.FrostG = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.frost.B", Get = () => SaunaBucketLiquidTuning.FrostB, Set = v => SaunaBucketLiquidTuning.FrostB = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.frost.A", Get = () => SaunaBucketLiquidTuning.FrostA, Set = v => SaunaBucketLiquidTuning.FrostA = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },

                    new Entry { Name = "bucket.fire.R", Get = () => SaunaBucketLiquidTuning.FireR, Set = v => SaunaBucketLiquidTuning.FireR = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.fire.G", Get = () => SaunaBucketLiquidTuning.FireG, Set = v => SaunaBucketLiquidTuning.FireG = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.fire.B", Get = () => SaunaBucketLiquidTuning.FireB, Set = v => SaunaBucketLiquidTuning.FireB = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon },
                    new Entry { Name = "bucket.fire.A", Get = () => SaunaBucketLiquidTuning.FireA, Set = v => SaunaBucketLiquidTuning.FireA = Mathf.Clamp01(v), Step = 0.02f, Changed = RefreshBucketLiquidsAndIcon }
                }
            },
            new Section
            {
                Name = "Effect Icons",
                Entries = new[]
                {
                    new Entry { Name = "badge.X",     Get = () => SaunaWellSteamedTierBadgePatch.BadgeOffsetX, Set = v => SaunaWellSteamedTierBadgePatch.BadgeOffsetX = v, Step = 1f, AllowNegative = true },
                    new Entry { Name = "badge.Y",     Get = () => SaunaWellSteamedTierBadgePatch.BadgeOffsetY, Set = v => SaunaWellSteamedTierBadgePatch.BadgeOffsetY = v, Step = 1f, AllowNegative = true },
                    new Entry { Name = "badge.Scale", Get = () => SaunaWellSteamedTierBadgePatch.BadgeScale,   Set = v => SaunaWellSteamedTierBadgePatch.BadgeScale = Mathf.Max(0.1f, v), Step = 0.05f }
                }
            },
            new Section
            {
                Name = "Steam",
                Entries = new[]
                {
                    new Entry { Name = "steam.Lifetime",      Get = () => SteamTuning.Lifetime,      Set = v => SteamTuning.Lifetime = Mathf.Max(SteamTuning.MinLifetime, v), Step = 5f },
                    new Entry { Name = "steam.FadeTime",      Get = () => SteamTuning.FadeTime,      Set = v => SteamTuning.FadeTime = v,           Step = 0.5f },
                    new Entry { Name = "steam.Spread",        Get = () => SteamTuning.Spread,        Set = v => SteamTuning.Spread = v,             Step = 0.05f },
                    new Entry { Name = "steam.Rise",          Get = () => SteamTuning.Rise,          Set = v => SteamTuning.Rise = v,               Step = 0.1f },
                    new Entry { Name = "steam.Force",         Get = () => SteamTuning.Force,         Set = v => SteamTuning.Force = v,              Step = 0.25f },
                    new Entry { Name = "steam.Push",          Get = () => SteamTuning.Push,          Set = v => SteamTuning.Push = v,               Step = 0.25f },
                    new Entry { Name = "steam.CloudSize",     Get = () => SteamTuning.CloudSize,     Set = v => SteamTuning.CloudSize = v,          Step = 0.1f },
                    new Entry { Name = "steam.CloudsGenerated.WithoutBucket", Get = () => SteamTuning.CloudsPerPour,           Set = v => SteamTuning.CloudsPerPour = Mathf.Max(1, (int)v),           Step = 1f, Integer = true },
                    new Entry { Name = "steam.CloudsGenerated.WithBucket",    Get = () => SteamTuning.CloudsPerPourWithBucket, Set = v => SteamTuning.CloudsPerPourWithBucket = Mathf.Max(1, (int)v), Step = 1f, Integer = true },
                    new Entry { Name = "steam.MaxClouds",     Get = () => SteamTuning.MaxClouds,     Set = v => SteamTuning.MaxClouds = (int)v,     Step = 10f, Integer = true },
                    new Entry { Name = "steam.Grace",         Get = () => SteamTuning.Grace,         Set = v => SteamTuning.Grace = v,              Step = 0.5f }
                }
            },
            new Section
            {
                Name = "Mead Test",
                Entries = new[]
                {
                    new Entry { Name = "mead.DurationMultiplier", Get = () => SaunaMeadTuning.DurationMultiplier, Set = v => SaunaMeadTuning.DurationMultiplier = Mathf.Max(0.1f, v), Step = 0.05f },
                    new Entry { Name = "TEST Spawn Poison Mead x10", Get = () => 0f, Step = 1f, ActionOnly = true, Trigger = () => SaunaMeadSystem.SpawnTestMead(SaunaMeadSystem.Poison, 10) },
                    new Entry { Name = "TEST Spawn Frost Mead x10",  Get = () => 0f, Step = 1f, ActionOnly = true, Trigger = () => SaunaMeadSystem.SpawnTestMead(SaunaMeadSystem.Frost, 10) },
                    new Entry { Name = "TEST Spawn Fire Wine x10",   Get = () => 0f, Step = 1f, ActionOnly = true, Trigger = () => SaunaMeadSystem.SpawnTestMead(SaunaMeadSystem.Fire, 10) }
                }
            }
        };

        // The editor is a development tool. Public builds keep it disabled by default;
        // it can be enabled explicitly in BepInEx/config/nekitker.saunamod.cfg.
        public static void Update()
        {
            if (!SaunaPlugin.EditorEnabled)
            {
                if (Active)
                {
                    SetActive(false);
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                SetActive(!Active);
            }

            if (!Active)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                ChangeSection(1);
            }
            else if (Input.GetKeyDown(KeyCode.PageUp))
            {
                ChangeSection(-1);
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                ChangeEntry(1);
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                ChangeEntry(-1);
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                Apply(1);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                Apply(-1);
            }
            else if (Input.GetKeyDown(KeyCode.F8))
            {
                Dump();
            }
            else if (Input.GetKeyDown(KeyCode.F9))
            {
                DumpCurrentIcon();
            }
            else if (Input.GetKeyDown(KeyCode.F10))
            {
                if (Player.m_localPlayer != null)
                {
                    SaunaStove.ExtinguishNearest(Player.m_localPlayer.transform.position);
                }
            }
        }

        private static Section CurrentSection()
        {
            return Sections[Mathf.Clamp(_sectionIndex, 0, Sections.Length - 1)];
        }

        // Preview colors should override the real bucket contents only while the
        // Bucket section itself is selected. This lets the Steam editor remain open
        // during gameplay tests without forcing every placed bucket to Preview=0.
        public static bool BucketLiquidPreviewActive =>
            Active && CurrentSection().Name == "Bucket";

        private static Entry CurrentEntry()
        {
            Section section = CurrentSection();
            _entryIndex = Mathf.Clamp(_entryIndex, 0, section.Entries.Length - 1);
            return section.Entries[_entryIndex];
        }

        private static void ChangeSection(int dir)
        {
            _sectionIndex = (_sectionIndex + dir + Sections.Length) % Sections.Length;
            _entryIndex = 0;

            // Entering Bucket shows the selected preview; leaving it immediately
            // restores the real networked infusion state.
            SaunaPlugin.RefreshBucketLiquidVisuals();
        }

        private static void ChangeEntry(int dir)
        {
            Entry[] entries = CurrentSection().Entries;
            _entryIndex = (_entryIndex + dir + entries.Length) % entries.Length;
        }

        private static void SetActive(bool value)
        {
            Active = value;

            if (Active)
            {
                if (_gui == null)
                {
                    _gui = new GameObject("SaunaEditorGui");
                    UnityEngine.Object.DontDestroyOnLoad(_gui);
                    _gui.AddComponent<SaunaEditorGui>();
                }
            }
            else if (_gui != null)
            {
                UnityEngine.Object.Destroy(_gui);
                _gui = null;
            }

            SaunaPlugin.RefreshBucketLiquidVisuals();
            Jotunn.Logger.LogInfo($"sauna editor: {(Active ? "ON" : "OFF")}");
        }

        private static void Apply(int dir)
        {
            Entry e = CurrentEntry();

            if (e.ActionOnly)
            {
                e.Trigger?.Invoke();
                return;
            }

            float mul = 1f;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                mul *= 10f;
            }
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                mul *= 0.1f;
            }

            float value = e.Get() + e.Step * dir * mul;

            if (e.Integer)
            {
                value = Mathf.Round(value);
            }

            if (!e.AllowNegative)
            {
                value = Mathf.Max(0f, value);
            }

            e.Set(value);
            e.Changed?.Invoke();
        }

        private static string FormatValue(Entry e)
        {
            if (e.ActionOnly)
            {
                return "press Up/Down";
            }

            return e.Integer ? ((int)e.Get()).ToString() : e.Get().ToString("0.###");
        }

        public static string StatusLine()
        {
            Section section = CurrentSection();
            Entry e = CurrentEntry();

            return $"Sauna Editor  [{_sectionIndex + 1}/{Sections.Length}] {section.Name}\n" +
                   $"[{_entryIndex + 1}/{section.Entries.Length}] {e.Name} = {FormatValue(e)}\n" +
                   "PgUp/PgDn section   ← → parameter   ↑ ↓ value   Ctrl ×0.1   Shift ×10\n" +
                   "F8 dump settings   F9 save current icon PNG   F10 extinguish stove   F7 close";
        }

        private static void DumpCurrentIcon()
        {
            switch (_sectionIndex)
            {
                case 0:
                    SaunaPlugin.RefreshPieceIcon();
                    SaunaPlugin.DumpPieceIcon();
                    break;
                case 1:
                    SaunaPlugin.RefreshWrisksIcon();
                    SaunaPlugin.DumpWrisksIcon();
                    break;
                case 2:
                    SaunaPlugin.RefreshBucketIcon();
                    SaunaPlugin.DumpBucketIcon();
                    break;
                default:
                    Jotunn.Logger.LogInfo("sauna editor: current section has no hammer icon to save as PNG");
                    break;
            }
        }

        private static void Dump()
        {
            Jotunn.Logger.LogInfo("===== SAUNA EDITOR DUMP BEGIN =====");
            Jotunn.Logger.LogInfo($"stone prefab = {StoveVisual.CurrentStoneName()}");
            Jotunn.Logger.LogInfo($"lava prefab = {StoveVisual.CurrentLavaName()}");

            foreach (Section section in Sections)
            {
                Jotunn.Logger.LogInfo($"--- {section.Name} ---");
                foreach (Entry e in section.Entries)
                {
                    if (e.ActionOnly)
                    {
                        continue;
                    }

                    string value = e.Integer ? ((int)e.Get()).ToString() : e.Get().ToString("0.###") + "f";
                    Jotunn.Logger.LogInfo($"{e.Name} = {value};");
                }
            }

            Jotunn.Logger.LogInfo("===== SAUNA EDITOR DUMP END =====");
        }
    }

    internal class SaunaVfxLife : MonoBehaviour
    {
        public float Remaining = 60f;

        private bool m_stopped;

        private void Update()
        {
            Remaining -= Time.deltaTime;

            if (Remaining > 0f || m_stopped)
            {
                return;
            }

            m_stopped = true;

            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.EmissionModule emission = ps.emission;
                emission.enabled = false;
            }

            Destroy(gameObject, 5f);
        }
    }

    internal class SaunaPlayer : MonoBehaviour
    {
        public const string RpcName = "SaunaMod_Steamed";
        public const string MeadRpcName = "SaunaMod_Mead";

        private ZNetView m_nview;

        // Use Start instead of Awake so behavior does not depend on component order on the player prefab.
        private void Start()
        {
            m_nview = GetComponent<ZNetView>();

            if (m_nview != null && m_nview.IsValid())
            {
                m_nview.Register(RpcName, new Action<long>(RPC_Steamed));
                m_nview.Register<int, UnityEngine.Vector3>(
                    MeadRpcName,
                    new Action<long, int, UnityEngine.Vector3>(RPC_Mead));
            }
        }

        public void Broadcast()
        {
            if (m_nview != null && m_nview.IsValid())
            {
                m_nview.InvokeRPC(ZNetView.Everybody, RpcName);
            }
            else
            {
                RPC_Steamed(0L);
            }
        }

        private void RPC_Steamed(long sender)
        {
            SaunaPlugin.SpawnSteamVfxOn(gameObject);
        }

        public void GrantMeadToOwner(int type, UnityEngine.Vector3 stovePosition)
        {
            Player player = GetComponent<Player>();
            if (player == null)
            {
                return;
            }

            if (player == Player.m_localPlayer)
            {
                RPC_Mead(0L, type, stovePosition);
                return;
            }

            if (m_nview != null && m_nview.IsValid() && m_nview.GetZDO() != null)
            {
                long owner = m_nview.GetZDO().GetOwner();
                if (owner != 0L)
                {
                    m_nview.InvokeRPC(owner, MeadRpcName, type, stovePosition);
                }
            }
        }

        private void RPC_Mead(long sender, int type, UnityEngine.Vector3 stovePosition)
        {
            Player player = GetComponent<Player>();
            if (player == null || player != Player.m_localPlayer)
            {
                return;
            }

            if (UnityEngine.Vector3.Distance(player.transform.position, stovePosition) >
                SaunaMeadTuning.EffectRadius)
            {
                return;
            }

            SEMan seman = player.GetSEMan();
            if (seman == null || !seman.HaveStatusEffect(SEMan.s_statusEffectShelter))
            {
                return;
            }

            SaunaStove closest = SaunaStove.FindClosestForVisualLink(
                player.transform.position,
                SaunaMeadTuning.EffectRadius);
            if (closest == null ||
                UnityEngine.Vector3.Distance(closest.transform.position, stovePosition) > 0.75f)
            {
                return;
            }

            SaunaMeadSystem.ApplyToPlayer(player, type);
        }
    }

    internal class SaunaStove : MonoBehaviour
    {
        public const string RpcName = "SaunaMod_Pour";
        public const string ZdoLastPour = "sauna_lastpour";

        public static GameObject SteamPrefab;
        public static AudioClip PourClip;

        public const float PourCooldown = 12f;
        public const float BurstDuration = 3f;
        public const float BurstRadius = 0.7f;
        public const float BurstHeight = 0.5f;
        public const float PourVolume = 1.0f;

        private static readonly List<SaunaStove> s_all = new List<SaunaStove>();

        private Fireplace m_fireplace;
        private ZNetView m_nview;
        private AudioSource m_audio;
        private float m_localLastPour = -999f;

        private Transform m_hotStones;
        private Transform m_coldStones;
        private bool m_wasHot;
        private float m_heatTimer;
        private float m_burstLeft;
        private float m_spawnAccum;
        private int m_burstCloudCount = 20;

        private readonly List<Transform> m_fireRoots = new List<Transform>();
        private readonly List<UnityEngine.Vector3> m_firePos = new List<UnityEngine.Vector3>();
        private readonly List<UnityEngine.Vector3> m_fireScale = new List<UnityEngine.Vector3>();
        private readonly List<ParticleSystem> m_fireParticles = new List<ParticleSystem>();
        private readonly List<UnityEngine.Vector3> m_fireParticlePos = new List<UnityEngine.Vector3>();
        private readonly List<UnityEngine.Vector3> m_fireParticleScale = new List<UnityEngine.Vector3>();
        private readonly List<float> m_fireParticleStartSize = new List<float>();
        private readonly List<SphereCollider> m_warmthColliders = new List<SphereCollider>();
        private readonly List<float> m_warmthRadii = new List<float>();

        public static void RebuildAll()
        {
            foreach (SaunaStove stove in s_all)
            {
                if (stove != null)
                {
                    stove.RefreshVisual();
                }
            }
        }

        private void Awake()
        {
            s_all.Add(this);

            m_fireplace = GetComponent<Fireplace>();
            m_nview = GetComponent<ZNetView>();

            if (m_fireplace != null && m_fireplace.m_smokeSpawner != null)
            {
                m_fireplace.m_smokeSpawner.enabled = false;
            }

            CacheFireRoots();
            SetupAudio();
            RefreshVisual();
        }

        // Register in Start rather than Awake so component order does not matter
        // and ZNetView is guaranteed to have its ZDO.
        private void Start()
        {
            if (m_nview != null && m_nview.IsValid())
            {
                m_nview.Register<int>(RpcName, new Action<long, int>(RPC_Pour));
            }
        }

        private void OnDestroy()
        {
            s_all.Remove(this);
        }

        /// Stores the original fire positions so repeated edits do not accumulate offsets.
        private void CacheFireRoots()
        {
            if (m_fireplace == null)
            {
                return;
            }

            GameObject[] objects =
            {
                m_fireplace.m_enabledObject,
                m_fireplace.m_enabledObjectLow,
                m_fireplace.m_enabledObjectHigh
            };

            foreach (GameObject go in objects)
            {
                if (go == null)
                {
                    continue;
                }

                m_fireRoots.Add(go.transform);
                m_firePos.Add(go.transform.localPosition);
                m_fireScale.Add(go.transform.localScale);

                foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps == null || m_fireParticles.Contains(ps))
                    {
                        continue;
                    }

                    ParticleSystem.MainModule main = ps.main;
                    m_fireParticles.Add(ps);
                    m_fireParticlePos.Add(ps.transform.localPosition);
                    m_fireParticleScale.Add(ps.transform.localScale);
                    m_fireParticleStartSize.Add(main.startSizeMultiplier);
                }

                // The heat zone is an EffectArea. Do not touch the ignition Aoe,
                // otherwise the player could catch fire while standing away from the stove.
                foreach (EffectArea area in go.GetComponentsInChildren<EffectArea>(true))
                {
                    SphereCollider sphere = area.GetComponent<SphereCollider>();

                    if (sphere == null || m_warmthColliders.Contains(sphere))
                    {
                        continue;
                    }

                    m_warmthColliders.Add(sphere);
                    m_warmthRadii.Add(sphere.radius);
                }
            }

            if (m_warmthColliders.Count > 0)
            {
                Jotunn.Logger.LogInfo($"warmth areas: {m_warmthColliders.Count}, " +
                    $"base radius={m_warmthRadii[0]:0.00}");
            }
        }

        public void RefreshVisual()
        {
            StoveVisual.Build(transform);
            ApplyFire();

            // Rebuilt children are new objects, so resolve them again and immediately apply the current state.
            m_hotStones = transform.Find(StoveVisual.LavaName);
            m_coldStones = transform.Find(StoveVisual.CoalName);
            ApplyHeat(IsHot(), true);
        }

        private bool IsHot()
        {
            // IsBurning reads ZDO without a null check. Placement ghosts have no ZDO,
            // so calling it would throw NullReferenceException. Treat placement ghosts as cold.
            return m_fireplace != null
                && m_nview != null
                && m_nview.IsValid()
                && m_fireplace.IsBurning();
        }

        /// Burning stove: show hot stones. Extinguished stove: show coals.
        /// If the coal visual is unavailable, keep the hot stones visible as before.
        private void ApplyHeat(bool hot, bool force)
        {
            if (!force && hot == m_wasHot)
            {
                return;
            }

            m_wasHot = hot;

            if (m_coldStones == null)
            {
                if (m_hotStones != null)
                {
                    m_hotStones.gameObject.SetActive(true);
                }
                return;
            }

            if (m_hotStones != null)
            {
                m_hotStones.gameObject.SetActive(hot);
            }

            m_coldStones.gameObject.SetActive(!hot);
        }

        /// Nearest actually placed sauna stove used only for visual linking.
        /// The stove placement ghost is excluded because it has no valid ZDO.
        public static SaunaStove FindClosestForVisualLink(UnityEngine.Vector3 position, float maxDistance)
        {
            SaunaStove nearest = null;
            float best = maxDistance * maxDistance;

            foreach (SaunaStove stove in s_all)
            {
                if (stove == null || stove.m_nview == null || !stove.m_nview.IsValid())
                {
                    continue;
                }

                float sqr = (stove.transform.position - position).sqrMagnitude;
                if (sqr < best)
                {
                    best = sqr;
                    nearest = stove;
                }
            }

            return nearest;
        }

        /// Anchor point for the golden connection line. This is visual only;
        /// the stove does not need a CraftingStation or StationExtension for it.
        public UnityEngine.Vector3 VisualLinkPoint()
        {
            return transform.position + UnityEngine.Vector3.up * 0.45f;
        }

        /// Sauna tier around THIS stove. Uses the same ownership/link rule as the golden line:
        /// the accessory must be within MaxLinkDistance and this stove must be its nearest stove.
        /// The bucket alone does not raise the tier; progression is stove -> whisks -> bucket.
        public int GetWellSteamedSaunaTier()
        {
            bool hasWhisks = false;
            bool hasBucket = false;

            foreach (Piece piece in Piece.s_allPieces)
            {
                if (piece == null)
                {
                    continue;
                }

                bool isWhisks = piece.m_name == "$piece_sauna_wrisks";
                bool isBucket = piece.m_name == "$piece_sauna_bucket";
                if (!isWhisks && !isBucket)
                {
                    continue;
                }

                // Placement ghosts also have Piece components, so count only actually placed objects.
                ZNetView nview = piece.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid())
                {
                    continue;
                }

                SaunaStove linkedStove = FindClosestForVisualLink(
                    piece.transform.position,
                    SaunaVisualLink.MaxLinkDistance);

                if (linkedStove != this)
                {
                    continue;
                }

                if (isWhisks)
                {
                    hasWhisks = true;
                }
                else if (isBucket)
                {
                    hasBucket = true;
                }

                if (hasWhisks && hasBucket)
                {
                    return 3;
                }
            }

            return hasWhisks ? 2 : 1;
        }

        /// Use the nearest stove to the player when granting Well Steamed.
        /// Steam physically originates at the stove, so 8 m leaves room for cloud movement,
        /// while accessories are still counted only within their own 5 m link radius.
        public static int GetWellSteamedSaunaTierNear(UnityEngine.Vector3 position)
        {
            const float sourceRange = 8f;
            SaunaStove stove = FindClosestForVisualLink(position, sourceRange);
            return stove != null ? stove.GetWellSteamedSaunaTier() : 1;
        }

        /// Returns the comfort bonus from sauna accessories around the nearest active sauna stove.
        /// Whisks and bucket each contribute +1, but only while the player is near a burning sauna stove.
        /// Accessories must also be genuinely placed and linked to that stove by the normal 5 m sauna link rule.
        public static int GetSaunaComfortBonusNear(UnityEngine.Vector3 position)
        {
            const float sourceRange = 8f;
            SaunaStove activeStove = null;
            float best = sourceRange * sourceRange;

            foreach (SaunaStove stove in s_all)
            {
                if (stove == null || !stove.IsHot())
                {
                    continue;
                }

                float sqr = (stove.transform.position - position).sqrMagnitude;
                if (sqr < best)
                {
                    best = sqr;
                    activeStove = stove;
                }
            }

            if (activeStove == null)
            {
                return 0;
            }

            bool hasWhisks = false;
            bool hasBucket = false;

            foreach (Piece piece in Piece.s_allPieces)
            {
                if (piece == null)
                {
                    continue;
                }

                bool isWhisks = piece.m_name == "$piece_sauna_wrisks";
                bool isBucket = piece.m_name == "$piece_sauna_bucket";
                if (!isWhisks && !isBucket)
                {
                    continue;
                }

                ZNetView nview = piece.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid())
                {
                    continue;
                }

                SaunaStove linkedStove = FindClosestForVisualLink(
                    piece.transform.position,
                    SaunaVisualLink.MaxLinkDistance);

                if (linkedStove != activeStove)
                {
                    continue;
                }

                if (isWhisks)
                {
                    hasWhisks = true;
                }
                else if (isBucket)
                {
                    hasBucket = true;
                }

                if (hasWhisks && hasBucket)
                {
                    return 2;
                }
            }

            return (hasWhisks ? 1 : 0) + (hasBucket ? 1 : 0);
        }

        /// Editor tool: instantly consume all fuel in the nearest sauna stove.
        /// SetFuel forwards to the owner through RPC, so it also works in multiplayer.
        public static void ExtinguishNearest(UnityEngine.Vector3 position)
        {
            SaunaStove nearest = null;
            float best = 20f * 20f;

            foreach (SaunaStove stove in s_all)
            {
                if (stove == null || stove.m_fireplace == null)
                {
                    continue;
                }

                float d = (stove.transform.position - position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = stove;
                }
            }

            if (nearest == null)
            {
                Jotunn.Logger.LogInfo("extinguish: no sauna stove within 20 m");
                return;
            }

            nearest.m_fireplace.SetFuel(0f);
            Jotunn.Logger.LogInfo("extinguish: stove fuel set to 0");
        }

        private void ApplyFire()
        {
            for (int i = 0; i < m_fireRoots.Count; i++)
            {
                Transform t = m_fireRoots[i];
                if (t == null)
                {
                    continue;
                }

                t.localPosition = m_firePos[i] + UnityEngine.Vector3.up * StoveVisual.FireOffsetY;
                t.localScale = m_fireScale[i] * StoveVisual.FireScale;

                foreach (ParticleSystem ps in t.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
                    if (psr != null)
                    {
                        psr.enabled = StoveVisual.KeepFlame(ps.gameObject.name);
                    }
                }
            }

            // Calculate heat radius from the original value so repeated edits do not accumulate.
            for (int i = 0; i < m_warmthColliders.Count && i < m_warmthRadii.Count; i++)
            {
                if (m_warmthColliders[i] != null)
                {
                    m_warmthColliders[i].radius = m_warmthRadii[i] * StoveVisual.WarmthRadius;
                }
            }

            // FlameSize affects the ParticleSystems currently visible for FlameSet.
            // Scale the system transform rather than only startSize because much of the
            // vanilla flame appearance comes from other ParticleSystem modules,
            // making startSize alone barely noticeable.
            int count = Mathf.Min(
                m_fireParticles.Count,
                Mathf.Min(m_fireParticlePos.Count,
                    Mathf.Min(m_fireParticleScale.Count, m_fireParticleStartSize.Count)));

            for (int i = 0; i < count; i++)
            {
                ParticleSystem ps = m_fireParticles[i];
                if (ps == null)
                {
                    continue;
                }

                bool visibleFlame = StoveVisual.KeepFlame(ps.gameObject.name);

                // Always start from the original values so editor adjustments
                // do not accumulate on every RefreshVisual().
                ps.transform.localPosition = m_fireParticlePos[i];
                ps.transform.localScale = m_fireParticleScale[i];

                ParticleSystem.MainModule main = ps.main;
                main.startSizeMultiplier = m_fireParticleStartSize[i];

                if (!visibleFlame)
                {
                    continue;
                }

                // If a ParticleSystem is on a separate child transform, scale that child.
                // This changes the whole flame visual without changing the Fireplace root.
                bool isFireRoot = false;
                for (int r = 0; r < m_fireRoots.Count; r++)
                {
                    if (m_fireRoots[r] == ps.transform)
                    {
                        isFireRoot = true;
                        break;
                    }
                }

                if (!isFireRoot)
                {
                    ps.transform.localPosition =
                        m_fireParticlePos[i] + UnityEngine.Vector3.up * StoveVisual.FlameOffsetY;
                    ps.transform.localScale =
                        m_fireParticleScale[i] * StoveVisual.FlameSize;
                }
                else
                {
                    // Rare fallback: if the ParticleSystem is directly on the Fireplace root,
                    // leave the root untouched so heat/collider positions do not move and adjust startSize instead.
                    main.startSizeMultiplier =
                        m_fireParticleStartSize[i] * StoveVisual.FlameSize;
                }
            }
        }

        private void SetupAudio()
        {
            AudioSource template = GetComponentInChildren<AudioSource>(true);

            m_audio = gameObject.AddComponent<AudioSource>();
            m_audio.playOnAwake = false;
            m_audio.loop = false;
            m_audio.spatialBlend = 1f;
            m_audio.rolloffMode = AudioRolloffMode.Linear;
            m_audio.minDistance = 3f;
            m_audio.maxDistance = 32f;

            if (template != null)
            {
                m_audio.outputAudioMixerGroup = template.outputAudioMixerGroup;
            }
        }

        private double NetTime()
        {
            return ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;
        }

        public bool Pour(Humanoid user)
        {
            if (m_fireplace == null || !m_fireplace.IsBurning())
            {
                if (user != null)
                {
                    user.Message(MessageHud.MessageType.Center, "$msg_sauna_notburning");
                }
                return false;
            }

            bool networked = m_nview != null && m_nview.IsValid();

            // The bucket increases the amount of steam per pour, not cloud speed or TTL.
            int saunaTier = GetWellSteamedSaunaTier();
            int cloudCount = saunaTier >= 3
                ? SteamTuning.CloudsPerPourWithBucket
                : SteamTuning.CloudsPerPour;

            if (networked)
            {
                double now = NetTime();
                float last = m_nview.GetZDO().GetFloat(ZdoLastPour, -9999f);

                if (now - last < PourCooldown)
                {
                    if (user != null)
                    {
                        user.Message(MessageHud.MessageType.Center, "$msg_sauna_wait");
                    }
                    return false;
                }

                if (!m_nview.IsOwner())
                {
                    m_nview.ClaimOwnership();
                }

                m_nview.GetZDO().Set(ZdoLastPour, (float)now);
                m_nview.InvokeRPC(ZNetView.Everybody, RpcName, cloudCount);
            }
            else
            {
                if (Time.time - m_localLastPour < PourCooldown)
                {
                    if (user != null)
                    {
                        user.Message(MessageHud.MessageType.Center, "$msg_sauna_wait");
                    }
                    return false;
                }

                m_localLastPour = Time.time;
                StartBurst(cloudCount);
            }

            // An infused bucket is consumed only by a SUCCESSFUL pour
            // and only when the sauna is truly Tier 3 (stove + whisks + bucket).
            int infusionType = SaunaBucket.ConsumeLinkedInfusion(this);
            if (infusionType != SaunaMeadSystem.None)
            {
                int recipients = SaunaMeadSystem.DistributeFromStove(this, infusionType);

                // Even if no eligible player is nearby, the player who poured receives
                // clear feedback that the mead was released into the steam.
                if (recipients == 0 && user != null)
                {
                    string fmt = Localization.instance != null
                        ? Localization.instance.Localize("$msg_sauna_mead_aroma")
                        : "The aroma of {0} fills the sauna";
                    user.Message(
                        MessageHud.MessageType.Center,
                        string.Format(fmt, SaunaMeadSystem.LocalizedName(infusionType)));
                }
            }
            else if (user != null)
            {
                user.Message(MessageHud.MessageType.Center, "$msg_sauna_pour");
            }

            return true;
        }

        private void RPC_Pour(long sender, int cloudCount)
        {
            StartBurst(cloudCount);
        }

        private void StartBurst(int cloudCount)
        {
            m_burstCloudCount = Mathf.Max(1, cloudCount);
            m_burstLeft = BurstDuration;
            m_spawnAccum = 0f;

            if (PourClip != null && m_audio != null)
            {
                m_audio.PlayOneShot(PourClip, PourVolume);
            }
        }

        private void Update()
        {
            // There is no need to check burning every frame; twice per second is more than enough.
            m_heatTimer -= Time.deltaTime;
            if (m_heatTimer <= 0f)
            {
                m_heatTimer = 0.5f;
                ApplyHeat(IsHot(), false);
            }

            if (m_burstLeft <= 0f || SteamPrefab == null)
            {
                return;
            }

            m_burstLeft -= Time.deltaTime;

            float perSecond = m_burstCloudCount / BurstDuration;
            m_spawnAccum += perSecond * Time.deltaTime;

            while (m_spawnAccum >= 1f)
            {
                m_spawnAccum -= 1f;
                SpawnCloud();
            }
        }

        private void SpawnCloud()
        {
            // This cap is shared with vanilla smoke. When exceeded, remove the oldest cloud.
            if (Smoke.GetTotalSmoke() >= SteamTuning.MaxClouds)
            {
                Smoke.FadeOldest();
            }

            UnityEngine.Vector2 flat = UnityEngine.Random.insideUnitCircle * BurstRadius;
            UnityEngine.Vector3 pos = transform.position + new UnityEngine.Vector3(
                flat.x,
                BurstHeight + UnityEngine.Random.Range(0f, 0.4f),
                flat.y);

            GameObject cloud = Instantiate(SteamPrefab, pos, UnityEngine.Quaternion.identity);
            SteamTuning.Apply(cloud);
        }
    }

    /// <summary>
    /// Purely visual connection between new sauna pieces and the Sauna Stove.
    /// The accessories do not use StationExtension and the stove does not use CraftingStation;
    /// the mod only borrows vanilla vfx_ExtensionConnection from forge_ext1.
    /// </summary>
    internal static class SaunaVisualLink
    {
        // Close to vanilla workbench/forge extension distance.
        // Beyond this distance the line is simply hidden; placement is NOT blocked.
        public const float MaxLinkDistance = 5f;

        private static GameObject s_connectionPrefab;
        private static bool s_connectionPrefabSearched;
        private static GameObject s_connectionInstance;

        public static void UpdateForPlacementGhost(GameObject ghost)
        {
            if (ghost == null)
            {
                Hide();
                return;
            }

            Piece piece = ghost.GetComponent<Piece>();
            if (!IsLinkedSaunaPiece(piece, ghost))
            {
                Hide();
                return;
            }

            SaunaStove stove = SaunaStove.FindClosestForVisualLink(
                ghost.transform.position,
                MaxLinkDistance);

            if (stove == null)
            {
                Hide();
                return;
            }

            GameObject connectionPrefab = ConnectionPrefab();
            if (connectionPrefab == null)
            {
                Hide();
                return;
            }

            UnityEngine.Vector3 from = VisualCenter(ghost);
            UnityEngine.Vector3 to = stove.VisualLinkPoint();
            UnityEngine.Vector3 direction = to - from;

            if (direction.sqrMagnitude < 0.0001f)
            {
                Hide();
                return;
            }

            if (s_connectionInstance == null)
            {
                s_connectionInstance = UnityEngine.Object.Instantiate(
                    connectionPrefab,
                    from,
                    UnityEngine.Quaternion.identity);
                s_connectionInstance.name = "sauna_visual_connection";
            }

            float distance = direction.magnitude;
            s_connectionInstance.transform.position = from;
            s_connectionInstance.transform.rotation =
                UnityEngine.Quaternion.LookRotation(direction / distance);
            s_connectionInstance.transform.localScale =
                new UnityEngine.Vector3(1f, 1f, distance);

            if (!s_connectionInstance.activeSelf)
            {
                s_connectionInstance.SetActive(true);
            }
        }

        public static void Hide()
        {
            if (s_connectionInstance != null && s_connectionInstance.activeSelf)
            {
                s_connectionInstance.SetActive(false);
            }
        }

        private static bool IsLinkedSaunaPiece(Piece piece, GameObject ghost)
        {
            if (piece != null)
            {
                // m_name is the most reliable identifier for the placement ghost of our Pieces.
                if (piece.m_name == "$piece_sauna_wrisks" ||
                    piece.m_name == "$piece_sauna_bucket")
                {
                    return true;
                }
            }

            // Small fallback to prefab name in case a localization token changes in the future.
            string prefabName = Utils.GetPrefabName(ghost);
            return prefabName == "sauna_wrisks" || prefabName == "sauna_bucket";
        }

        private static GameObject ConnectionPrefab()
        {
            if (s_connectionPrefabSearched)
            {
                return s_connectionPrefab;
            }

            if (ZNetScene.instance == null)
            {
                // Do not cache failure permanently; during early loading the scene may not be ready yet.
                return null;
            }

            s_connectionPrefabSearched = true;

            // forge_ext1 and workbench extensions use the same golden connection VFX.
            string[] donors = { "forge_ext1", "piece_workbench_ext1" };
            foreach (string donorName in donors)
            {
                GameObject donor = ZNetScene.instance.GetPrefab(donorName);
                if (donor == null)
                {
                    continue;
                }

                StationExtension extension = donor.GetComponent<StationExtension>();
                if (extension != null && extension.m_connectionPrefab != null)
                {
                    s_connectionPrefab = extension.m_connectionPrefab;
                    Jotunn.Logger.LogInfo(
                        $"sauna visual link: borrowed connection VFX from {donorName}");
                    break;
                }
            }

            if (s_connectionPrefab == null)
            {
                Jotunn.Logger.LogWarning(
                    "sauna visual link: vanilla extension connection VFX not found; visual links disabled");
            }

            return s_connectionPrefab;
        }

        private static UnityEngine.Vector3 VisualCenter(GameObject ghost)
        {
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>(true);
            bool haveBounds = false;
            Bounds bounds = new Bounds(ghost.transform.position, UnityEngine.Vector3.zero);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                // Ignore possible helper particle/renderers; only the visual center of the Piece model is needed.
                if (renderer is ParticleSystemRenderer || renderer is LineRenderer)
                {
                    continue;
                }

                if (!haveBounds)
                {
                    bounds = renderer.bounds;
                    haveBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!haveBounds)
            {
                return ghost.transform.position + UnityEngine.Vector3.up * 0.25f;
            }

            return bounds.center;
        }
    }

    /// Called by the game only while a placement ghost for the selected build piece exists.
    /// Therefore the golden line is visible only while our whisks or bucket are selected in the hammer.
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class SaunaPlacementVisualLinkPatch
    {
        private static void Postfix(Player __instance, GameObject ___m_placementGhost)
        {
            if (__instance != Player.m_localPlayer)
            {
                return;
            }

            SaunaVisualLink.UpdateForPlacementGhost(___m_placementGhost);
        }
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
    internal static class SaunaInteractPatch
    {
        private static bool Prefix(Fireplace __instance, Humanoid user, bool hold, bool alt, ref bool __result)
        {
            if (hold || !alt)
            {
                return true;
            }

            SaunaStove stove = __instance.GetComponent<SaunaStove>();
            if (stove == null)
            {
                return true;
            }

            __result = stove.Pour(user);
            return false;
        }
    }

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
    internal static class SaunaHoverPatch
    {
        private static void Postfix(Fireplace __instance, ref string __result)
        {
            if (__instance.GetComponent<SaunaStove>() == null)
            {
                return;
            }

            __result += "\n[<color=yellow><b>Shift + E</b></color>] " +
                Localization.instance.Localize("$piece_sauna_pour");
        }
    }

    [HarmonyPatch(typeof(Player), "GetComfortLevel")]
    internal static class SaunaComfortPatch
    {
        private static void Postfix(Player __instance, ref int __result)
        {
            if (__instance == null || !SaunaPlugin.SaunaComfortEnabled)
            {
                return;
            }

            // Sauna accessories are intentionally not normal comfort furniture.
            // They contribute only inside a sheltered, active sauna.
            SEMan seman = __instance.GetSEMan();
            if (seman == null || !seman.HaveStatusEffect(SEMan.s_statusEffectShelter))
            {
                return;
            }

            int bonus = SaunaStove.GetSaunaComfortBonusNear(__instance.transform.position);
            if (bonus > 0)
            {
                __result += bonus;
            }
        }
    }

    [HarmonyPatch(typeof(SEMan), "Internal_AddStatusEffect")]
    internal static class BlockColdPatch
    {
        private static bool Prefix(SEMan __instance, int nameHash, ref StatusEffect __result)
        {
            // Well Steamed protects against Cold, but Wet remains active
            // because it is still needed for vanilla water-drop visuals on the character.
            if (nameHash != SEMan.s_statusEffectCold)
            {
                return true;
            }

            if (SaunaPlugin.WellSteamedHash == 0)
            {
                return true;
            }

            if (__instance.m_character == null || !__instance.m_character.IsPlayer())
            {
                return true;
            }

            if (!__instance.HaveStatusEffect(SaunaPlugin.WellSteamedHash))
            {
                return true;
            }

            __result = null;
            return false;
        }
    }

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


    /// <summary>
    /// Small vanilla tier star shown on top of the Well Steamed icon.
    /// No custom star PNG is used; clone the existing MinLevel UI element
    /// from InventoryGui into the HUD.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
    internal static class SaunaWellSteamedTierBadgePatch
    {
        private const string BadgeName = "SaunaWellSteamedTierBadge";

        // Editable position and scale parameters for the tier star on Well Steamed.
        public static float BadgeOffsetX = 7f;
        public static float BadgeOffsetY = -60f;
        public static float BadgeScale = 0.55f;

        private static readonly List<GameObject> s_badges = new List<GameObject>();
        private static readonly List<UnityEngine.UI.Image> s_hudImages = new List<UnityEngine.UI.Image>();
        private static bool s_loggedSource;
        private static bool s_loggedMissingSource;

        private static void Postfix(Hud __instance)
        {
            HideTrackedBadges();

            if (__instance == null || Player.m_localPlayer == null ||
                SaunaPlugin.WellSteamedHash == 0)
            {
                return;
            }

            SEMan seman = Player.m_localPlayer.GetSEMan();
            if (seman == null)
            {
                return;
            }

            SE_WellSteamed live = seman.GetStatusEffect(SaunaPlugin.WellSteamedHash) as SE_WellSteamed;
            if (live == null || live.m_icon == null)
            {
                return;
            }

            int tier = Mathf.Clamp(live.SaunaTier, 1, 3);

            // HUD does not expose a direct StatusEffect -> Image reference,
            // so locate the active Image reliably by the unique sprite of our effect.
            s_hudImages.Clear();
            __instance.GetComponentsInChildren<UnityEngine.UI.Image>(true, s_hudImages);
            foreach (UnityEngine.UI.Image image in s_hudImages)
            {
                if (image == null || !image.gameObject.activeInHierarchy || image.sprite != live.m_icon)
                {
                    continue;
                }

                GameObject badge = GetOrCreateBadge(image);
                if (badge == null)
                {
                    continue;
                }

                ApplyBadgeTransform(badge);

                TMPro.TMP_Text tierText = badge.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (tierText != null)
                {
                    tierText.text = tier.ToString();
                    // The vanilla MinLevel text is recolored red when a crafting
                    // station requirement is not met. Our clone must not inherit
                    // that transient UI state, so keep the sauna tier number white.
                    tierText.color = UnityEngine.Color.white;
                    tierText.raycastTarget = false;
                    tierText.gameObject.SetActive(true);
                }

                badge.SetActive(true);
            }
        }

        private static void HideTrackedBadges()
        {
            for (int i = s_badges.Count - 1; i >= 0; i--)
            {
                GameObject badge = s_badges[i];
                if (badge == null)
                {
                    s_badges.RemoveAt(i);
                    continue;
                }

                badge.SetActive(false);
            }
        }

        private static GameObject GetOrCreateBadge(UnityEngine.UI.Image statusIcon)
        {
            Transform existing = statusIcon.transform.Find(BadgeName);
            if (existing != null)
            {
                GameObject existingGo = existing.gameObject;
                if (!s_badges.Contains(existingGo))
                {
                    s_badges.Add(existingGo);
                }
                return existingGo;
            }

            UnityEngine.UI.Image sourceImage;
            TMPro.TMP_Text sourceText;
            if (!TryGetVanillaStationLevelUi(out sourceImage, out sourceText))
            {
                if (!s_loggedMissingSource)
                {
                    s_loggedMissingSource = true;
                    Jotunn.Logger.LogWarning("Well Steamed tier badge: vanilla MinLevel UI source not found");
                }
                return null;
            }

            GameObject badge = UnityEngine.Object.Instantiate(sourceImage.gameObject, statusIcon.transform, false);
            badge.name = BadgeName;
            badge.SetActive(true);

            ApplyBadgeTransform(badge);

            UnityEngine.UI.Image badgeImage = badge.GetComponent<UnityEngine.UI.Image>();
            if (badgeImage != null)
            {
                badgeImage.raycastTarget = false;
            }

            TMPro.TMP_Text badgeText = badge.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (badgeText != null)
            {
                badgeText.color = UnityEngine.Color.white;
                badgeText.raycastTarget = false;
                badgeText.gameObject.SetActive(true);
            }

            s_badges.Add(badge);

            if (!s_loggedSource)
            {
                s_loggedSource = true;
                Jotunn.Logger.LogInfo(
                    $"Well Steamed tier badge: vanilla source='{sourceImage.gameObject.name}', " +
                    $"sprite='{sourceImage.sprite?.name}', size={sourceImage.rectTransform.sizeDelta}, " +
                    $"text='{sourceText?.gameObject.name}'");
            }

            return badge;
        }

        private static void ApplyBadgeTransform(GameObject badge)
        {
            if (badge == null)
            {
                return;
            }

            RectTransform rt = badge.transform as RectTransform;
            if (rt == null)
            {
                return;
            }

            rt.anchorMin = new UnityEngine.Vector2(0f, 1f);
            rt.anchorMax = new UnityEngine.Vector2(0f, 1f);
            rt.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new UnityEngine.Vector2(BadgeOffsetX, BadgeOffsetY);
            rt.localRotation = UnityEngine.Quaternion.identity;
            rt.localScale = UnityEngine.Vector3.one * Mathf.Max(0.1f, BadgeScale);
        }

        private static bool TryGetVanillaStationLevelUi(
            out UnityEngine.UI.Image image,
            out TMPro.TMP_Text text)
        {
            image = null;
            text = null;

            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
            {
                return false;
            }

            // These fields were confirmed from a Valheim 1.0.12 dump:
            // Inventory_screen/root/Crafting/Decription/requirements/level/MinLevel
            // and its MinLevel/level_text child.
            System.Reflection.FieldInfo imageField =
                AccessTools.Field(typeof(InventoryGui), "m_minStationLevelIcon");
            System.Reflection.FieldInfo textField =
                AccessTools.Field(typeof(InventoryGui), "m_minStationLevelText");

            if (imageField != null)
            {
                image = imageField.GetValue(gui) as UnityEngine.UI.Image;
            }
            if (textField != null)
            {
                text = textField.GetValue(gui) as TMPro.TMP_Text;
            }

            return image != null && image.sprite != null && text != null;
        }
    }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class SaunaPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "nikita.valheim.sauna";
        public const string PluginName = "SaunaMod";
        public const string PluginVersion = "2.0.1";
        private const string ConfigFileName = "nekitker.saunamod.cfg";

        private const string DefaultStoveRecipe = "Wood:10,Coal:5,Stone:40";
        private const string DefaultWhisksRecipe = "FineWood:5,BronzeNails:1";
        private const string DefaultBucketRecipe = "Iron:5,FineWood:10";

        private const int SteamLayer = 30;
        private const int SmokeLayer = 31;

        private const float CheckInterval = 0.5f;

        // Gameplay defaults. BindConfig() may replace these values from the .cfg file.
        private static float SteamTimeToBuff = 20f;
        // Calm healing only while the player is physically inside sauna steam.
        // 0.5 HP/s = 30 HP/min: useful recovery, but deliberately not a combat heal.
        private static float SteamHealPerSecond = 0.5f;
        private static float DetectRadius = 0.6f;
        private const float SteamVfxDuration = 60f;
        private const float PlayerVfxHeight = 1.3f;

        private static readonly float[] WellSteamedTimeTiers = { 300f, 600f, 900f };

        // Public release: the F7 live editor is opt-in.
        internal static bool EditorEnabled { get; private set; }

        // Sauna comfort is server-authoritative and can be disabled in the synchronized config.
        internal static bool SaunaComfortEnabled { get; private set; } = true;

        private ConfigFile _saunaConfig;
        private ConfigEntry<bool> _cfgEnableEditor;
        private ConfigEntry<bool> _cfgEnableSaunaComfort;

        private ConfigEntry<string> _cfgStoveRecipe;
        private ConfigEntry<string> _cfgWhisksRecipe;
        private ConfigEntry<string> _cfgBucketRecipe;

        private ConfigEntry<float> _cfgSteamTimeToBuff;
        private ConfigEntry<float> _cfgSteamHealPerSecond;
        private ConfigEntry<float> _cfgDetectRadius;
        private ConfigEntry<float> _cfgSteamGrace;

        private ConfigEntry<float> _cfgWellSteamedTier1Minutes;
        private ConfigEntry<float> _cfgWellSteamedTier2Minutes;
        private ConfigEntry<float> _cfgWellSteamedTier3Minutes;

        private ConfigEntry<float> _cfgMeadDurationMultiplier;

        private ConfigEntry<float> _cfgSteamLifetime;
        private ConfigEntry<float> _cfgSteamFadeTime;
        private ConfigEntry<float> _cfgSteamSpread;
        private ConfigEntry<float> _cfgSteamRise;
        private ConfigEntry<float> _cfgSteamForce;
        private ConfigEntry<float> _cfgSteamPush;
        private ConfigEntry<float> _cfgSteamCloudSize;
        private ConfigEntry<int> _cfgCloudsPerPour;
        private ConfigEntry<int> _cfgCloudsPerPourWithBucket;
        private ConfigEntry<int> _cfgMaxClouds;

        public static int WellSteamedHash;

        private static GameObject _prefabContainer;
        private static GameObject _steamVfxPrefab;
        private static GameObject _stovePrefab;
        private static GameObject _wrisksPrefab;
        private static Sprite _wrisksFallbackIcon;
        private static GameObject _bucketPrefab;
        private static int _iconRenderCount;
        private static SaunaPlugin _instance;

        private static SE_Stats _steaming;
        private static SE_WellSteamed _wellSteamed;

        private float _checkTimer;
        private float _steamTime;
        private int _timeTier;

        // Seconds elapsed since steam contact was lost. This gap is counted
        // only if the player returns before SteamTuning.Grace expires.
        private float _gapTime;
        private bool _worldReady;

        private static readonly CustomLocalization _loc =
            LocalizationManager.Instance.GetLocalization();

        private void Awake()
        {
            _instance = this;

            // Use a stable public-facing config name without changing the plugin GUID.
            _saunaConfig = new ConfigFile(
                Path.Combine(BepInEx.Paths.ConfigPath, ConfigFileName),
                true);

            // This is a custom-named config file, so Jotunn must be told explicitly
            // to include it in server-to-client configuration synchronization.
            SynchronizationManager.Instance.RegisterCustomConfig(_saunaConfig);

            BindConfig();

            // Apply local/server-side edits immediately. Jotunn will also raise its
            // synchronization event when authoritative server values reach a client.
            _saunaConfig.SettingChanged += OnSaunaConfigSettingChanged;
            SynchronizationManager.OnConfigurationSynchronized += (sender, args) =>
            {
                if (_instance != null)
                {
                    _instance.ApplyConfigValues();
                    _instance.ApplyRecipeConfigToRegisteredPieces();
                    Jotunn.Logger.LogInfo(
                        $"SaunaMod config synchronized; editor={(_instance._cfgEnableEditor.Value ? "enabled" : "disabled")}, " +
                        $"comfort={(SaunaComfortEnabled ? "enabled" : "disabled")}, " +
                        $"wellSteamedAfter={SteamTimeToBuff:0.##}s, meadX={SaunaMeadTuning.DurationMultiplier:0.##}");
                }
            };

            ApplyConfigValues();

            Jotunn.Logger.LogInfo(
                $"SaunaMod loaded; editor={(EditorEnabled ? "enabled" : "disabled")}, " +
                $"comfort={(SaunaComfortEnabled ? "enabled" : "disabled")}, " +
                $"wellSteamedAfter={SteamTimeToBuff:0.##}s, meadX={SaunaMeadTuning.DurationMultiplier:0.##}");
            AddLocalization();

            // All long-established SaunaMod patches are applied normally.
            // The experimental Humanoid.UseItem hook is intentionally patched
            // separately below, so failure of that one hook cannot stop prefab
            // registration and make every sauna piece disappear from the game.
            Harmony harmony = new Harmony(PluginGUID);
            harmony.PatchAll();
            TryPatchBucketMeadHotbarUse(harmony);

            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
            StartCoroutine(LoadPourSound());
        }

        private static ConfigDescription SyncedConfigDescription(
            string description,
            AcceptableValueBase acceptableValues = null)
        {
            return new ConfigDescription(
                description,
                acceptableValues,
                new ConfigurationManagerAttributes
                {
                    IsAdminOnly = true
                });
        }

        private void OnSaunaConfigSettingChanged(object sender, SettingChangedEventArgs args)
        {
            ApplyConfigValues();
            ApplyRecipeConfigToRegisteredPieces();
        }

        private void BindConfig()
        {
            _cfgEnableEditor = _saunaConfig.Bind(
                "General",
                "EnableEditor",
                false,
                SyncedConfigDescription(
                    "Enables the in-game SaunaMod live editor. Press F7 to open it. Intended for testing and tuning."));

            _cfgEnableSaunaComfort = _saunaConfig.Bind(
                "Comfort",
                "EnableSaunaComfort",
                true,
                SyncedConfigDescription(
                    "Enables conditional sauna comfort. Whisks and bucket each add +1 comfort only near a burning sauna stove while sheltered."));

            _cfgStoveRecipe = _saunaConfig.Bind(
                "Recipes",
                "SaunaStove",
                DefaultStoveRecipe,
                SyncedConfigDescription(
                    "Crafting recipe for the sauna stove. Format: ItemPrefab:Amount,ItemPrefab:Amount. Example: Wood:10,Coal:5,Stone:40."));

            _cfgWhisksRecipe = _saunaConfig.Bind(
                "Recipes",
                "SaunaWhisks",
                DefaultWhisksRecipe,
                SyncedConfigDescription(
                    "Crafting recipe for the sauna whisks. Format: ItemPrefab:Amount,ItemPrefab:Amount. Example: FineWood:5,BronzeNails:1."));

            _cfgBucketRecipe = _saunaConfig.Bind(
                "Recipes",
                "SaunaBucket",
                DefaultBucketRecipe,
                SyncedConfigDescription(
                    "Crafting recipe for the sauna bucket. Format: ItemPrefab:Amount,ItemPrefab:Amount. Example: Iron:5,FineWood:10."));

            _cfgSteamTimeToBuff = _saunaConfig.Bind(
                "Gameplay",
                "SecondsToWellSteamed",
                20f,
                SyncedConfigDescription(
                    "Seconds of accumulated contact with sauna steam required to gain or advance Well Steamed.",
                    new AcceptableValueRange<float>(1f, 300f)));

            _cfgSteamHealPerSecond = _saunaConfig.Bind(
                "Gameplay",
                "SteamHealPerSecond",
                0.5f,
                SyncedConfigDescription(
                    "Health restored per second while the player is physically inside sauna steam. Set to 0 to disable healing.",
                    new AcceptableValueRange<float>(0f, 10f)));

            _cfgDetectRadius = _saunaConfig.Bind(
                "Gameplay",
                "SteamDetectionRadius",
                0.6f,
                SyncedConfigDescription(
                    "Radius in metres used to detect whether the player is touching a sauna steam cloud.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            _cfgSteamGrace = _saunaConfig.Bind(
                "Gameplay",
                "SteamContactGraceSeconds",
                3f,
                SyncedConfigDescription(
                    "How long a short gap between steam clouds may last without resetting steaming progress.",
                    new AcceptableValueRange<float>(0f, 20f)));

            _cfgWellSteamedTier1Minutes = _saunaConfig.Bind(
                "WellSteamed",
                "Tier1DurationMinutes",
                5f,
                SyncedConfigDescription(
                    "Duration of the first Well Steamed time tier in minutes.",
                    new AcceptableValueRange<float>(0.5f, 120f)));

            _cfgWellSteamedTier2Minutes = _saunaConfig.Bind(
                "WellSteamed",
                "Tier2DurationMinutes",
                10f,
                SyncedConfigDescription(
                    "Duration of the second Well Steamed time tier in minutes.",
                    new AcceptableValueRange<float>(0.5f, 120f)));

            _cfgWellSteamedTier3Minutes = _saunaConfig.Bind(
                "WellSteamed",
                "Tier3DurationMinutes",
                15f,
                SyncedConfigDescription(
                    "Duration of the third Well Steamed time tier in minutes. This tier requires sauna whisks.",
                    new AcceptableValueRange<float>(0.5f, 120f)));

            _cfgMeadDurationMultiplier = _saunaConfig.Bind(
                "Mead",
                "DurationMultiplier",
                1.20f,
                SyncedConfigDescription(
                    "Multiplier applied to the normal duration of resistance mead distributed through the sauna bucket.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            _cfgSteamLifetime = _saunaConfig.Bind(
                "Steam",
                "CloudLifetimeSeconds",
                40f,
                SyncedConfigDescription(
                    "Lifetime of a sauna steam cloud before it begins to fade.",
                    new AcceptableValueRange<float>(SteamTuning.MinLifetime, 180f)));

            _cfgSteamFadeTime = _saunaConfig.Bind(
                "Steam",
                "CloudFadeSeconds",
                4.4f,
                SyncedConfigDescription(
                    "Time a steam cloud takes to fade after its lifetime ends.",
                    new AcceptableValueRange<float>(0.1f, 30f)));

            _cfgSteamSpread = _saunaConfig.Bind(
                "Steam",
                "HorizontalSpread",
                1.0f,
                SyncedConfigDescription(
                    "Horizontal target speed of steam clouds.",
                    new AcceptableValueRange<float>(0f, 10f)));

            _cfgSteamRise = _saunaConfig.Bind(
                "Steam",
                "RiseSpeed",
                1.45f,
                SyncedConfigDescription(
                    "Vertical target speed of steam clouds.",
                    new AcceptableValueRange<float>(0f, 10f)));

            _cfgSteamForce = _saunaConfig.Bind(
                "Steam",
                "MovementForce",
                0.25f,
                SyncedConfigDescription(
                    "How quickly steam accelerates toward its target movement.",
                    new AcceptableValueRange<float>(0f, 10f)));

            _cfgSteamPush = _saunaConfig.Bind(
                "Steam",
                "CloudPush",
                0.5f,
                SyncedConfigDescription(
                    "How strongly overlapping steam clouds separate from each other.",
                    new AcceptableValueRange<float>(0f, 10f)));

            _cfgSteamCloudSize = _saunaConfig.Bind(
                "Steam",
                "CloudSize",
                2.9f,
                SyncedConfigDescription(
                    "Physical size of each steam cloud. This mainly affects distribution and detection, not the smoke sprite itself.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            _cfgCloudsPerPour = _saunaConfig.Bind(
                "Steam",
                "CloudsPerPour",
                30,
                SyncedConfigDescription(
                    "Number of steam clouds created by a normal pour.",
                    new AcceptableValueRange<int>(1, 200)));

            _cfgCloudsPerPourWithBucket = _saunaConfig.Bind(
                "Steam",
                "CloudsPerPourWithBucket",
                50,
                SyncedConfigDescription(
                    "Number of steam clouds created when the sauna has the bucket upgrade.",
                    new AcceptableValueRange<int>(1, 200)));

            _cfgMaxClouds = _saunaConfig.Bind(
                "Steam",
                "MaxClouds",
                100,
                SyncedConfigDescription(
                    "Global smoke/steam cloud cap used by SaunaMod. Very high values may be visually wasteful or affect performance.",
                    new AcceptableValueRange<int>(10, 1000)));
        }

        private void ApplyConfigValues()
        {
            EditorEnabled = _cfgEnableEditor != null && _cfgEnableEditor.Value;
            SaunaComfortEnabled = _cfgEnableSaunaComfort == null || _cfgEnableSaunaComfort.Value;

            SteamTimeToBuff = Mathf.Max(1f, _cfgSteamTimeToBuff.Value);
            SteamHealPerSecond = Mathf.Max(0f, _cfgSteamHealPerSecond.Value);
            DetectRadius = Mathf.Max(0.1f, _cfgDetectRadius.Value);
            SteamTuning.Grace = Mathf.Max(0f, _cfgSteamGrace.Value);

            // Keep the three time tiers monotonic even if somebody edits the cfg by hand.
            WellSteamedTimeTiers[0] = Mathf.Max(30f, _cfgWellSteamedTier1Minutes.Value * 60f);
            WellSteamedTimeTiers[1] = Mathf.Max(WellSteamedTimeTiers[0], _cfgWellSteamedTier2Minutes.Value * 60f);
            WellSteamedTimeTiers[2] = Mathf.Max(WellSteamedTimeTiers[1], _cfgWellSteamedTier3Minutes.Value * 60f);

            SaunaMeadTuning.DurationMultiplier = Mathf.Max(0.1f, _cfgMeadDurationMultiplier.Value);

            SteamTuning.Lifetime = Mathf.Max(SteamTuning.MinLifetime, _cfgSteamLifetime.Value);
            SteamTuning.FadeTime = Mathf.Max(0.1f, _cfgSteamFadeTime.Value);
            SteamTuning.Spread = Mathf.Max(0f, _cfgSteamSpread.Value);
            SteamTuning.Rise = Mathf.Max(0f, _cfgSteamRise.Value);
            SteamTuning.Force = Mathf.Max(0f, _cfgSteamForce.Value);
            SteamTuning.Push = Mathf.Max(0f, _cfgSteamPush.Value);
            SteamTuning.CloudSize = Mathf.Max(0.1f, _cfgSteamCloudSize.Value);
            SteamTuning.CloudsPerPour = Mathf.Max(1, _cfgCloudsPerPour.Value);
            SteamTuning.CloudsPerPourWithBucket = Mathf.Max(1, _cfgCloudsPerPourWithBucket.Value);
            SteamTuning.MaxClouds = Mathf.Max(10, _cfgMaxClouds.Value);
        }

        /// <summary>
        /// Parses a synchronized recipe string such as "FineWood:5,BronzeNails:1".
        /// Invalid entries fall back to the built-in recipe so a typo cannot make a piece unusable.
        /// </summary>
        private static RequirementConfig[] ParseRecipeConfig(
            string recipe,
            string fallbackRecipe,
            string label)
        {
            RequirementConfig[] parsed = TryParseRecipeConfig(recipe);
            if (parsed != null && RecipeItemsAvailable(parsed))
            {
                return parsed;
            }

            Jotunn.Logger.LogWarning(
                $"{label} recipe '{recipe}' is invalid or contains an unknown item; using default '{fallbackRecipe}'");

            parsed = TryParseRecipeConfig(fallbackRecipe);
            return parsed ?? new RequirementConfig[0];
        }

        private static bool RecipeItemsAvailable(RequirementConfig[] requirements)
        {
            if (requirements == null || ObjectDB.instance == null)
            {
                return true;
            }

            foreach (RequirementConfig requirement in requirements)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(requirement.Item);
                if (prefab == null || prefab.GetComponent<ItemDrop>() == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static RequirementConfig[] TryParseRecipeConfig(
            string recipe)
        {
            if (recipe == null)
            {
                return null;
            }

            string trimmedRecipe = recipe.Trim();
            if (trimmedRecipe.Length == 0)
            {
                // An empty recipe deliberately makes the piece free to build.
                return new RequirementConfig[0];
            }

            List<RequirementConfig> requirements = new List<RequirementConfig>();
            string[] entries = trimmedRecipe.Split(
                new[] { ',' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string rawEntry in entries)
            {
                string entry = rawEntry.Trim();
                int colon = entry.LastIndexOf(':');
                if (colon <= 0 || colon >= entry.Length - 1)
                {
                    return null;
                }

                string item = entry.Substring(0, colon).Trim();
                string amountText = entry.Substring(colon + 1).Trim();
                int amount;
                if (item.Length == 0 || !int.TryParse(amountText, out amount) || amount < 0)
                {
                    return null;
                }

                // Amount 0 is a convenient way to remove one ingredient without rewriting the whole line.
                if (amount == 0)
                {
                    continue;
                }

                requirements.Add(new RequirementConfig
                {
                    Item = item,
                    Amount = amount,
                    Recover = true
                });
            }

            return requirements.ToArray();
        }

        private string StoveRecipeValue => _cfgStoveRecipe != null
            ? _cfgStoveRecipe.Value
            : DefaultStoveRecipe;

        private string WhisksRecipeValue => _cfgWhisksRecipe != null
            ? _cfgWhisksRecipe.Value
            : DefaultWhisksRecipe;

        private string BucketRecipeValue => _cfgBucketRecipe != null
            ? _cfgBucketRecipe.Value
            : DefaultBucketRecipe;

        /// <summary>
        /// Applies synchronized recipe changes to already registered prefabs as well.
        /// This matters on multiplayer clients because server config can arrive after local prefab creation.
        /// </summary>
        private void ApplyRecipeConfigToRegisteredPieces()
        {
            if (ObjectDB.instance == null)
            {
                return;
            }

            ApplyRecipeToPiece(_stovePrefab, StoveRecipeValue, DefaultStoveRecipe, "sauna_stove");
            ApplyRecipeToPiece(_bucketPrefab, BucketRecipeValue, DefaultBucketRecipe, "sauna_bucket");

            GameObject runtimeWhisks = ResolveSaunaWrisksRuntimePrefab();
            if (runtimeWhisks != null)
            {
                _wrisksPrefab = runtimeWhisks;
            }
            ApplyRecipeToPiece(_wrisksPrefab, WhisksRecipeValue, DefaultWhisksRecipe, "sauna_wrisks");
        }

        private static void ApplyRecipeToPiece(
            GameObject prefab,
            string recipe,
            string fallbackRecipe,
            string label)
        {
            if (prefab == null || ObjectDB.instance == null)
            {
                return;
            }

            Piece piece = prefab.GetComponent<Piece>();
            if (piece == null)
            {
                return;
            }

            RequirementConfig[] configs = ParseRecipeConfig(recipe, fallbackRecipe, label);
            List<Piece.Requirement> requirements = new List<Piece.Requirement>();

            foreach (RequirementConfig config in configs)
            {
                GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(config.Item);
                ItemDrop itemDrop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
                if (itemDrop == null)
                {
                    Jotunn.Logger.LogWarning(
                        $"{label} recipe item '{config.Item}' was not found; using default recipe");

                    if (!string.Equals(recipe, fallbackRecipe, StringComparison.Ordinal))
                    {
                        ApplyRecipeToPiece(prefab, fallbackRecipe, fallbackRecipe, label);
                    }
                    return;
                }

                requirements.Add(new Piece.Requirement
                {
                    m_resItem = itemDrop,
                    m_amount = config.Amount,
                    m_amountPerLevel = 0,
                    m_recover = config.Recover
                });
            }

            piece.m_resources = requirements.ToArray();
        }

        private void OnDestroy()
        {
            if (_saunaConfig != null)
            {
                _saunaConfig.SettingChanged -= OnSaunaConfigSettingChanged;
            }

            PieceManager.OnPiecesRegistered -= OnPiecesRegistered;
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
        }

        private static void OnPiecesRegistered()
        {
            EnsureSaunaWrisksRuntimeRegistration(null);
        }

        private IEnumerator RefreshWrisksAfterSpawn(Player expectedPlayer)
        {
            // Give the local player's Hammer/build UI one extra moment to finish its own
            // OnSpawned refresh. This is intentionally a one-shot safety pass, not a poll.
            yield return new WaitForSeconds(1f);

            if (expectedPlayer != null && expectedPlayer == Player.m_localPlayer)
            {
                EnsureSaunaWrisksRuntimeRegistration(expectedPlayer);
            }
        }

        private static void TryPatchBucketMeadHotbarUse(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(
                    typeof(Humanoid),
                    "UseItem",
                    new Type[]
                    {
                        typeof(Inventory),
                        typeof(ItemDrop.ItemData),
                        typeof(bool)
                    });

                var prefix = AccessTools.Method(
                    typeof(SaunaBucketUseMeadPatch),
                    nameof(SaunaBucketUseMeadPatch.Prefix));

                if (target == null || prefix == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna bucket: Humanoid.UseItem hook not available; " +
                        "bucket [E] interaction still works");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                Jotunn.Logger.LogInfo(
                    "sauna bucket: exact Humanoid.UseItem hotbar hook applied");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning(
                    "sauna bucket: optional hotbar hook failed; " +
                    "sauna pieces will still register. " + ex);
            }
        }

        private string FindAssetDir(string sub)
        {
            string local = Path.Combine(Path.GetDirectoryName(Info.Location), sub);

            if (Directory.Exists(local))
            {
                return local;
            }

            try
            {
                string[] hits = Directory.GetDirectories(
                    BepInEx.Paths.PluginPath, sub, SearchOption.AllDirectories);

                if (hits.Length > 0)
                {
                    Jotunn.Logger.LogWarning($"'{sub}' not next to dll, using {hits[0]}");
                    return hits[0];
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"asset search failed: {ex.Message}");
            }

            Jotunn.Logger.LogWarning($"folder '{sub}' not found, expected at {local}");
            return null;
        }

        private IEnumerator LoadPourSound()
        {
            string dir = FindAssetDir("sounds");

            if (string.IsNullOrEmpty(dir))
            {
                yield break;
            }

            string[] files = { "pour.wav", "pour.ogg", "pour.mp3" };

            foreach (string file in files)
            {
                string path = Path.Combine(dir, file);

                if (!File.Exists(path))
                {
                    continue;
                }

                AudioType type = AudioType.MPEG;
                if (file.EndsWith(".wav"))
                {
                    type = AudioType.WAV;
                }
                else if (file.EndsWith(".ogg"))
                {
                    type = AudioType.OGGVORBIS;
                }

                string url = new Uri(path).AbsoluteUri;

                using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, type))
                {
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Jotunn.Logger.LogWarning($"sound load failed {file}: {req.error}");
                        continue;
                    }

                    AudioClip clip = DownloadHandlerAudioClip.GetContent(req);

                    if (clip == null || clip.length <= 0f)
                    {
                        Jotunn.Logger.LogWarning($"sound decoded empty: {file}");
                        continue;
                    }

                    clip.name = "sauna_pour";
                    SaunaStove.PourClip = clip;
                    Jotunn.Logger.LogInfo($"pour sound loaded: {file}, {clip.length:0.00}s");
                    yield break;
                }
            }

            Jotunn.Logger.LogWarning($"no pour sound file in {dir}");
        }

        private void AddLocalization()
        {
            _loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "piece_sauna_stove", "Sauna stove" },
                { "piece_sauna_stove_desc", "A stone sauna stove. Pour on water and enjoy a proper steam." },
                { "piece_sauna_pour", "Pour water" },
                { "msg_sauna_pour", "Water hits the hot stones" },
                { "msg_sauna_notburning", "The stove is cold" },
                { "msg_sauna_wait", "The stones need to heat up again" },
                { "msg_sauna_tier", "The heat sinks deeper" },
                { "msg_sauna_max_tier", "Now that’s a proper steam!" },
                { "msg_sauna_bucket_use_mead", "Pour in mead" },
                { "msg_sauna_bucket_choose_mead", "More than one resistance mead is available. Aim at the bucket and use the one you want from the hotbar" },
                { "msg_sauna_bucket_no_mead", "You have no supported resistance mead" },
                { "msg_sauna_bucket_resistance_only", "Only resistance mead is suitable for the sauna" },
                { "msg_sauna_bucket_contains", "Infused with" },
                { "msg_sauna_bucket_full", "The sauna bucket already contains mead" },
                { "msg_sauna_bucket_filled", "Added {0} to the sauna bucket" },
                { "msg_sauna_mead_aroma", "The aroma of {0} fills the sauna" },
                { "se_sauna_steaming", "Steaming" },
                { "se_sauna_steaming_tooltip", "The steam slowly restores health and eases whatever ails you." },
                { "se_sauna_steaming_start", "You step into the steam" },
                { "se_sauna_wellsteamed", "Well steamed" },
                { "se_sauna_wellsteamed_tooltip",
                  "The heat stays with you.\nYou do not feel the cold." },
                { "se_sauna_wellsteamed_tooltip_whisks",
                  "The heat stays with you.\nYou do not feel the cold.\nSauna whisks make wetness harmless and unlock 15-minute warming." },
                { "se_sauna_wellsteamed_start", "You are well steamed" },
                { "piece_sauna_wrisks", "Sauna whisks" },
                { "piece_sauna_wrisks_desc", "A pair of birch sauna whisks. They help you steam properly and shrug off the discomfort of being wet." },
                { "piece_sauna_bucket", "Sauna bucket with ladle" },
                { "piece_sauna_bucket_desc", "Lets you throw more water on the stones and infuse the steam with mead, so you can enjoy its aroma and absorb its effects." }
            });

            _loc.AddTranslation("Russian", new Dictionary<string, string>
            {
                { "piece_sauna_stove", "Банная печь" },
                { "piece_sauna_stove_desc", "Каменная банная печь. Поддай пару и как следует пропарься." },
                { "piece_sauna_pour", "Поддать пару" },
                { "msg_sauna_pour", "Вода шипит на раскалённых камнях" },
                { "msg_sauna_notburning", "Печь не растоплена" },
                { "msg_sauna_wait", "Камни ещё не раскалились" },
                { "msg_sauna_tier", "Жар проникает глубже" },
                { "msg_sauna_max_tier", "С лёгким паром!" },
                { "msg_sauna_bucket_use_mead", "Подлить медовуху" },
                { "msg_sauna_bucket_choose_mead", "У тебя несколько защитных медовух. Наведи на ведро и используй нужную медовуху с панели быстрого доступа" },
                { "msg_sauna_bucket_no_mead", "У тебя нет подходящей защитной медовухи" },
                { "msg_sauna_bucket_resistance_only", "Для бани подходит только защитная медовуха" },
                { "msg_sauna_bucket_contains", "Добавлено" },
                { "msg_sauna_bucket_full", "В банном ведре уже есть медовуха" },
                { "msg_sauna_bucket_filled", "В банное ведро добавлено: {0}" },
                { "msg_sauna_mead_aroma", "Аромат {0} наполняет парную" },
                { "se_sauna_steaming", "Пропаривание" },
                { "se_sauna_steaming_tooltip", "Пар понемногу восстанавливает здоровье и выбивает из тебя всякую заразу." },
                { "se_sauna_steaming_start", "Ты зашёл в пар" },
                { "se_sauna_wellsteamed", "Пропарен" },
                { "se_sauna_wellsteamed_tooltip",
                  "Тепло держится в теле.\nХолод не берёт." },
                { "se_sauna_wellsteamed_tooltip_whisks",
                  "Тепло держится в теле.\nХолод не берёт.\nБанные веники снимают неудобства от сырости и открывают прогрев на 15 минут." },
                { "se_sauna_wellsteamed_start", "Хорошо пропарился" },
                { "piece_sauna_wrisks", "Банные веники" },
                { "piece_sauna_wrisks_desc", "Пара берёзовых веников. Помогают как следует пропариться и не страдать от сырости." },
                { "piece_sauna_bucket", "Ведро с ковшом" },
                { "piece_sauna_bucket_desc", "Позволяет поддать больше воды и подлить медовуху, чтобы насладиться ароматным паром и получить её защитный эффект." }
            });

            _loc.AddTranslation("German", new Dictionary<string, string>
            {
                { "piece_sauna_stove", "Saunaofen" },
                { "piece_sauna_stove_desc", "Ein steinerner Saunaofen. Mach einen Aufguss und schwitz dich ordentlich aus." },
                { "piece_sauna_pour", "Aufguss machen" },
                { "msg_sauna_pour", "Wasser zischt auf den heißen Steinen" },
                { "msg_sauna_notburning", "Der Ofen ist kalt" },
                { "msg_sauna_wait", "Die Steine müssen erst wieder heiß werden" },
                { "msg_sauna_tier", "Die Hitze dringt tiefer" },
                { "msg_sauna_max_tier", "Das war ein guter Aufguss!" },
                { "msg_sauna_bucket_use_mead", "Met dazugießen" },
                { "msg_sauna_bucket_choose_mead", "Du hast mehrere Widerstandsmete. Ziele auf den Eimer und benutze den gewünschten Met aus der Schnellleiste" },
                { "msg_sauna_bucket_no_mead", "Du hast keinen passenden Widerstandsmet" },
                { "msg_sauna_bucket_resistance_only", "Für die Sauna eignet sich nur Widerstandsmet" },
                { "msg_sauna_bucket_contains", "Enthält" },
                { "msg_sauna_bucket_full", "Im Saunaeimer ist bereits Met" },
                { "msg_sauna_bucket_filled", "{0} wurde in den Saunaeimer gegeben" },
                { "msg_sauna_mead_aroma", "Der Duft von {0} erfüllt die Sauna" },
                { "se_sauna_steaming", "Saunadampf" },
                { "se_sauna_steaming_tooltip", "Der Dampf stellt langsam Gesundheit wieder her und lindert, was dich plagt." },
                { "se_sauna_steaming_start", "Du trittst in den Dampf" },
                { "se_sauna_wellsteamed", "Gut durchgewärmt" },
                { "se_sauna_wellsteamed_tooltip",
                  "Die Wärme bleibt im Körper.\nKälte macht dir nichts aus." },
                { "se_sauna_wellsteamed_tooltip_whisks",
                  "Die Wärme bleibt im Körper.\nKälte macht dir nichts aus.\nBirkenquasten machen Nässe harmlos und schalten 15 Minuten Wärme frei." },
                { "se_sauna_wellsteamed_start", "Du bist gut durchgewärmt" },
                { "piece_sauna_wrisks", "Saunabirkenquaste" },
                { "piece_sauna_wrisks_desc", "Ein Paar Birkenquasten. Sie helfen dir, dich ordentlich durchzuwärmen, und Nässe macht dir nichts mehr aus." },
                { "piece_sauna_bucket", "Saunaeimer mit Kelle" },
                { "piece_sauna_bucket_desc", "Damit kannst du mehr Wasser aufgießen und Met hinzufügen, um den aromatischen Dampf zu genießen und seine Schutzwirkung aufzunehmen." }
            });

            _loc.AddTranslation("Spanish", new Dictionary<string, string>
            {
                { "piece_sauna_stove", "Estufa de sauna" },
                { "piece_sauna_stove_desc", "Una estufa de sauna de piedra. Echa agua sobre las piedras y date un buen baño de vapor." },
                { "piece_sauna_pour", "Echar agua" },
                { "msg_sauna_pour", "El agua sisea sobre las piedras calientes" },
                { "msg_sauna_notburning", "La estufa está fría" },
                { "msg_sauna_wait", "Las piedras tienen que volver a calentarse" },
                { "msg_sauna_tier", "El calor penetra más hondo" },
                { "msg_sauna_max_tier", "¡Eso sí que es un buen baño de vapor!" },
                { "msg_sauna_bucket_use_mead", "Añadir hidromiel" },
                { "msg_sauna_bucket_choose_mead", "Tienes varios hidromieles de resistencia. Apunta al cubo y usa el que quieras desde la barra rápida" },
                { "msg_sauna_bucket_no_mead", "No tienes un hidromiel de resistencia compatible" },
                { "msg_sauna_bucket_resistance_only", "Solo el hidromiel de resistencia sirve para la sauna" },
                { "msg_sauna_bucket_contains", "Contiene" },
                { "msg_sauna_bucket_full", "El cubo de sauna ya contiene hidromiel" },
                { "msg_sauna_bucket_filled", "Has añadido {0} al cubo de sauna" },
                { "msg_sauna_mead_aroma", "El aroma de {0} llena la sauna" },
                { "se_sauna_steaming", "Baño de vapor" },
                { "se_sauna_steaming_tooltip", "El vapor restaura poco a poco la salud y te hace sudar cualquier mal." },
                { "se_sauna_steaming_start", "Entras en el vapor" },
                { "se_sauna_wellsteamed", "Bien templado" },
                { "se_sauna_wellsteamed_tooltip",
                  "El calor permanece en tu cuerpo.\nEl frío no te afecta." },
                { "se_sauna_wellsteamed_tooltip_whisks",
                  "El calor permanece en tu cuerpo.\nEl frío no te afecta.\nLos ramos de sauna anulan las molestias de estar mojado y desbloquean 15 minutos de calor." },
                { "se_sauna_wellsteamed_start", "Has entrado bien en calor" },
                { "piece_sauna_wrisks", "Ramos de abedul para sauna" },
                { "piece_sauna_wrisks_desc", "Un par de ramos de abedul. Te ayudan a darte un buen baño de vapor y a no sufrir las molestias de estar mojado." },
                { "piece_sauna_bucket", "Cubo de sauna con cucharón" },
                { "piece_sauna_bucket_desc", "Permite echar más agua sobre las piedras y añadir hidromiel para disfrutar de su aroma en el vapor y absorber su efecto protector." }
            });

            _loc.AddTranslation("Finnish", new Dictionary<string, string>
            {
                { "piece_sauna_stove", "Kiuas" },
                { "piece_sauna_stove_desc", "Kivinen kiuas. Heitä löylyä ja nauti kunnon saunasta." },
                { "piece_sauna_pour", "Heitä löylyä" },
                { "msg_sauna_pour", "Vesi sihahtaa kuumille kiville" },
                { "msg_sauna_notburning", "Kiuas on kylmä" },
                { "msg_sauna_wait", "Kivet eivät ole vielä tarpeeksi kuumat" },
                { "msg_sauna_tier", "Lämpö painuu syvemmälle" },
                { "msg_sauna_max_tier", "Kunnon löylyt!" },
                { "msg_sauna_bucket_use_mead", "Kaada simaa kiuluun" },
                { "msg_sauna_bucket_choose_mead", "Sinulla on useita vastustuskykyä antavia simoja. Tähtää kiuluun ja käytä haluamaasi pikapalkista" },
                { "msg_sauna_bucket_no_mead", "Sinulla ei ole sopivaa vastustussimaa" },
                { "msg_sauna_bucket_resistance_only", "Vain vastustuskykyä antava sima sopii saunaan" },
                { "msg_sauna_bucket_contains", "Sisältää" },
                { "msg_sauna_bucket_full", "Saunakiulussa on jo simaa" },
                { "msg_sauna_bucket_filled", "{0} kaadettiin saunakiuluun" },
                { "msg_sauna_mead_aroma", "Höyryssä tuoksuu {0}" },
                { "se_sauna_steaming", "Löylyssä" },
                { "se_sauna_steaming_tooltip", "Löyly palauttaa hiljalleen terveyttä ja karkottaa kolotukset." },
                { "se_sauna_steaming_start", "Astut löylyyn" },
                { "se_sauna_wellsteamed", "Läpikotaisin lämmin" },
                { "se_sauna_wellsteamed_tooltip",
                  "Lämpö pysyy kehossa.\nKylmä ei tunnu missään." },
                { "se_sauna_wellsteamed_tooltip_whisks",
                  "Lämpö pysyy kehossa.\nKylmä ei tunnu missään.\nKoivuvihdat poistavat märkyyden haitat ja avaavat 15 minuutin lämmön." },
                { "se_sauna_wellsteamed_start", "Olet lämmin läpikotaisin" },
                { "piece_sauna_wrisks", "Koivuvihdat" },
                { "piece_sauna_wrisks_desc", "Pari koivuvihtaa. Niillä saat kunnon löylyt, eikä märkyys enää haittaa." },
                { "piece_sauna_bucket", "Saunakiulu ja kauha" },
                { "piece_sauna_bucket_desc", "Sillä saat heitettyä enemmän löylyä ja voit lisätä simaa, jolloin sen tuoksu ja suojaava vaikutus kulkevat höyryn mukana." }
            });
        }

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

        /// Redraws the stove icon. Called on world entry
        /// and from the editor when the view angle changes.
        public static void RefreshPieceIcon()
        {
            if (_stovePrefab == null || _instance == null)
            {
                return;
            }

            Piece piece = _stovePrefab.GetComponent<Piece>();
            if (piece == null)
            {
                return;
            }

            Sprite custom = _instance.LoadIcon(_instance.FindAssetDir("icons"), "stove.png");

            if (custom != null)
            {
                piece.m_icon = custom;
                Jotunn.Logger.LogInfo("piece icon: custom file");
                return;
            }

            GameObject temp = null;

            try
            {
                // Use a unique name so the renderer cannot return a cached previous view.
                temp = new GameObject("sauna_icon_src_" + (++_iconRenderCount));
                temp.SetActive(false);

                // Build exactly what is visible on the real stove: floor, dome, and stones.
                StoveVisual.Build(temp.transform);

                // The icon shows a lit stove, so hide the cooled coals.
                Transform coal = temp.transform.Find(StoveVisual.CoalName);
                if (coal != null)
                {
                    coal.gameObject.SetActive(false);
                }

                if (temp.GetComponentInChildren<MeshFilter>(true) == null)
                {
                    Jotunn.Logger.LogWarning("piece icon: no mesh");
                    return;
                }

                if (StoveVisual.IconFlame > 0)
                {
                    AddFlameToIcon(temp);
                }

                Sprite rendered = RenderManager.Instance.Render(
                    new RenderManager.RenderRequest(temp)
                    {
                        Rotation = UnityEngine.Quaternion.Euler(
                            StoveVisual.IconPitch, StoveVisual.IconYaw, 0f),
                        Width = 128,
                        Height = 128
                    });

                if (rendered != null)
                {
                    piece.m_icon = rendered;
                    Jotunn.Logger.LogInfo($"piece icon: rendered, yaw={StoveVisual.IconYaw:0}, " +
                        $"pitch={StoveVisual.IconPitch:0}");
                }
                else
                {
                    Jotunn.Logger.LogWarning("piece icon: render returned null");
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"piece icon failed: {ex.Message}");
            }
            finally
            {
                if (temp != null)
                {
                    UnityEngine.Object.Destroy(temp);
                }
            }
        }

        /// <summary>
        /// Selects the most complete sauna-whisk prefab instance. Jotunn Kitbash normally
        /// modifies the same object, but on first entry some versions or mod combinations
        /// may leave both a skeleton reference and a fully assembled prefab.
        /// Prefer the instance that actually contains Renderers.
        /// </summary>
        private static GameObject ResolveSaunaWrisksRuntimePrefab()
        {
            CustomPiece custom = PieceManager.Instance.GetPiece("sauna_wrisks");
            GameObject managerPrefab = custom != null ? custom.PiecePrefab : null;
            GameObject cachedPrefab = PrefabManager.Instance.GetPrefab("sauna_wrisks");

            GameObject best = null;
            int bestRendererCount = -1;

            GameObject[] candidates = { _wrisksPrefab, managerPrefab, cachedPrefab };
            foreach (GameObject candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }

                int renderers = candidate.GetComponentsInChildren<Renderer>(true).Length;
                if (best == null || renderers > bestRendererCount)
                {
                    best = candidate;
                    bestRendererCount = renderers;
                }
            }

            return best;
        }

        /// <summary>
        /// Verifies runtime registration of the whisks after Jotunn has created
        /// the real Hammer PieceTable. Normally this changes nothing.
        /// If first entry left an old or missing reference, replace it with the
        /// authoritative CustomPiece prefab, restore the ZNetScene hash, and
        /// refresh the local player's available build pieces without requiring a relog.
        /// </summary>
        private static void EnsureSaunaWrisksRuntimeRegistration(Player player)
        {
            try
            {
                GameObject prefab = ResolveSaunaWrisksRuntimePrefab();
                if (prefab == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_wrisks runtime verify: authoritative prefab not found");
                    return;
                }

                _wrisksPrefab = prefab;

                Piece piece = prefab.GetComponent<Piece>();
                if (piece == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_wrisks runtime verify: Piece component missing");
                    return;
                }

                // Never let a late icon-render attempt make the menu entry unusable.
                if (piece.m_icon == null)
                {
                    if (_wrisksFallbackIcon != null)
                    {
                        piece.m_icon = _wrisksFallbackIcon;
                    }
                    else
                    {
                        GameObject donor = PrefabManager.Instance.GetPrefab("piece_walltorch");
                        Piece donorPiece = donor != null ? donor.GetComponent<Piece>() : null;
                        if (donorPiece != null)
                        {
                            piece.m_icon = donorPiece.m_icon;
                            _wrisksFallbackIcon = donorPiece.m_icon;
                        }
                    }
                }

                bool repairedTable = false;
                PieceTable hammer = PieceManager.Instance.GetPieceTable(PieceTables.Hammer);
                if (hammer != null)
                {
                    int namedIndex = -1;
                    for (int i = hammer.m_pieces.Count - 1; i >= 0; --i)
                    {
                        GameObject current = hammer.m_pieces[i];
                        if (current == null)
                        {
                            hammer.m_pieces.RemoveAt(i);
                            repairedTable = true;
                            continue;
                        }

                        if (current.name == prefab.name)
                        {
                            if (namedIndex < 0)
                            {
                                namedIndex = i;
                            }
                            else
                            {
                                // Remove duplicate same-name entries, keeping one slot.
                                hammer.m_pieces.RemoveAt(i);
                                repairedTable = true;
                                if (i < namedIndex)
                                {
                                    namedIndex--;
                                }
                            }
                        }
                    }

                    if (namedIndex >= 0)
                    {
                        if (hammer.m_pieces[namedIndex] != prefab)
                        {
                            hammer.m_pieces[namedIndex] = prefab;
                            repairedTable = true;
                        }
                    }
                    else
                    {
                        hammer.m_pieces.Add(prefab);
                        repairedTable = true;
                    }
                }
                else
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_wrisks runtime verify: Hammer PieceTable not found");
                }

                bool repairedNetwork = false;
                if (ZNetScene.instance != null)
                {
                    int hash = prefab.name.GetStableHashCode();
                    GameObject registered;
                    if (!ZNetScene.instance.m_namedPrefabs.TryGetValue(hash, out registered))
                    {
                        PrefabManager.Instance.RegisterToZNetScene(prefab);
                        repairedNetwork = true;
                    }
                    else if (registered != prefab)
                    {
                        // A same-name pre-kitbash skeleton can survive the first registration
                        // pass. Spawning by hash must resolve to the final PieceManager prefab.
                        ZNetScene.instance.m_namedPrefabs[hash] = prefab;
                        repairedNetwork = true;
                    }
                }

                if (player != null)
                {
                    player.UpdateKnownRecipesList();
                    player.UpdateAvailablePiecesList();
                }

                int renderers = prefab.GetComponentsInChildren<Renderer>(true).Length;
                Jotunn.Logger.LogInfo(
                    $"sauna_wrisks runtime verify: tableRepair={repairedTable}, " +
                    $"networkRepair={repairedNetwork}, renderers={renderers}, " +
                    $"playerRefresh={(player != null)}");
            }
            catch (Exception ex)
            {
                // This is a self-heal path. It must never be allowed to break the rest of
                // SaunaMod if another mod has replaced the build table or network registry.
                Jotunn.Logger.LogWarning(
                    "sauna_wrisks runtime verify failed (non-fatal): " + ex);
            }
        }

        /// Redraws the whisks icon from the actual assembled model.
        public static void RefreshWrisksIcon()
        {
            GameObject resolved = ResolveSaunaWrisksRuntimePrefab();
            if (resolved != null)
            {
                _wrisksPrefab = resolved;
            }

            Piece wrisksPiece = _wrisksPrefab != null ? _wrisksPrefab.GetComponent<Piece>() : null;
            if (wrisksPiece != null && wrisksPiece.m_icon == null && _wrisksFallbackIcon != null)
            {
                wrisksPiece.m_icon = _wrisksFallbackIcon;
            }

            RefreshVisualPieceIcon(
                _wrisksPrefab,
                "sauna_wrisks_visual_root",
                SaunaPieceIconTuning.WrisksYaw,
                SaunaPieceIconTuning.WrisksPitch,
                SaunaPieceIconTuning.WrisksRoll,
                SaunaPieceIconTuning.WrisksOffsetX,
                SaunaPieceIconTuning.WrisksOffsetY,
                SaunaPieceIconTuning.WrisksDistance,
                SaunaPieceIconTuning.WrisksScale,
                "wrisks");
        }

        /// Redraws the bucket icon from the actual assembled model.
        public static void RefreshBucketIcon()
        {
            RefreshVisualPieceIcon(
                _bucketPrefab,
                "sauna_bucket_visual_root",
                SaunaPieceIconTuning.BucketYaw,
                SaunaPieceIconTuning.BucketPitch,
                SaunaPieceIconTuning.BucketRoll,
                SaunaPieceIconTuning.BucketOffsetX,
                SaunaPieceIconTuning.BucketOffsetY,
                SaunaPieceIconTuning.BucketDistance,
                SaunaPieceIconTuning.BucketScale,
                "bucket");
        }

        private static void RefreshVisualPieceIcon(
            GameObject piecePrefab,
            string visualRootName,
            float yaw,
            float pitch,
            float roll,
            float offsetX,
            float offsetY,
            float distance,
            float iconScale,
            string label)
        {
            if (piecePrefab == null)
            {
                Jotunn.Logger.LogWarning($"{label} icon: piece prefab is null");
                return;
            }

            Piece piece = piecePrefab.GetComponent<Piece>();
            Transform sourceRoot = piecePrefab.transform.Find(visualRootName);
            if (piece == null || sourceRoot == null)
            {
                Jotunn.Logger.LogWarning($"{label} icon: visual root '{visualRootName}' not found");
                return;
            }

            GameObject temp = null;

            try
            {
                temp = new GameObject($"sauna_{label}_icon_src_{++_iconRenderCount}");
                temp.SetActive(false);

                GameObject visual = UnityEngine.Object.Instantiate(sourceRoot.gameObject, temp.transform, false);
                visual.name = visualRootName;

                // IMPORTANT: Rotation used to be passed to RenderManager. In our kitbash models
                // the visible geometry is far from the root origin, so rotating it there
                // made it sweep a large arc and move out of frame.
                //
                // Now rotate the visual first, calculate the real Renderer.bounds for that view,
                // and move their center exactly to the temporary root origin. RenderManager
                // receives identity rotation, so every view rotates around the same center.
                visual.transform.localPosition = UnityEngine.Vector3.zero;
                visual.transform.localRotation = UnityEngine.Quaternion.Euler(pitch, yaw, roll);
                visual.transform.localScale *= Mathf.Max(0.05f, iconScale);

                if (!CenterIconVisualOnRendererBounds(visual.transform, temp.transform, out UnityEngine.Vector3 originalCenter))
                {
                    Jotunn.Logger.LogWarning($"{label} icon: no renderer in '{visualRootName}'");
                    return;
                }

                // These three parameters are applied AFTER centering and rotation.
                // They therefore do not change the pivot or cause another frame shift when angles change.
                visual.transform.localPosition += new UnityEngine.Vector3(offsetX, offsetY, distance);

                Sprite rendered = RenderManager.Instance.Render(
                    new RenderManager.RenderRequest(temp)
                    {
                        Rotation = UnityEngine.Quaternion.identity,
                        Width = 128,
                        Height = 128
                    });

                if (rendered != null)
                {
                    piece.m_icon = rendered;
                    Jotunn.Logger.LogInfo(
                        $"{label} icon: rendered, yaw={yaw:0.###}, pitch={pitch:0.###}, roll={roll:0.###}, " +
                        $"offset=({offsetX:0.###},{offsetY:0.###}), distance={distance:0.###}, scale={iconScale:0.###}, " +
                        $"centerWas={originalCenter}");
                }
                else
                {
                    Jotunn.Logger.LogWarning($"{label} icon: render returned null");
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"{label} icon failed: {ex.Message}");
            }
            finally
            {
                if (temp != null)
                {
                    UnityEngine.Object.Destroy(temp);
                }
            }
        }

        private static bool CenterIconVisualOnRendererBounds(
            Transform visualRoot,
            Transform frameRoot,
            out UnityEngine.Vector3 centerInFrame)
        {
            centerInFrame = UnityEngine.Vector3.zero;

            if (visualRoot == null || frameRoot == null)
            {
                return false;
            }

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds combined = new Bounds();

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                return false;
            }

            // Renderer.bounds are in world space. Convert the center to temp/frame coordinates
            // and move the root by the opposite vector. Parent translation is independent of
            // the visual's local Rotation/Scale, so the center becomes exactly (0,0,0).
            centerInFrame = frameRoot.InverseTransformPoint(combined.center);
            visualRoot.localPosition -= centerInFrame;
            return true;
        }

        /// Saves the current stove icon as a PNG next to the DLL.
        public static void DumpPieceIcon()
        {
            string file = $"stove_render_y{StoveVisual.IconYaw:0.###}_p{StoveVisual.IconPitch:0.###}.png";
            DumpIconForPrefab(_stovePrefab, file, "stove");
        }

        public static void DumpWrisksIcon()
        {
            string file = $"wrisks_render_y{SaunaPieceIconTuning.WrisksYaw:0.###}_" +
                          $"p{SaunaPieceIconTuning.WrisksPitch:0.###}_" +
                          $"r{SaunaPieceIconTuning.WrisksRoll:0.###}_" +
                          $"d{SaunaPieceIconTuning.WrisksDistance:0.###}_" +
                          $"s{SaunaPieceIconTuning.WrisksScale:0.###}.png";
            DumpIconForPrefab(_wrisksPrefab, file, "wrisks");
        }

        public static void DumpBucketIcon()
        {
            string file = $"bucket_render_y{SaunaPieceIconTuning.BucketYaw:0.###}_" +
                          $"p{SaunaPieceIconTuning.BucketPitch:0.###}_" +
                          $"r{SaunaPieceIconTuning.BucketRoll:0.###}_" +
                          $"d{SaunaPieceIconTuning.BucketDistance:0.###}_" +
                          $"s{SaunaPieceIconTuning.BucketScale:0.###}.png";
            DumpIconForPrefab(_bucketPrefab, file, "bucket");
        }

        private static void DumpIconForPrefab(GameObject prefab, string fileName, string label)
        {
            if (prefab == null || _instance == null)
            {
                Jotunn.Logger.LogWarning($"{label} icon dump: prefab/instance missing");
                return;
            }

            Piece piece = prefab.GetComponent<Piece>();
            if (piece == null || piece.m_icon == null || piece.m_icon.texture == null)
            {
                Jotunn.Logger.LogWarning($"{label} icon dump: nothing to save");
                return;
            }

            Texture2D copy = null;

            try
            {
                Texture2D src = piece.m_icon.texture;
                byte[] png = null;

                try
                {
                    png = src.EncodeToPNG();
                }
                catch (Exception)
                {
                    png = null;
                }

                if (png == null)
                {
                    RenderTexture rt = RenderTexture.GetTemporary(
                        src.width, src.height, 0, RenderTextureFormat.ARGB32);

                    RenderTexture previous = RenderTexture.active;
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;

                    copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                    copy.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
                    copy.Apply();

                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);

                    png = copy.EncodeToPNG();
                }

                string dir = Path.GetDirectoryName(_instance.Info.Location);
                string path = Path.Combine(dir, fileName);

                File.WriteAllBytes(path, png);
                Jotunn.Logger.LogInfo($"{label} icon dump saved: {path} ({src.width}x{src.height})");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"{label} icon dump failed: {ex.Message}");
            }
            finally
            {
                if (copy != null)
                {
                    UnityEngine.Object.Destroy(copy);
                }
            }
        }

        /// Copies the flame from the stove prefab for icon rendering and removes nonvisual parts:
        /// lights, effect areas, and audio are unnecessary for rendering and may spam the log.
        private static void AddFlameToIcon(GameObject temp)
        {
            Fireplace fireplace = _stovePrefab.GetComponent<Fireplace>();

            if (fireplace == null)
            {
                return;
            }

            GameObject source = fireplace.m_enabledObjectHigh != null
                ? fireplace.m_enabledObjectHigh
                : fireplace.m_enabledObject;

            if (source == null)
            {
                return;
            }

            GameObject flame = UnityEngine.Object.Instantiate(source, temp.transform);
            flame.name = "icon_flame";
            flame.transform.localPosition = source.transform.localPosition;
            flame.transform.localRotation = source.transform.localRotation;
            flame.transform.localScale = source.transform.localScale;
            flame.SetActive(true);

            foreach (Light light in flame.GetComponentsInChildren<Light>(true))
            {
                UnityEngine.Object.DestroyImmediate(light);
            }

            foreach (EffectArea area in flame.GetComponentsInChildren<EffectArea>(true))
            {
                UnityEngine.Object.DestroyImmediate(area);
            }

            foreach (AudioSource audio in flame.GetComponentsInChildren<AudioSource>(true))
            {
                UnityEngine.Object.DestroyImmediate(audio);
            }
        }

        private void EnsureIcons()
        {
            string dir = FindAssetDir("icons");

            _steaming.m_icon = LoadIcon(dir, "steaming.png") ?? FindIcon("Resting", "Rested");
            _wellSteamed.m_icon = LoadIcon(dir, "wellsteamed.png") ?? FindIcon("Rested", "Resting");

            Jotunn.Logger.LogInfo($"icons: steaming={_steaming.m_icon != null}, " +
                $"wellSteamed={_wellSteamed.m_icon != null}");
        }

        private Sprite LoadIcon(string dir, string fileName)
        {
            if (string.IsNullOrEmpty(dir))
            {
                return null;
            }

            string path = Path.Combine(dir, fileName);

            if (!File.Exists(path))
            {
                Jotunn.Logger.LogInfo($"icon not found: {path}");
                return null;
            }

            try
            {
                Sprite sprite = AssetUtils.LoadSpriteFromFile(path);
                Jotunn.Logger.LogInfo(sprite != null
                    ? $"icon loaded: {fileName}"
                    : $"icon failed to parse: {fileName}");
                return sprite;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"icon load error {fileName}: {ex.Message}");
                return null;
            }
        }

        private Sprite FindIcon(params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                StatusEffect se = FindEffect(candidate);
                if (se != null && se.m_icon != null)
                {
                    return se.m_icon;
                }
            }
            return null;
        }

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

            if (inSteam)
            {
                if (!seman.HaveStatusEffect(_steaming.NameHash()))
                {
                    seman.AddStatusEffect(_steaming, false, 0, 0f, -1);
                }

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
            else if (_gapTime + elapsed < SteamTuning.Grace &&
                     seman.HaveStatusEffect(_steaming.NameHash()))
            {
                // Steam contact was briefly lost, for example because a cloud drifted away.
                // Keep the effect and progress unchanged and only remember the gap duration.
                _gapTime += elapsed;
            }
            else
            {
                seman.RemoveStatusEffect(_steaming.NameHash(), true);
                _steamTime = 0f;
                _gapTime = 0f;
                _timeTier = 0;   // The player stayed out of steam too long, so restart tier progression.
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

            // Without whisks the player can earn only the 5- and 10-minute tiers.
            // Whisks (SaunaTier 2+) unlock the third 15-minute tier.
            // Never shorten an already earned active 15-minute effect.
            int maxEarnableTimeTier = currentSaunaTier >= 2
                ? WellSteamedTimeTiers.Length
                : Mathf.Min(2, WellSteamedTimeTiers.Length);

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

                Sprite icon = LoadIcon(FindAssetDir("icons"), "whisks.png");
                if (icon == null)
                {
                    icon = FindFirstPieceIcon(
                        "piece_walltorch", "piece_chair", "piece_stool", "piece_table_round");
                }

                // Never let an optional icon file decide whether the build piece exists.
                // Keep a stable fallback until the real model-rendered icon is generated in-world.
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

                Sprite icon = LoadIcon(FindAssetDir("icons"), "bucket.png");
                if (icon == null)
                {
                    icon = FindFirstPieceIcon(
                        "piece_chest_wood",
                        "piece_table_round",
                        "piece_stool",
                        "piece_cauldron");
                }

                bucket.Piece.m_icon = icon;
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
