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
        private string FindAssetDir(string sub)
        {
            string local = Path.Combine(Path.GetDirectoryName(Info.Location), sub);

            if (Directory.Exists(local))
            {
                return local;
            }

            try
            {
                string[] hits = Directory.GetDirectories(
                    BepInEx.Paths.PluginPath, sub, SearchOption.AllDirectories);

                if (hits.Length > 0)
                {
                    Jotunn.Logger.LogWarning($"'{sub}' not next to dll, using {hits[0]}");
                    return hits[0];
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"asset search failed: {ex.Message}");
            }

            Jotunn.Logger.LogWarning($"folder '{sub}' not found, expected at {local}");
            return null;
        }

        private IEnumerator LoadPourSound()
        {
            string dir = FindAssetDir("sounds");

            if (string.IsNullOrEmpty(dir))
            {
                yield break;
            }

            string[] files = { "pour.wav", "pour.ogg", "pour.mp3" };

            foreach (string file in files)
            {
                string path = Path.Combine(dir, file);

                if (!File.Exists(path))
                {
                    continue;
                }

                AudioType type = AudioType.MPEG;
                if (file.EndsWith(".wav"))
                {
                    type = AudioType.WAV;
                }
                else if (file.EndsWith(".ogg"))
                {
                    type = AudioType.OGGVORBIS;
                }

                string url = new Uri(path).AbsoluteUri;

                using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, type))
                {
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Jotunn.Logger.LogWarning($"sound load failed {file}: {req.error}");
                        continue;
                    }

                    AudioClip clip = DownloadHandlerAudioClip.GetContent(req);

                    if (clip == null || clip.length <= 0f)
                    {
                        Jotunn.Logger.LogWarning($"sound decoded empty: {file}");
                        continue;
                    }

                    clip.name = "sauna_pour";
                    SaunaStove.PourClip = clip;
                    Jotunn.Logger.LogInfo($"pour sound loaded: {file}, {clip.length:0.00}s");
                    yield break;
                }
            }

            Jotunn.Logger.LogWarning($"no pour sound file in {dir}");
        }
    }
}
