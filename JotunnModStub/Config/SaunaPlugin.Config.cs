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
            if (requirements == null)
            {
                return true;
            }

            foreach (RequirementConfig requirement in requirements)
            {
                if (FindRecipeItem(requirement.Item) == null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Resolves a recipe ingredient to its ItemDrop (Piece.Requirement needs an ItemDrop).
        /// Pieces are registered from OnVanillaPrefabsAvailable, when ObjectDB.instance already
        /// exists but its item list is still empty, so fall back to Jotunn's prefab cache then.
        /// </summary>
        private static ItemDrop FindRecipeItem(string itemName)
        {
            GameObject prefab = ObjectDB.instance != null && ObjectDB.instance.m_items.Count > 0
                ? ObjectDB.instance.GetItemPrefab(itemName)
                : PrefabManager.Instance.GetPrefab(itemName);

            return prefab != null ? prefab.GetComponent<ItemDrop>() : null;
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
            if (prefab == null)
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
                ItemDrop itemDrop = FindRecipeItem(config.Item);
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
    }
}
