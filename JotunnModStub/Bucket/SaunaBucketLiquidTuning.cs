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
}
