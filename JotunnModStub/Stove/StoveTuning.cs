// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using UnityEngine;

namespace SaunaMod
{
    /// Fuel, stone-heat and pour rules of the sauna stove. Tuned live from the editor.
    ///
    /// The stones heat up while the stove burns and cool down while it does not.
    /// Every pour turns part of that heat into steam; below MinPourHeat the stones
    /// are too cold to produce steam.
    internal static class StoveTuning
    {
        /// Upper bound of the stone heat scale.
        public const float MaxHeat = 100f;

        /// Wood capacity of the stove.
        public static int MaxFuel = 5;

        /// Seconds one piece of wood burns.
        public static float SecPerFuel = 60f;

        /// Heat gained per minute while the stove is burning.
        /// 20 heats the stones from 0 to 100 in five minutes, exactly one full load of wood.
        public static float HeatPerMinute = 20f;

        /// Heat lost per minute while the stove is not burning.
        public static float CoolPerMinute = 15f;

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

        public enum HeatingSpeed
        {
            Standard,
            Fast
        }

        /// Heat, cooling and pour cost for the chosen preset (cfg Stove.HeatingSpeed).
        /// Fast: the stones heat in 3 min 20 s instead of 5 min, cool slower and give 4 pours instead of 3.
        public static void ApplyHeatingSpeed(HeatingSpeed speed)
        {
            switch (speed)
            {
                case HeatingSpeed.Fast:
                    HeatPerMinute = 30f;
                    CoolPerMinute = 10f;
                    PourHeatCost = 15f;
                    break;

                default:
                    HeatPerMinute = 20f;
                    CoolPerMinute = 15f;
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

            fireplace.m_maxFuel = Mathf.Max(1, MaxFuel);
            fireplace.m_secPerFuel = Mathf.Max(1f, SecPerFuel);
            fireplace.m_startFuel = Mathf.Min(fireplace.m_startFuel, fireplace.m_maxFuel);
        }
    }
}
