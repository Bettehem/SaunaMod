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
    internal class SaunaStove : MonoBehaviour
    {
        public const string RpcName = "SaunaMod_Pour";
        public const string ZdoLastPour = "sauna_lastpour";
        public const string ZdoHeat = "sauna_heat";
        public const string ZdoHeatTime = "sauna_heat_time";

        public static GameObject SteamPrefab;
        public static AudioClip PourClip;

        public const float BurstDuration = 3f;
        public const float BurstRadius = 0.7f;
        public const float BurstHeight = 0.5f;
        public const float PourVolume = 1.0f;

        private static readonly List<SaunaStove> s_all = new List<SaunaStove>();

        private Fireplace m_fireplace;
        private ZNetView m_nview;
        private AudioSource m_audio;

        private Transform m_hotStones;
        private Transform m_coldStones;
        private bool m_wasHot;
        private float m_heatTimer;

        // The first update waits one second so that Fireplace has already caught up
        // the fuel it burned while the area was unloaded.
        private float m_stoneHeatTimer = 1f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private MeshRenderer m_domeRenderer;
        private MeshRenderer m_floorRenderer;
        private MaterialPropertyBlock m_stoneBlock;
        private Light m_glowLight;

        // Heat shown on the stones. It eases toward the real heat, so a pour fades
        // the red over a moment instead of snapping. Negative = not shown yet.
        private float m_displayHeat = -1f;
        private float m_shownHeat = -1f;
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

            StoveTuning.ApplyFuel(m_fireplace);

            CacheFireRoots();
            SetupAudio();
            SetupGlowLight();
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

            m_domeRenderer = FindRenderer(StoveVisual.DomeName);
            m_floorRenderer = FindRenderer(StoveVisual.FloorName);
            m_shownHeat = -1f;
        }

        private MeshRenderer FindRenderer(string name)
        {
            Transform t = transform.Find(name);
            return t != null ? t.GetComponent<MeshRenderer>() : null;
        }

        /// Editor: re-tint every stove after a redness or glow setting changed.
        public static void RefreshStoneHeatAll()
        {
            foreach (SaunaStove stove in s_all)
            {
                if (stove != null)
                {
                    stove.m_shownHeat = -1f;
                }
            }
        }

        private void SetupGlowLight()
        {
            GameObject go = new GameObject("sauna_stone_glow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = UnityEngine.Vector3.up * 0.6f;

            m_glowLight = go.AddComponent<Light>();
            m_glowLight.type = LightType.Point;
            m_glowLight.color = StoneRednessTuning.LightColor;
            m_glowLight.shadows = LightShadows.None;
            m_glowLight.intensity = 0f;
            m_glowLight.enabled = false;
        }

        private void UpdateStoneVisual()
        {
            float target = SaunaEditor.Active && StoneRednessTuning.Preview > 0
                ? StoveTuning.MaxHeat
                : GetHeat();

            m_displayHeat = m_displayHeat < 0f
                ? target
                : Mathf.MoveTowards(m_displayHeat, target, 40f * Time.deltaTime);

            if (m_shownHeat >= 0f && Mathf.Abs(m_displayHeat - m_shownHeat) < 0.2f)
            {
                return;
            }

            m_shownHeat = m_displayHeat;
            float heat01 = Mathf.Clamp01(m_displayHeat / StoveTuning.MaxHeat);

            TintStones(m_domeRenderer, StoveVisual.DomeBandWeights, heat01, 1f);
            TintStones(m_floorRenderer, StoveVisual.FloorBandWeights, heat01, StoneRednessTuning.FloorGlowBoost);

            if (m_glowLight != null)
            {
                float intensity = StoneRednessTuning.LightIntensity * heat01;
                m_glowLight.intensity = intensity;
                m_glowLight.range = StoneRednessTuning.LightRange;
                m_glowLight.enabled = intensity > 0.01f;
            }
        }

        /// Each material slot of the dome/floor is one heat band; its weight scales the redness.
        private void TintStones(MeshRenderer renderer, float[] weights, float heat01, float glowScale)
        {
            if (renderer == null)
            {
                return;
            }

            if (m_stoneBlock == null)
            {
                m_stoneBlock = new MaterialPropertyBlock();
            }

            Material[] materials = renderer.sharedMaterials;
            int count = Mathf.Min(materials.Length, weights.Length);

            for (int i = 0; i < count; i++)
            {
                Material material = materials[i];
                if (material == null)
                {
                    continue;
                }

                float redness = weights[i] * heat01;
                m_stoneBlock.Clear();

                if (material.HasProperty(ColorId))
                {
                    m_stoneBlock.SetColor(ColorId,
                        StoneRednessTuning.Apply(material.GetColor(ColorId), redness));
                }

                if (material.HasProperty(EmissionId))
                {
                    m_stoneBlock.SetColor(EmissionId, StoneRednessTuning.Emission(redness) * glowScale);
                }

                renderer.SetPropertyBlock(m_stoneBlock, i);
            }
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
        /// Whisks and bucket each contribute +1, but only while the player is near a stove
        /// whose stones are at least StoveTuning.ComfortMinHeat hot.
        /// Accessories must also be genuinely placed and linked to that stove by the normal 5 m sauna link rule.
        public static int GetSaunaComfortBonusNear(UnityEngine.Vector3 position)
        {
            const float sourceRange = 8f;
            SaunaStove activeStove = null;
            float best = sourceRange * sourceRange;

            foreach (SaunaStove stove in s_all)
            {
                if (stove == null || stove.GetHeat() < StoveTuning.ComfortMinHeat)
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

        /// Editor tool: set the stone heat of the nearest sauna stove.
        public static void SetHeatNearest(UnityEngine.Vector3 position, float heat)
        {
            SaunaStove nearest = null;
            float best = 20f * 20f;

            foreach (SaunaStove stove in s_all)
            {
                if (stove == null || stove.m_nview == null || !stove.m_nview.IsValid())
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
                Jotunn.Logger.LogInfo("set heat: no sauna stove within 20 m");
                return;
            }

            if (!nearest.m_nview.IsOwner())
            {
                nearest.m_nview.ClaimOwnership();
            }

            // Restart the heat clock so the next update does not apply time from before the override.
            ZDO zdo = nearest.m_nview.GetZDO();
            zdo.Set(ZdoHeatTime, HeatClockTicks());
            zdo.Set(ZdoHeat, Mathf.Clamp(heat, 0f, StoveTuning.MaxHeat));
            Jotunn.Logger.LogInfo($"set heat: stove heat set to {heat:0}");
        }

        /// Editor: push the current fuel rules to every placed stove.
        public static void ApplyFuelToAll()
        {
            foreach (SaunaStove stove in s_all)
            {
                if (stove != null)
                {
                    StoveTuning.ApplyFuel(stove.m_fireplace);
                }
            }
        }

        /// Stone heat from 0 to StoveTuning.MaxHeat, as last written by the owner.
        public float GetHeat()
        {
            if (m_nview == null || !m_nview.IsValid())
            {
                return 0f;
            }

            return m_nview.GetZDO().GetFloat(ZdoHeat, 0f);
        }

        private static long HeatClockTicks()
        {
            return ZNet.instance != null ? ZNet.instance.GetTime().Ticks : DateTime.Now.Ticks;
        }

        /// Owner only: advance the stone heat by the time passed since the last update.
        /// Elapsed time is measured on the network clock and stored in the ZDO, so the heat
        /// also catches up after the area was unloaded or ownership changed hands.
        private void UpdateStoneHeat()
        {
            if (m_fireplace == null || m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner())
            {
                return;
            }

            ZDO zdo = m_nview.GetZDO();
            long now = HeatClockTicks();
            long last = zdo.GetLong(ZdoHeatTime, 0L);
            zdo.Set(ZdoHeatTime, now);

            if (last <= 0L || now <= last)
            {
                return;
            }

            float minutes = (float)((now - last) / (double)TimeSpan.TicksPerMinute);
            float rate = m_fireplace.IsBurning()
                ? StoveTuning.HeatPerMinute
                : -StoveTuning.CoolPerMinute;

            float heat = zdo.GetFloat(ZdoHeat, 0f);
            zdo.Set(ZdoHeat, Mathf.Clamp(heat + rate * minutes, 0f, StoveTuning.MaxHeat));
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
            // Placement ghosts have no ZDO, so they have neither heat nor a pour cooldown.
            if (m_fireplace == null || m_nview == null || !m_nview.IsValid())
            {
                return false;
            }

            // Steam comes from the heat stored in the stones, not from the fire itself:
            // a stove that has just gone out can still be poured on while the stones are hot.
            if (GetHeat() < StoveTuning.MinPourHeat)
            {
                if (user != null)
                {
                    user.Message(MessageHud.MessageType.Center, "$msg_sauna_cold_stones");
                }
                return false;
            }

            double now = NetTime();
            float last = m_nview.GetZDO().GetFloat(ZdoLastPour, -9999f);

            if (now - last < StoveTuning.PourCooldown)
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

            // Bring the heat up to date as the new owner before spending it.
            UpdateStoneHeat();

            ZDO zdo = m_nview.GetZDO();
            float heat = zdo.GetFloat(ZdoHeat, 0f);
            zdo.Set(ZdoHeat, Mathf.Max(0f, heat - StoveTuning.PourHeatCost));
            zdo.Set(ZdoLastPour, (float)now);

            // The bucket increases the amount of steam per pour, not cloud speed or TTL.
            // Cooler stones give less steam, measured by the heat before this pour.
            int saunaTier = GetWellSteamedSaunaTier();
            int cloudCount = saunaTier >= 3
                ? SteamTuning.CloudsPerPourWithBucket
                : SteamTuning.CloudsPerPour;
            cloudCount = Mathf.Max(1, Mathf.RoundToInt(cloudCount * StoveTuning.SteamFactor(heat)));

            m_nview.InvokeRPC(ZNetView.Everybody, RpcName, cloudCount);

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

            m_stoneHeatTimer -= Time.deltaTime;
            if (m_stoneHeatTimer <= 0f)
            {
                m_stoneHeatTimer = 1f;
                UpdateStoneHeat();
            }

            UpdateStoneVisual();

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
}
