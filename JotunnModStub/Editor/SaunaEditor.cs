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

        private static readonly Action RefreshStoveFuel = () => SaunaStove.ApplyFuelToAll();
        private static readonly Action RefreshStoneHeat = () => SaunaStove.RefreshStoneHeatAll();
        private static readonly Action RefreshWrisksIcon = () => SaunaPlugin.RefreshWrisksIcon();
        private static readonly Action RefreshBucketIcon = () => SaunaPlugin.RefreshBucketIcon();
        private static readonly Action RefreshBucketLiquidsAndIcon = () =>
        {
            SaunaPlugin.RefreshBucketLiquidVisuals();
            SaunaPlugin.RefreshBucketIcon();
        };
        private static readonly Action RefreshSkinRedness = () => SaunaPlugin.ApplySkinRedness(Player.m_localPlayer);

        private static readonly Section[] Sections =
        {
            new Section
            {
                Name = "Sauna Stove",
                Entries = new[]
                {
                    new Entry { Name = "fuel.MaxWood",          Get = () => StoveTuning.MaxFuel,         Set = v => StoveTuning.MaxFuel = Mathf.Max(1, (int)v),       Step = 1f,  Integer = true, Changed = RefreshStoveFuel },
                    new Entry { Name = "fuel.SecondsPerWood",   Get = () => StoveTuning.SecPerFuel,      Set = v => StoveTuning.SecPerFuel = Mathf.Max(1f, v),        Step = 5f,  Changed = RefreshStoveFuel },

                    new Entry { Name = "heat.HeatPerMinute",    Get = () => StoveTuning.HeatPerMinute,   Set = v => StoveTuning.HeatPerMinute = v,                    Step = 1f },
                    new Entry { Name = "heat.CoolPerMinute",    Get = () => StoveTuning.CoolPerMinute,   Set = v => StoveTuning.CoolPerMinute = v,                    Step = 1f },
                    new Entry { Name = "heat.PourCost",         Get = () => StoveTuning.PourHeatCost,    Set = v => StoveTuning.PourHeatCost = v,                     Step = 1f },
                    new Entry { Name = "heat.MinToPour",        Get = () => StoveTuning.MinPourHeat,     Set = v => StoveTuning.MinPourHeat = Mathf.Min(StoveTuning.MaxHeat, v), Step = 5f },
                    // 0 = hidden; 1 = show the stone heat when hovering over the stove.
                    new Entry { Name = "heat.ShowOnHover",      Get = () => StoveTuning.ShowHeatOnHover, Set = v => StoveTuning.ShowHeatOnHover = Mathf.Clamp((int)v, 0, 1), Step = 1f, Integer = true },

                    new Entry { Name = "heat.ComfortMinHeat",   Get = () => StoveTuning.ComfortMinHeat,  Set = v => StoveTuning.ComfortMinHeat = Mathf.Min(StoveTuning.MaxHeat, v), Step = 5f },

                    new Entry { Name = "pour.Cooldown",         Get = () => StoveTuning.PourCooldown,    Set = v => StoveTuning.PourCooldown = v,                     Step = 0.5f },

                    new Entry { Name = "stones.MaxRedness",     Get = () => StoneRednessTuning.Strength,       Set = v => StoneRednessTuning.Strength = Mathf.Clamp01(v),   Step = 0.05f, Changed = RefreshStoneHeat },
                    new Entry { Name = "stones.Glow",           Get = () => StoneRednessTuning.Glow,           Set = v => StoneRednessTuning.Glow = v,                      Step = 0.05f, Changed = RefreshStoneHeat },
                    new Entry { Name = "stones.LightIntensity", Get = () => StoneRednessTuning.LightIntensity, Set = v => StoneRednessTuning.LightIntensity = v,            Step = 0.05f, Changed = RefreshStoneHeat },
                    new Entry { Name = "stones.LightRange",     Get = () => StoneRednessTuning.LightRange,     Set = v => StoneRednessTuning.LightRange = v,                Step = 0.25f, Changed = RefreshStoneHeat },
                    // 0 = real stone heat; 1 = show the stones at 100 heat to tune the color.
                    new Entry { Name = "stones.Preview",        Get = () => StoneRednessTuning.Preview,        Set = v => StoneRednessTuning.Preview = Mathf.Clamp((int)v, 0, 1), Step = 1f, Integer = true, Changed = RefreshStoneHeat },

                    new Entry { Name = "TEST Stones heat = 100", Get = () => 0f, Step = 1f, ActionOnly = true, Trigger = () =>
                    {
                        if (Player.m_localPlayer != null)
                        {
                            SaunaStove.SetHeatNearest(Player.m_localPlayer.transform.position, StoveTuning.MaxHeat);
                        }
                    } }
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
                Name = "Skin Redness",
                Entries = new[]
                {
                    new Entry { Name = "skinredness.Strength",    Get = () => SkinRednessTuning.Strength,    Set = v => SkinRednessTuning.Strength = Mathf.Clamp01(v),    Step = 0.05f, Changed = RefreshSkinRedness },
                    new Entry { Name = "skinredness.CoolSeconds", Get = () => SkinRednessTuning.CoolSeconds, Set = v => SkinRednessTuning.CoolSeconds = Mathf.Max(1f, v), Step = 5f },
                    // 0 = real sauna heat; 1 = show full redness to tune the color.
                    new Entry { Name = "skinredness.Preview",     Get = () => SkinRednessTuning.Preview,     Set = v => SkinRednessTuning.Preview = Mathf.Clamp((int)v, 0, 1), Step = 1f, Integer = true, Changed = RefreshSkinRedness }
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
}
