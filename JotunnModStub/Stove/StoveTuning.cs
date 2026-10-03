// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using UnityEngine;

namespace SaunaMod
{
    /// Fuel, stone-heat and pour rules of the sauna stove. Tuned live from the editor.
    ///
    /// The stones heat up while the stove burns. After the fire goes out they hold their heat
    /// for CoolingDelaySeconds, then cool down. Every pour turns part of that heat into steam;
    /// below MinPourHeat the stones are too cold to produce steam.
    internal static class StoveTuning
    {
        /// Upper bound of the stone heat scale.
        public static float MaxHeat = 100f;

        /// Wood capacity of the stove.
        public static int MaxWood = 5;

        /// Seconds one piece of wood burns.
        public static float SecondsPerWood = 60f;

        /// Heat gained per minute while the stove is burning.
        /// 20 heats the stones from 0 to 100 in five minutes, exactly one full load of wood.
        public static float HeatPerMinute = 20f;

        /// Heat lost per minute once the stove has been out for CoolingDelaySeconds.
        /// 5 cools fully heated stones from 100 to 0 in 20 minutes.
        public static float CoolPerMinute = 5f;

        /// Seconds the stones keep their heat after the fire goes out before they start cooling.
        public static float CoolingDelaySeconds = 60f;

        /// Heat consumed by one pour.
        public static float PourHeatCost = 20f;

        /// Stones below this heat are too cold to pour on.
        public static float MinPourHeat = 50f;

        /// Whisks and bucket give comfort while the stones are at least this hot.
        /// Must stay below MinPourHeat - PourHeatCost minus the cooling over one steam cloud's
        /// lifetime, otherwise comfort can vanish while the player is still steaming.
        public static float ComfortMinHeat = 15f;

        /// Seconds between two pours on the same stove.
        public static float PourCooldown = 5f;

        /// 1 = append the stone heat to the stove hover text.
        public static int ShowHeatOnHover = 1;

        /// When false (cfg Stove.SteamDependsOnHeat), every pour gives the full amount of steam.
        public static bool SteamDependsOnHeat = true;

        /// Share of the steam a pour gives at 0 heat. Steam scales linearly from this
        /// at 0 heat up to the full cloud count at MaxHeat.
        public static float SteamAtZeroHeat = 0.5f;

        /// Multiplier for the cloud count of a pour made at the given stone heat.
        public static float SteamFactor(float heat)
        {
            if (!SteamDependsOnHeat)
            {
                return 1f;
            }

            return Mathf.Lerp(Mathf.Clamp01(SteamAtZeroHeat), 1f, Mathf.Clamp01(heat / MaxHeat));
        }

        public enum HeatingSpeed
        {
            Standard,
            Fast
        }

        /// Heating and pour cost for the chosen preset (cfg Stove.HeatingSpeed).
        /// Fast: the stones heat in 3 min 20 s instead of 5 min and give 4 pours instead of 3.
        /// Cooling is a separate setting (cfg Stove.CoolPerMinute).
        public static void ApplyHeatingSpeed(HeatingSpeed speed)
        {
            switch (speed)
            {
                case HeatingSpeed.Fast:
                    HeatPerMinute = 30f;
                    PourHeatCost = 15f;
                    break;

                default:
                    HeatPerMinute = 20f;
                    PourHeatCost = 20f;
                    break;
            }
        }

        public static void ApplyFuel(Fireplace fireplace)
        {
            if (fireplace == null)
            {
                return;
            }

            fireplace.m_maxFuel = Mathf.Max(1, MaxWood);
            fireplace.m_secPerFuel = Mathf.Max(1f, SecondsPerWood);
            fireplace.m_startFuel = Mathf.Min(fireplace.m_startFuel, fireplace.m_maxFuel);
        }
    }
}
