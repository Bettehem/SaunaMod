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
    internal class SaunaVfxLife : MonoBehaviour
    {
        public float Remaining = 60f;

        private bool m_stopped;

        private void Update()
        {
            Remaining -= Time.deltaTime;

            if (Remaining > 0f || m_stopped)
            {
                return;
            }

            m_stopped = true;

            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.EmissionModule emission = ps.emission;
                emission.enabled = false;
            }

            Destroy(gameObject, 5f);
        }
    }
}
