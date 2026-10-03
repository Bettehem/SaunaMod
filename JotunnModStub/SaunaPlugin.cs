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
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal partial class SaunaPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "nikita.valheim.sauna";
        public const string PluginName = "SaunaMod";
        public const string PluginVersion = "2.2.0";
        private const string ConfigFileName = "nekitker.saunamod.cfg";

        private const string DefaultStoveRecipe = "Wood:10,Coal:5,Stone:40";
        private const string DefaultWhisksRecipe = "FineWood:5,BronzeNails:1";
        private const string DefaultBucketRecipe = "Iron:5,FineWood:10";
        private const string DefaultTowelRackRecipe = "FineWood:5,WolfPelt:5";

        private const int SteamLayer = 30;
        private const int SmokeLayer = 31;

        private const float CheckInterval = 0.5f;

        // Gameplay defaults. BindConfig() may replace these values from the .cfg file.
        private static float SteamTimeToBuff = 20f;
        // Calm healing only while the player is physically inside sauna steam.
        // 0.5 HP/s = 30 HP/min: useful recovery, but deliberately not a combat heal.
        private static float SteamHealPerSecond = 0.5f;
        public static float SteamDamagePerSecond = 4f;
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
        private ConfigEntry<string> _cfgTowelRackRecipe;

        private ConfigEntry<float> _cfgSteamTimeToBuff;
        private ConfigEntry<float> _cfgSteamHealPerSecond;
        private ConfigEntry<float> _cfgDetectRadius;
        private ConfigEntry<float> _cfgSteamGrace;
        private ConfigEntry<bool> _cfgTooHotWeapons;

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

        private ConfigEntry<int> _cfgStoveMaxWood;
        private ConfigEntry<StoveTuning.HeatingSpeed> _cfgStoveHeatingSpeed;
        private ConfigEntry<float> _cfgStoveMaxHeat;
        private ConfigEntry<float> _cfgStoveCoolPerMinute;
        private ConfigEntry<float> _cfgStoveCoolingDelaySeconds;
        private ConfigEntry<bool> _cfgStoveSteamDependsOnHeat;
        private ConfigEntry<StoneRednessTuning.StoveGlow> _cfgStoveGlow;
        private ConfigEntry<bool> _cfgStoveShowHeatOnHover;

        public static int WellSteamedHash;

        private static GameObject _prefabContainer;
        private static GameObject _steamVfxPrefab;
        private static GameObject _stovePrefab;
        private static GameObject _wrisksPrefab;
        private static Sprite _wrisksFallbackIcon;
        private static GameObject _bucketPrefab;
        private static GameObject _towelRackPrefab;
        private static int _iconRenderCount;
        private static SaunaPlugin _instance;

        private static SE_Stats _steaming;
        private static SE_WellSteamed _wellSteamed;
        private static SE_Burning _tooHot;

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
            // A headless dedicated server has no audio device: clips decode empty
            // and nobody would hear them anyway.
            if (!GUIManager.IsHeadless())
            {
                StartCoroutine(LoadPourSound());
            }
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
    }
}
