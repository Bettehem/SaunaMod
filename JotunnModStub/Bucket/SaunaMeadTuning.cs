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
    /// <summary>
    /// Synergy between the sauna bucket and resistance meads.
    /// DurationMultiplier is applied to the normal duration of the vanilla effect.
    /// </summary>
    internal static class SaunaMeadTuning
    {
        public static float DurationMultiplier = 1.20f;
        public const float EffectRadius = 8f;
    }
}
