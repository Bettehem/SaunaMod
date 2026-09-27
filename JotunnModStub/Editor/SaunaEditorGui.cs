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
    internal class SaunaEditorGui : MonoBehaviour
    {
        private void OnGUI()
        {
            GUI.Box(new Rect(10f, 10f, 660f, 132f), "");
            GUI.Label(new Rect(20f, 16f, 640f, 116f), SaunaEditor.StatusLine());
        }
    }
}
