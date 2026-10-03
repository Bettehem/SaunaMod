// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaunaMod
{
    internal class SaunaStove : MonoBehaviour
    {
        public const string RpcName = "SaunaMod_Pour";
        public const string ZdoLastPour = "sauna_lastpour";
        public const string ZdoHeat = "sauna_heat";
        public const string ZdoHeatTime = "sauna_heat_time";
        public const string ZdoFireOutTime = "sauna_fire_out_time";

        public static GameObject SteamPrefab;
        public static AudioClip PourClip;

        public const float BurstDuration = 3f;
        public const float BurstRadius = 0.7f;
        public const float BurstHeight = 0.5f;
        public const float PourVolume = 1.0f;

        /// Steam spreads, so the stove counts for players this far away.
        private const float SaunaRange = 8f;

        /// Editor tools act on the nearest stove within this distance.
        private const float EditorToolRange = 20f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private static readonly List<SaunaStove> s_all = new List<SaunaStove>();

        private Fireplace m_fireplace;
        private ZNetView m_nview;
        private AudioSource m_audio;

        // ---- visuals ----
        private MeshRenderer m_domeRenderer;
        private MeshRenderer m_floorRenderer;
        private MeshRenderer m_coalRenderer;
        private readonly List<MeshRenderer> m_campfireLogs = new List<MeshRenderer>();
        private MaterialPropertyBlock m_stoneBlock;
        private MaterialPropertyBlock m_logBlock;
        private MaterialPropertyBlock m_coalBlock;
        private Light m_glowLight;

        private bool m_burning;
        private float m_burningCheckTimer;

        // Heat shown on the stones (0..MaxHeat). It eases toward the real heat, so a pour fades
        // the red over a moment instead of snapping. Negative = not shown yet.
        private float m_displayHeat = -1f;
        private float m_shownHeat = -1f;
        private float m_shownCoalGlow = -1f;

        // The catch-up after the area was unloaded happens in SaunaStoveFuelPatch, right before
        // Fireplace burns off its fuel; this timer only keeps the heat current in between.
        private float m_heatUpdateTimer = 1f;

        // ---- steam burst ----
        private float m_burstTimeLeft;
        private float m_cloudsToSpawn;
        private int m_burstCloudCount = 20;

        // ---- original fire transforms, so repeated rebuilds do not accumulate offsets ----
        private readonly List<Transform> m_fireRoots = new List<Transform>();
        private readonly List<Vector3> m_fireRootPositions = new List<Vector3>();
        private readonly List<Vector3> m_fireRootScales = new List<Vector3>();
        private readonly List<ParticleSystem> m_flames = new List<ParticleSystem>();
        private readonly List<Vector3> m_flamePositions = new List<Vector3>();
        private readonly List<Vector3> m_flameScales = new List<Vector3>();
        private readonly List<float> m_flameStartSizes = new List<float>();
        private readonly List<SphereCollider> m_warmthColliders = new List<SphereCollider>();
        private readonly List<float> m_warmthRadii = new List<float>();

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

        private void Update()
        {
            // There is no need to check burning every frame; twice per second is more than enough.
            m_burningCheckTimer -= Time.deltaTime;
            if (m_burningCheckTimer <= 0f)
            {
                m_burningCheckTimer = 0.5f;
                UpdateCampfire(false);
            }

            m_heatUpdateTimer -= Time.deltaTime;
            if (m_heatUpdateTimer <= 0f)
            {
                m_heatUpdateTimer = 1f;
                UpdateStoneHeat();
            }

            UpdateStoneVisual();
            UpdateSteamBurst();
        }

        // =====================================================================
        // Stove lookup
        // =====================================================================

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

        /// Nearest stove within maxDistance that matches the filter, or null.
        private static SaunaStove FindNearest(Vector3 position, float maxDistance, Func<SaunaStove, bool> filter)
        {
            SaunaStove nearest = null;
            float nearestStoveDistanceSqr = maxDistance * maxDistance;

            foreach (SaunaStove stove in s_all)
            {
                if (stove == null || !filter(stove))
                {
                    continue;
                }

                float distanceSqr = (stove.transform.position - position).sqrMagnitude;
                if (distanceSqr < nearestStoveDistanceSqr)
                {
                    nearestStoveDistanceSqr = distanceSqr;
                    nearest = stove;
                }
            }

            return nearest;
        }

        /// Placement ghosts have no valid ZDO and never count as a real stove.
        private bool IsPlaced()
        {
            return m_nview != null && m_nview.IsValid();
        }

        /// Nearest actually placed sauna stove, used for linking accessories.
        public static SaunaStove FindClosestForVisualLink(Vector3 position, float maxDistance)
        {
            return FindNearest(position, maxDistance, stove => stove.IsPlaced());
        }

        /// Anchor point for the golden connection line. This is visual only;
        /// the stove does not need a CraftingStation or StationExtension for it.
        public Vector3 VisualLinkPoint()
        {
            return transform.position + Vector3.up * 0.45f;
        }

        // =====================================================================
        // Sauna accessories
        // =====================================================================

        /// Whisks and bucket linked to THIS stove. Uses the same rule as the golden line:
        /// the accessory must be within MaxLinkDistance and this stove must be its nearest stove.
        private void FindLinkedAccessories(out bool hasWhisks, out bool hasBucket)
        {
            hasWhisks = false;
            hasBucket = false;

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
                ZNetView pieceView = piece.GetComponent<ZNetView>();
                if (pieceView == null || !pieceView.IsValid())
                {
                    continue;
                }

                if (FindClosestForVisualLink(piece.transform.position, SaunaVisualLink.MaxLinkDistance) != this)
                {
                    continue;
                }

                hasWhisks |= isWhisks;
                hasBucket |= isBucket;

                if (hasWhisks && hasBucket)
                {
                    return;
                }
            }
        }

        /// Sauna tier around THIS stove. The bucket alone does not raise the tier;
        /// progression is stove -> whisks -> bucket.
        public int GetWellSteamedSaunaTier()
        {
            bool hasWhisks;
            bool hasBucket;
            FindLinkedAccessories(out hasWhisks, out hasBucket);

            if (hasWhisks && hasBucket)
            {
                return 3;
            }

            return hasWhisks ? 2 : 1;
        }

        /// Use the nearest stove to the player when granting Well Steamed.
        /// Steam physically originates at the stove, so SaunaRange leaves room for cloud movement,
        /// while accessories are still counted only within their own link radius.
        public static int GetWellSteamedSaunaTierNear(Vector3 position)
        {
            SaunaStove stove = FindClosestForVisualLink(position, SaunaRange);
            return stove != null ? stove.GetWellSteamedSaunaTier() : 1;
        }

        /// Comfort bonus from sauna accessories around the nearest stove with hot stones
        /// (at least StoveTuning.ComfortMinHeat). Whisks and bucket each contribute +1.
        public static int GetSaunaComfortBonusNear(Vector3 position)
        {
            SaunaStove hotStove = FindNearest(position, SaunaRange,
                stove => stove.GetHeat() >= StoveTuning.ComfortMinHeat);

            if (hotStove == null)
            {
                return 0;
            }

            bool hasWhisks;
            bool hasBucket;
            hotStove.FindLinkedAccessories(out hasWhisks, out hasBucket);

            return (hasWhisks ? 1 : 0) + (hasBucket ? 1 : 0);
        }

        // =====================================================================
        // Editor tools
        // =====================================================================

        /// Instantly consume all fuel in the nearest sauna stove.
        /// SetFuel forwards to the owner through RPC, so it also works in multiplayer.
        public static void ExtinguishNearest(Vector3 position)
        {
            SaunaStove nearest = FindNearest(position, EditorToolRange, stove => stove.m_fireplace != null);
            if (nearest == null)
            {
                Jotunn.Logger.LogInfo($"extinguish: no sauna stove within {EditorToolRange:0} m");
                return;
            }

            nearest.m_fireplace.SetFuel(0f);
            Jotunn.Logger.LogInfo("extinguish: stove fuel set to 0");
        }

        /// Set the stone heat of the nearest sauna stove.
        public static void SetHeatNearest(Vector3 position, float heat)
        {
            SaunaStove nearest = FindNearest(position, EditorToolRange, stove => stove.IsPlaced());
            if (nearest == null)
            {
                Jotunn.Logger.LogInfo($"set heat: no sauna stove within {EditorToolRange:0} m");
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

        /// Push the current fuel rules to every placed stove.
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

        /// Re-tint every stove after a redness or glow setting changed.
        public static void RefreshStoneHeatAll()
        {
            foreach (SaunaStove stove in s_all)
            {
                if (stove != null)
                {
                    stove.m_shownHeat = -1f;
                    stove.m_shownCoalGlow = -1f;
                }
            }
        }

        // =====================================================================
        // Stone heat
        // =====================================================================

        /// Stone heat from 0 to StoveTuning.MaxHeat, as last written by the owner.
        public float GetHeat()
        {
            return IsPlaced() ? m_nview.GetZDO().GetFloat(ZdoHeat, 0f) : 0f;
        }

        private bool IsBurning()
        {
            // IsBurning reads ZDO without a null check, so placement ghosts would throw.
            return m_fireplace != null && IsPlaced() && m_fireplace.IsBurning();
        }

        private static long HeatClockTicks()
        {
            return ZNet.instance != null ? ZNet.instance.GetTime().Ticks : DateTime.Now.Ticks;
        }

        /// Network-clock moment the stove runs (or ran) out of wood: long.MaxValue while it
        /// cannot run out, long.MinValue while it cannot burn at all.
        ///
        /// Fireplace stores its fuel as of its own last update (ZDOVars.s_lastTime) and burns
        /// it off in one step when the owner next updates it. As long as that has not happened,
        /// lastTime + fuel * secPerFuel is the exact moment the wood runs out, also when it ran
        /// out while the area was unloaded.
        private long FuelOutTicks(ZDO zdo, long nowTicks)
        {
            // The same conditions as Fireplace.IsBurning apart from the fuel.
            // m_blocked stays false because the stove disables the cover check.
            if (m_fireplace.m_blocked || zdo.GetInt(ZDOVars.s_state, 1) != 1)
            {
                return long.MinValue;
            }

            if (m_fireplace.m_checkWaterLevel)
            {
                Vector3 firePosition = m_fireplace.m_enabledObject != null
                    ? m_fireplace.m_enabledObject.transform.position
                    : transform.position;
                if (Floating.IsUnderWater(firePosition, ref m_fireplace.m_previousWaterVolume))
                {
                    return long.MinValue;
                }
            }

            if (m_fireplace.m_infiniteFuel)
            {
                return long.MaxValue;
            }

            float fuel = zdo.GetFloat(ZDOVars.s_fuel, 0f);
            if (fuel <= 0f)
            {
                return long.MinValue;
            }

            if (m_fireplace.m_secPerFuel <= 0f)
            {
                return long.MaxValue;
            }

            // Without lastTime the Fireplace has not burned anything yet; it starts counting now.
            long fuelTimeTicks = zdo.GetLong(ZDOVars.s_lastTime, nowTicks);
            double burnTicks = fuel * (double)m_fireplace.m_secPerFuel * TimeSpan.TicksPerSecond;
            return fuelTimeTicks + (long)Math.Min(burnTicks, long.MaxValue / 2);
        }

        /// Owner only: advance the stone heat by the time passed since the last update.
        /// Elapsed time is measured on the network clock and stored in the ZDO, so the heat
        /// also catches up after the area was unloaded or ownership changed hands.
        ///
        /// The interval is split at the moment the wood ran out: the stones heat until then,
        /// hold their heat for CoolingDelaySeconds and cool for the rest. SaunaStoveFuelPatch
        /// runs this right before Fireplace burns off its fuel, so the fuel read here is never
        /// already spent for the time being calculated.
        internal void UpdateStoneHeat()
        {
            if (m_fireplace == null || !IsPlaced() || !m_nview.IsOwner())
            {
                return;
            }

            ZDO zdo = m_nview.GetZDO();
            long nowTicks = HeatClockTicks();
            long lastTicks = zdo.GetLong(ZdoHeatTime, 0L);
            zdo.Set(ZdoHeatTime, nowTicks);

            if (lastTicks <= 0L || nowTicks <= lastTicks)
            {
                return;
            }

            float heat = zdo.GetFloat(ZdoHeat, 0f);

            // End of burning within this interval: lastTicks = not burning, nowTicks = burned throughout.
            long burnEndTicks = Math.Max(lastTicks, Math.Min(nowTicks, FuelOutTicks(zdo, nowTicks)));

            if (burnEndTicks > lastTicks)
            {
                heat += StoveTuning.HeatPerMinute * TicksToMinutes(burnEndTicks - lastTicks);
                heat = Mathf.Min(heat, StoveTuning.MaxHeat);
            }

            if (burnEndTicks >= nowTicks)
            {
                zdo.Set(ZdoFireOutTime, 0L);
            }
            else
            {
                // Remember when the fire went out; the stones hold their heat for CoolingDelaySeconds
                // after that and only cool for the part of this interval that comes later.
                long fireOutTicks = zdo.GetLong(ZdoFireOutTime, 0L);
                if (burnEndTicks > lastTicks || fireOutTicks <= 0L)
                {
                    fireOutTicks = burnEndTicks;
                    zdo.Set(ZdoFireOutTime, fireOutTicks);
                }

                long delayTicks = (long)(Mathf.Max(0f, StoveTuning.CoolingDelaySeconds) * TimeSpan.TicksPerSecond);
                long coolingStartTicks = Math.Max(lastTicks, fireOutTicks + delayTicks);

                if (nowTicks > coolingStartTicks)
                {
                    heat -= StoveTuning.CoolPerMinute * TicksToMinutes(nowTicks - coolingStartTicks);
                }
            }

            zdo.Set(ZdoHeat, Mathf.Clamp(heat, 0f, StoveTuning.MaxHeat));
        }

        private static float TicksToMinutes(long ticks)
        {
            return (float)(ticks / (double)TimeSpan.TicksPerMinute);
        }

        // =====================================================================
        // Visuals
        // =====================================================================

        public void RefreshVisual()
        {
            StoveVisual.Build(transform);
            ApplyFire();

            // Rebuilt children are new objects, so resolve them again and immediately apply the current state.
            m_domeRenderer = FindRenderer(StoveVisual.DomeName);
            m_floorRenderer = FindRenderer(StoveVisual.FloorName);
            m_coalRenderer = FindRenderer(StoveVisual.CoalName);

            m_campfireLogs.Clear();
            Transform campfire = transform.Find(StoveVisual.CampfireName);
            if (campfire != null)
            {
                m_campfireLogs.AddRange(campfire.GetComponentsInChildren<MeshRenderer>(true));
            }

            UpdateCampfire(true);
            m_shownHeat = -1f;
            m_shownCoalGlow = -1f;
        }

        private MeshRenderer FindRenderer(string childName)
        {
            Transform child = transform.Find(childName);
            return child != null ? child.GetComponent<MeshRenderer>() : null;
        }

        private void SetupGlowLight()
        {
            GameObject lightObject = new GameObject("sauna_stone_glow");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = Vector3.up * 0.6f;

            m_glowLight = lightObject.AddComponent<Light>();
            m_glowLight.type = LightType.Point;
            m_glowLight.color = StoneRednessTuning.LightColor;
            m_glowLight.shadows = LightShadows.None;
            m_glowLight.intensity = 0f;
            m_glowLight.enabled = false;
        }

        /// Redness and glow of the stones, coal glow and the stone light, all following the stone heat.
        private void UpdateStoneVisual()
        {
            float targetHeat = SaunaEditor.Active && StoneRednessTuning.Preview > 0
                ? StoveTuning.MaxHeat
                : GetHeat();

            m_displayHeat = m_displayHeat < 0f
                ? targetHeat
                : Mathf.MoveTowards(m_displayHeat, targetHeat, 40f * Time.deltaTime);

            // 0 = cold stones, 1 = full glow. The glow is full at FullGlowHeat and does not grow
            // beyond it when MaxHeat is set higher; with a lower MaxHeat it is full at MaxHeat.
            float fullGlowHeat = Mathf.Min(StoneRednessTuning.FullGlowHeat, StoveTuning.MaxHeat);
            float stoneHeat = Mathf.Clamp01(m_displayHeat / fullGlowHeat);
            UpdateCoalGlow(stoneHeat);

            if (m_shownHeat >= 0f && Mathf.Abs(m_displayHeat - m_shownHeat) < 0.2f)
            {
                return;
            }

            m_shownHeat = m_displayHeat;

            TintStones(m_domeRenderer, StoveVisual.DomeBandWeights, stoneHeat, 1f);
            TintStones(m_floorRenderer, StoveVisual.FloorBandWeights, stoneHeat, StoneRednessTuning.FloorGlowBoost);

            if (m_glowLight != null)
            {
                float intensity = StoneRednessTuning.LightIntensity * stoneHeat;
                m_glowLight.intensity = intensity;
                m_glowLight.range = StoneRednessTuning.LightRange;
                m_glowLight.enabled = intensity > 0.01f;
            }
        }

        /// Each material slot of the dome/floor is one heat band; its weight scales the redness.
        private void TintStones(MeshRenderer stoneRenderer, float[] bandWeights, float stoneHeat, float glowScale)
        {
            if (stoneRenderer == null)
            {
                return;
            }

            if (m_stoneBlock == null)
            {
                m_stoneBlock = new MaterialPropertyBlock();
            }

            Material[] materials = stoneRenderer.sharedMaterials;
            int bandCount = Mathf.Min(materials.Length, bandWeights.Length);

            for (int band = 0; band < bandCount; band++)
            {
                Material material = materials[band];
                if (material == null)
                {
                    continue;
                }

                float redness = bandWeights[band] * stoneHeat;
                m_stoneBlock.Clear();

                if (material.HasProperty(ColorId))
                {
                    m_stoneBlock.SetColor(ColorId, StoneRednessTuning.Apply(material.GetColor(ColorId), redness));
                }

                if (material.HasProperty(EmissionId))
                {
                    m_stoneBlock.SetColor(EmissionId, StoneRednessTuning.Emission(redness) * glowScale);
                }

                stoneRenderer.SetPropertyBlock(m_stoneBlock, band);
            }
        }

        /// Campfire logs inside the stove. Unlike the vanilla campfire they never disappear:
        /// with the fire out they lose their glow and darken to look burnt.
        private void UpdateCampfire(bool force)
        {
            bool burning = IsBurning();
            if (!force && burning == m_burning)
            {
                return;
            }

            m_burning = burning;

            if (m_logBlock == null)
            {
                m_logBlock = new MaterialPropertyBlock();
            }

            foreach (MeshRenderer log in m_campfireLogs)
            {
                if (log == null)
                {
                    continue;
                }

                Material[] materials = log.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    m_logBlock.Clear();

                    if (!burning && materials[slot] != null)
                    {
                        if (materials[slot].HasProperty(ColorId))
                        {
                            m_logBlock.SetColor(ColorId, materials[slot].GetColor(ColorId) * StoveVisual.CharredLogTint);
                        }

                        m_logBlock.SetColor(EmissionId, Color.black);
                    }

                    log.SetPropertyBlock(m_logBlock, slot);
                }
            }
        }

        /// Coals glow fully while the fire burns, and with the fire out as long as the stones are hot.
        private void UpdateCoalGlow(float stoneHeat)
        {
            float glow = Mathf.Max(m_burning ? 1f : 0f, stoneHeat) * Mathf.Max(0f, StoneRednessTuning.CoalGlow);
            if (m_coalRenderer == null || (m_shownCoalGlow >= 0f && Mathf.Abs(glow - m_shownCoalGlow) < 0.005f))
            {
                return;
            }

            m_shownCoalGlow = glow;

            if (m_coalBlock == null)
            {
                m_coalBlock = new MaterialPropertyBlock();
            }

            m_coalBlock.Clear();
            m_coalBlock.SetColor(EmissionId, StoneRednessTuning.CoalGlowColor * glow);
            m_coalRenderer.SetPropertyBlock(m_coalBlock);
        }

        // =====================================================================
        // Fire
        // =====================================================================

        /// Stores the original fire transforms so repeated rebuilds do not accumulate offsets.
        private void CacheFireRoots()
        {
            if (m_fireplace == null)
            {
                return;
            }

            GameObject[] fireObjects =
            {
                m_fireplace.m_enabledObject,
                m_fireplace.m_enabledObjectLow,
                m_fireplace.m_enabledObjectHigh
            };

            foreach (GameObject fireObject in fireObjects)
            {
                if (fireObject == null)
                {
                    continue;
                }

                m_fireRoots.Add(fireObject.transform);
                m_fireRootPositions.Add(fireObject.transform.localPosition);
                m_fireRootScales.Add(fireObject.transform.localScale);

                foreach (ParticleSystem flame in fireObject.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (flame == null || m_flames.Contains(flame))
                    {
                        continue;
                    }

                    m_flames.Add(flame);
                    m_flamePositions.Add(flame.transform.localPosition);
                    m_flameScales.Add(flame.transform.localScale);
                    m_flameStartSizes.Add(flame.main.startSizeMultiplier);
                }

                // The heat zone is an EffectArea. Do not touch the ignition Aoe,
                // otherwise the player could catch fire while standing away from the stove.
                foreach (EffectArea area in fireObject.GetComponentsInChildren<EffectArea>(true))
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

        /// Shrinks and lowers the vanilla iron firepit fire into the dome and keeps only the flames.
        private void ApplyFire()
        {
            for (int i = 0; i < m_fireRoots.Count; i++)
            {
                Transform fireRoot = m_fireRoots[i];
                if (fireRoot == null)
                {
                    continue;
                }

                fireRoot.localPosition = m_fireRootPositions[i] + Vector3.up * StoveVisual.FireOffsetY;
                fireRoot.localScale = m_fireRootScales[i] * StoveVisual.FireScale;

                foreach (ParticleSystem particles in fireRoot.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
                    if (particleRenderer != null)
                    {
                        particleRenderer.enabled = StoveVisual.KeepFlame(particles.gameObject.name);
                    }
                }
            }

            // Calculate heat radius from the original value so repeated rebuilds do not accumulate.
            for (int i = 0; i < m_warmthColliders.Count; i++)
            {
                if (m_warmthColliders[i] != null)
                {
                    m_warmthColliders[i].radius = m_warmthRadii[i] * StoveVisual.WarmthRadius;
                }
            }

            // Scale the flame transforms rather than only startSize, because much of the
            // vanilla flame appearance comes from other ParticleSystem modules,
            // making startSize alone barely noticeable.
            for (int i = 0; i < m_flames.Count; i++)
            {
                ParticleSystem flame = m_flames[i];
                if (flame == null)
                {
                    continue;
                }

                // Always start from the original values so repeated rebuilds do not accumulate.
                flame.transform.localPosition = m_flamePositions[i];
                flame.transform.localScale = m_flameScales[i];

                ParticleSystem.MainModule main = flame.main;
                main.startSizeMultiplier = m_flameStartSizes[i];

                if (!StoveVisual.KeepFlame(flame.gameObject.name))
                {
                    continue;
                }

                if (!m_fireRoots.Contains(flame.transform))
                {
                    // A flame on its own child transform: move and scale that child.
                    flame.transform.localPosition = m_flamePositions[i] + Vector3.up * StoveVisual.FlameOffsetY;
                    flame.transform.localScale = m_flameScales[i] * StoveVisual.FlameSize;
                }
                else
                {
                    // Rare fallback: a flame directly on a fire root. Leave the root untouched
                    // so heat/collider positions do not move, and adjust startSize instead.
                    main.startSizeMultiplier = m_flameStartSizes[i] * StoveVisual.FlameSize;
                }
            }
        }

        // =====================================================================
        // Pouring water
        // =====================================================================

        private void SetupAudio()
        {
            AudioSource vanillaSource = GetComponentInChildren<AudioSource>(true);

            m_audio = gameObject.AddComponent<AudioSource>();
            m_audio.playOnAwake = false;
            m_audio.loop = false;
            m_audio.spatialBlend = 1f;
            m_audio.rolloffMode = AudioRolloffMode.Linear;
            m_audio.minDistance = 3f;
            m_audio.maxDistance = 32f;

            if (vanillaSource != null)
            {
                m_audio.outputAudioMixerGroup = vanillaSource.outputAudioMixerGroup;
            }
        }

        private static double NetTime()
        {
            return ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;
        }

        public bool Pour(Humanoid user)
        {
            // Placement ghosts have no ZDO, so they have neither heat nor a pour cooldown.
            if (m_fireplace == null || !IsPlaced())
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
            float lastPour = m_nview.GetZDO().GetFloat(ZdoLastPour, -9999f);

            if (now - lastPour < StoveTuning.PourCooldown)
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
            float heatBeforePour = zdo.GetFloat(ZdoHeat, 0f);
            zdo.Set(ZdoHeat, Mathf.Max(0f, heatBeforePour - StoveTuning.PourHeatCost));
            zdo.Set(ZdoLastPour, (float)now);

            // The bucket increases the amount of steam per pour, not cloud speed or TTL.
            // Cooler stones give less steam.
            int cloudCount = GetWellSteamedSaunaTier() >= 3
                ? SteamTuning.CloudsPerPourWithBucket
                : SteamTuning.CloudsPerPour;
            cloudCount = Mathf.Max(1, Mathf.RoundToInt(cloudCount * StoveTuning.SteamFactor(heatBeforePour)));

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
                    string aromaFormat = Localization.instance != null
                        ? Localization.instance.Localize("$msg_sauna_mead_aroma")
                        : "The aroma of {0} fills the sauna";
                    user.Message(
                        MessageHud.MessageType.Center,
                        string.Format(aromaFormat, SaunaMeadSystem.LocalizedName(infusionType)));
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
            m_burstCloudCount = Mathf.Max(1, cloudCount);
            m_burstTimeLeft = BurstDuration;
            m_cloudsToSpawn = 0f;

            if (PourClip != null && m_audio != null)
            {
                m_audio.PlayOneShot(PourClip, PourVolume);
            }
        }

        /// Spreads the clouds of a pour evenly over BurstDuration.
        private void UpdateSteamBurst()
        {
            if (m_burstTimeLeft <= 0f || SteamPrefab == null)
            {
                return;
            }

            m_burstTimeLeft -= Time.deltaTime;
            m_cloudsToSpawn += m_burstCloudCount / BurstDuration * Time.deltaTime;

            while (m_cloudsToSpawn >= 1f)
            {
                m_cloudsToSpawn -= 1f;
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

            Vector2 flat = UnityEngine.Random.insideUnitCircle * BurstRadius;
            Vector3 position = transform.position + new Vector3(
                flat.x,
                BurstHeight + UnityEngine.Random.Range(0f, 0.4f),
                flat.y);

            GameObject cloud = Instantiate(SteamPrefab, position, Quaternion.identity);
            SteamTuning.Apply(cloud);
        }
    }
}
