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
}
