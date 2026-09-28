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
        /// Redraws the stove icon. Called on world entry
        /// and from the editor when the view angle changes.
        public static void RefreshPieceIcon()
        {
            if (_stovePrefab == null || _instance == null)
            {
                return;
            }

            Piece piece = _stovePrefab.GetComponent<Piece>();
            if (piece == null)
            {
                return;
            }

            // The stove is the only piece with a hand-made icon; the render below is
            // just a fallback for when stove.png is missing. Whisks and bucket are always rendered.
            Sprite custom = _instance.LoadIcon(_instance.FindAssetDir("icons"), "stove.png");

            if (custom != null)
            {
                piece.m_icon = custom;
                Jotunn.Logger.LogInfo("piece icon: custom file");
                return;
            }

            GameObject temp = null;

            try
            {
                // Use a unique name so the renderer cannot return a cached previous view.
                temp = new GameObject("sauna_icon_src_" + (++_iconRenderCount));
                temp.SetActive(false);

                // Build exactly what is visible on the real stove: floor, dome, and stones.
                StoveVisual.Build(temp.transform);

                // The icon shows a lit stove, so hide the cooled coals.
                Transform coal = temp.transform.Find(StoveVisual.CoalName);
                if (coal != null)
                {
                    coal.gameObject.SetActive(false);
                }

                if (temp.GetComponentInChildren<MeshFilter>(true) == null)
                {
                    Jotunn.Logger.LogWarning("piece icon: no mesh");
                    return;
                }

                if (StoveVisual.IconFlame > 0)
                {
                    AddFlameToIcon(temp);
                }

                Sprite rendered = RenderManager.Instance.Render(
                    new RenderManager.RenderRequest(temp)
                    {
                        Rotation = UnityEngine.Quaternion.Euler(
                            StoveVisual.IconPitch, StoveVisual.IconYaw, 0f),
                        Width = 128,
                        Height = 128
                    });

                if (rendered != null)
                {
                    piece.m_icon = rendered;
                    Jotunn.Logger.LogInfo($"piece icon: rendered, yaw={StoveVisual.IconYaw:0}, " +
                        $"pitch={StoveVisual.IconPitch:0}");
                }
                else
                {
                    Jotunn.Logger.LogWarning("piece icon: render returned null");
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"piece icon failed: {ex.Message}");
            }
            finally
            {
                if (temp != null)
                {
                    UnityEngine.Object.Destroy(temp);
                }
            }
        }

        /// <summary>
        /// Selects the most complete sauna-whisk prefab instance. Jotunn Kitbash normally
        /// modifies the same object, but on first entry some versions or mod combinations
        /// may leave both a skeleton reference and a fully assembled prefab.
        /// Prefer the instance that actually contains Renderers.
        /// </summary>
        private static GameObject ResolveSaunaWrisksRuntimePrefab()
        {
            CustomPiece custom = PieceManager.Instance.GetPiece("sauna_wrisks");
            GameObject managerPrefab = custom != null ? custom.PiecePrefab : null;
            GameObject cachedPrefab = PrefabManager.Instance.GetPrefab("sauna_wrisks");

            GameObject best = null;
            int bestRendererCount = -1;

            GameObject[] candidates = { _wrisksPrefab, managerPrefab, cachedPrefab };
            foreach (GameObject candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }

                int renderers = candidate.GetComponentsInChildren<Renderer>(true).Length;
                if (best == null || renderers > bestRendererCount)
                {
                    best = candidate;
                    bestRendererCount = renderers;
                }
            }

            return best;
        }

        /// <summary>
        /// Verifies runtime registration of the whisks after Jotunn has created
        /// the real Hammer PieceTable. Normally this changes nothing.
        /// If first entry left an old or missing reference, replace it with the
        /// authoritative CustomPiece prefab, restore the ZNetScene hash, and
        /// refresh the local player's available build pieces without requiring a relog.
        /// </summary>
        private static void EnsureSaunaWrisksRuntimeRegistration(Player player)
        {
            try
            {
                GameObject prefab = ResolveSaunaWrisksRuntimePrefab();
                if (prefab == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_wrisks runtime verify: authoritative prefab not found");
                    return;
                }

                _wrisksPrefab = prefab;

                Piece piece = prefab.GetComponent<Piece>();
                if (piece == null)
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_wrisks runtime verify: Piece component missing");
                    return;
                }

                // Never let a late icon-render attempt make the menu entry unusable.
                if (piece.m_icon == null)
                {
                    if (_wrisksFallbackIcon != null)
                    {
                        piece.m_icon = _wrisksFallbackIcon;
                    }
                    else
                    {
                        GameObject donor = PrefabManager.Instance.GetPrefab("piece_walltorch");
                        Piece donorPiece = donor != null ? donor.GetComponent<Piece>() : null;
                        if (donorPiece != null)
                        {
                            piece.m_icon = donorPiece.m_icon;
                            _wrisksFallbackIcon = donorPiece.m_icon;
                        }
                    }
                }

                bool repairedTable = false;
                PieceTable hammer = PieceManager.Instance.GetPieceTable(PieceTables.Hammer);
                if (hammer != null)
                {
                    int namedIndex = -1;
                    for (int i = hammer.m_pieces.Count - 1; i >= 0; --i)
                    {
                        GameObject current = hammer.m_pieces[i];
                        if (current == null)
                        {
                            hammer.m_pieces.RemoveAt(i);
                            repairedTable = true;
                            continue;
                        }

                        if (current.name == prefab.name)
                        {
                            if (namedIndex < 0)
                            {
                                namedIndex = i;
                            }
                            else
                            {
                                // Remove duplicate same-name entries, keeping one slot.
                                hammer.m_pieces.RemoveAt(i);
                                repairedTable = true;
                                if (i < namedIndex)
                                {
                                    namedIndex--;
                                }
                            }
                        }
                    }

                    if (namedIndex >= 0)
                    {
                        if (hammer.m_pieces[namedIndex] != prefab)
                        {
                            hammer.m_pieces[namedIndex] = prefab;
                            repairedTable = true;
                        }
                    }
                    else
                    {
                        hammer.m_pieces.Add(prefab);
                        repairedTable = true;
                    }
                }
                else
                {
                    Jotunn.Logger.LogWarning(
                        "sauna_wrisks runtime verify: Hammer PieceTable not found");
                }

                bool repairedNetwork = false;
                if (ZNetScene.instance != null)
                {
                    int hash = prefab.name.GetStableHashCode();
                    GameObject registered;
                    if (!ZNetScene.instance.m_namedPrefabs.TryGetValue(hash, out registered))
                    {
                        PrefabManager.Instance.RegisterToZNetScene(prefab);
                        repairedNetwork = true;
                    }
                    else if (registered != prefab)
                    {
                        // A same-name pre-kitbash skeleton can survive the first registration
                        // pass. Spawning by hash must resolve to the final PieceManager prefab.
                        ZNetScene.instance.m_namedPrefabs[hash] = prefab;
                        repairedNetwork = true;
                    }
                }

                if (player != null)
                {
                    player.UpdateKnownRecipesList();
                    player.UpdateAvailablePiecesList();
                }

                int renderers = prefab.GetComponentsInChildren<Renderer>(true).Length;
                Jotunn.Logger.LogInfo(
                    $"sauna_wrisks runtime verify: tableRepair={repairedTable}, " +
                    $"networkRepair={repairedNetwork}, renderers={renderers}, " +
                    $"playerRefresh={(player != null)}");
            }
            catch (Exception ex)
            {
                // This is a self-heal path. It must never be allowed to break the rest of
                // SaunaMod if another mod has replaced the build table or network registry.
                Jotunn.Logger.LogWarning(
                    "sauna_wrisks runtime verify failed (non-fatal): " + ex);
            }
        }

        /// Redraws the whisks icon from the actual assembled model.
        public static void RefreshWrisksIcon()
        {
            GameObject resolved = ResolveSaunaWrisksRuntimePrefab();
            if (resolved != null)
            {
                _wrisksPrefab = resolved;
            }

            Piece wrisksPiece = _wrisksPrefab != null ? _wrisksPrefab.GetComponent<Piece>() : null;
            if (wrisksPiece != null && wrisksPiece.m_icon == null && _wrisksFallbackIcon != null)
            {
                wrisksPiece.m_icon = _wrisksFallbackIcon;
            }

            RefreshVisualPieceIcon(
                _wrisksPrefab,
                "sauna_wrisks_visual_root",
                SaunaPieceIconTuning.WrisksYaw,
                SaunaPieceIconTuning.WrisksPitch,
                SaunaPieceIconTuning.WrisksRoll,
                SaunaPieceIconTuning.WrisksOffsetX,
                SaunaPieceIconTuning.WrisksOffsetY,
                SaunaPieceIconTuning.WrisksDistance,
                SaunaPieceIconTuning.WrisksScale,
                "wrisks");
        }

        /// Redraws the bucket icon from the actual assembled model.
        public static void RefreshBucketIcon()
        {
            RefreshVisualPieceIcon(
                _bucketPrefab,
                "sauna_bucket_visual_root",
                SaunaPieceIconTuning.BucketYaw,
                SaunaPieceIconTuning.BucketPitch,
                SaunaPieceIconTuning.BucketRoll,
                SaunaPieceIconTuning.BucketOffsetX,
                SaunaPieceIconTuning.BucketOffsetY,
                SaunaPieceIconTuning.BucketDistance,
                SaunaPieceIconTuning.BucketScale,
                "bucket");
        }

        private static void RefreshVisualPieceIcon(
            GameObject piecePrefab,
            string visualRootName,
            float yaw,
            float pitch,
            float roll,
            float offsetX,
            float offsetY,
            float distance,
            float iconScale,
            string label)
        {
            if (piecePrefab == null)
            {
                Jotunn.Logger.LogWarning($"{label} icon: piece prefab is null");
                return;
            }

            Piece piece = piecePrefab.GetComponent<Piece>();
            Transform sourceRoot = piecePrefab.transform.Find(visualRootName);
            if (piece == null || sourceRoot == null)
            {
                Jotunn.Logger.LogWarning($"{label} icon: visual root '{visualRootName}' not found");
                return;
            }

            GameObject temp = null;

            try
            {
                temp = new GameObject($"sauna_{label}_icon_src_{++_iconRenderCount}");
                temp.SetActive(false);

                GameObject visual = UnityEngine.Object.Instantiate(sourceRoot.gameObject, temp.transform, false);
                visual.name = visualRootName;

                // IMPORTANT: Rotation used to be passed to RenderManager. In our kitbash models
                // the visible geometry is far from the root origin, so rotating it there
                // made it sweep a large arc and move out of frame.
                //
                // Now rotate the visual first, calculate the real Renderer.bounds for that view,
                // and move their center exactly to the temporary root origin. RenderManager
                // receives identity rotation, so every view rotates around the same center.
                visual.transform.localPosition = UnityEngine.Vector3.zero;
                visual.transform.localRotation = UnityEngine.Quaternion.Euler(pitch, yaw, roll);
                visual.transform.localScale *= Mathf.Max(0.05f, iconScale);

                if (!CenterIconVisualOnRendererBounds(visual.transform, temp.transform, out UnityEngine.Vector3 originalCenter))
                {
                    Jotunn.Logger.LogWarning($"{label} icon: no renderer in '{visualRootName}'");
                    return;
                }

                // These three parameters are applied AFTER centering and rotation.
                // They therefore do not change the pivot or cause another frame shift when angles change.
                visual.transform.localPosition += new UnityEngine.Vector3(offsetX, offsetY, distance);

                Sprite rendered = RenderManager.Instance.Render(
                    new RenderManager.RenderRequest(temp)
                    {
                        Rotation = UnityEngine.Quaternion.identity,
                        Width = 128,
                        Height = 128
                    });

                if (rendered != null)
                {
                    piece.m_icon = rendered;
                    Jotunn.Logger.LogInfo(
                        $"{label} icon: rendered, yaw={yaw:0.###}, pitch={pitch:0.###}, roll={roll:0.###}, " +
                        $"offset=({offsetX:0.###},{offsetY:0.###}), distance={distance:0.###}, scale={iconScale:0.###}, " +
                        $"centerWas={originalCenter}");
                }
                else
                {
                    Jotunn.Logger.LogWarning($"{label} icon: render returned null");
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"{label} icon failed: {ex.Message}");
            }
            finally
            {
                if (temp != null)
                {
                    UnityEngine.Object.Destroy(temp);
                }
            }
        }

        private static bool CenterIconVisualOnRendererBounds(
            Transform visualRoot,
            Transform frameRoot,
            out UnityEngine.Vector3 centerInFrame)
        {
            centerInFrame = UnityEngine.Vector3.zero;

            if (visualRoot == null || frameRoot == null)
            {
                return false;
            }

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds combined = new Bounds();

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                return false;
            }

            // Renderer.bounds are in world space. Convert the center to temp/frame coordinates
            // and move the root by the opposite vector. Parent translation is independent of
            // the visual's local Rotation/Scale, so the center becomes exactly (0,0,0).
            centerInFrame = frameRoot.InverseTransformPoint(combined.center);
            visualRoot.localPosition -= centerInFrame;
            return true;
        }

        /// Saves the current stove icon as a PNG next to the DLL.
        public static void DumpPieceIcon()
        {
            string file = $"stove_render_y{StoveVisual.IconYaw:0.###}_p{StoveVisual.IconPitch:0.###}.png";
            DumpIconForPrefab(_stovePrefab, file, "stove");
        }

        public static void DumpWrisksIcon()
        {
            string file = $"wrisks_render_y{SaunaPieceIconTuning.WrisksYaw:0.###}_" +
                          $"p{SaunaPieceIconTuning.WrisksPitch:0.###}_" +
                          $"r{SaunaPieceIconTuning.WrisksRoll:0.###}_" +
                          $"d{SaunaPieceIconTuning.WrisksDistance:0.###}_" +
                          $"s{SaunaPieceIconTuning.WrisksScale:0.###}.png";
            DumpIconForPrefab(_wrisksPrefab, file, "wrisks");
        }

        public static void DumpBucketIcon()
        {
            string file = $"bucket_render_y{SaunaPieceIconTuning.BucketYaw:0.###}_" +
                          $"p{SaunaPieceIconTuning.BucketPitch:0.###}_" +
                          $"r{SaunaPieceIconTuning.BucketRoll:0.###}_" +
                          $"d{SaunaPieceIconTuning.BucketDistance:0.###}_" +
                          $"s{SaunaPieceIconTuning.BucketScale:0.###}.png";
            DumpIconForPrefab(_bucketPrefab, file, "bucket");
        }

        private static void DumpIconForPrefab(GameObject prefab, string fileName, string label)
        {
            if (prefab == null || _instance == null)
            {
                Jotunn.Logger.LogWarning($"{label} icon dump: prefab/instance missing");
                return;
            }

            Piece piece = prefab.GetComponent<Piece>();
            if (piece == null || piece.m_icon == null || piece.m_icon.texture == null)
            {
                Jotunn.Logger.LogWarning($"{label} icon dump: nothing to save");
                return;
            }

            Texture2D copy = null;

            try
            {
                Texture2D src = piece.m_icon.texture;
                byte[] png = null;

                try
                {
                    png = src.EncodeToPNG();
                }
                catch (Exception)
                {
                    png = null;
                }

                if (png == null)
                {
                    RenderTexture rt = RenderTexture.GetTemporary(
                        src.width, src.height, 0, RenderTextureFormat.ARGB32);

                    RenderTexture previous = RenderTexture.active;
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;

                    copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                    copy.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
                    copy.Apply();

                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);

                    png = copy.EncodeToPNG();
                }

                string dir = Path.GetDirectoryName(_instance.Info.Location);
                string path = Path.Combine(dir, fileName);

                File.WriteAllBytes(path, png);
                Jotunn.Logger.LogInfo($"{label} icon dump saved: {path} ({src.width}x{src.height})");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"{label} icon dump failed: {ex.Message}");
            }
            finally
            {
                if (copy != null)
                {
                    UnityEngine.Object.Destroy(copy);
                }
            }
        }

        /// Copies the flame from the stove prefab for icon rendering and removes nonvisual parts:
        /// lights, effect areas, and audio are unnecessary for rendering and may spam the log.
        private static void AddFlameToIcon(GameObject temp)
        {
            Fireplace fireplace = _stovePrefab.GetComponent<Fireplace>();

            if (fireplace == null)
            {
                return;
            }

            GameObject source = fireplace.m_enabledObjectHigh != null
                ? fireplace.m_enabledObjectHigh
                : fireplace.m_enabledObject;

            if (source == null)
            {
                return;
            }

            GameObject flame = UnityEngine.Object.Instantiate(source, temp.transform);
            flame.name = "icon_flame";
            flame.transform.localPosition = source.transform.localPosition;
            flame.transform.localRotation = source.transform.localRotation;
            flame.transform.localScale = source.transform.localScale;
            flame.SetActive(true);

            foreach (Light light in flame.GetComponentsInChildren<Light>(true))
            {
                UnityEngine.Object.DestroyImmediate(light);
            }

            foreach (EffectArea area in flame.GetComponentsInChildren<EffectArea>(true))
            {
                UnityEngine.Object.DestroyImmediate(area);
            }

            foreach (AudioSource audio in flame.GetComponentsInChildren<AudioSource>(true))
            {
                UnityEngine.Object.DestroyImmediate(audio);
            }
        }

        private void EnsureIcons()
        {
            string dir = FindAssetDir("icons");

            _tooHot.m_icon = FindIcon("Burning");
            _steaming.m_icon = LoadIcon(dir, "steaming.png") ?? FindIcon("Resting", "Rested");
            _wellSteamed.m_icon = LoadIcon(dir, "wellsteamed.png") ?? FindIcon("Rested", "Resting");

            Jotunn.Logger.LogInfo($"icons: tooHot={_tooHot.m_icon != null}, steaming={_steaming.m_icon != null}, " +
                $"wellSteamed={_wellSteamed.m_icon != null}");
        }

        private Sprite LoadIcon(string dir, string fileName)
        {
            if (string.IsNullOrEmpty(dir))
            {
                return null;
            }

            string path = Path.Combine(dir, fileName);

            if (!File.Exists(path))
            {
                Jotunn.Logger.LogInfo($"icon not found: {path}");
                return null;
            }

            try
            {
                Sprite sprite = AssetUtils.LoadSpriteFromFile(path);
                Jotunn.Logger.LogInfo(sprite != null
                    ? $"icon loaded: {fileName}"
                    : $"icon failed to parse: {fileName}");
                return sprite;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"icon load error {fileName}: {ex.Message}");
                return null;
            }
        }

        private Sprite FindIcon(params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                StatusEffect se = FindEffect(candidate);
                if (se != null && se.m_icon != null)
                {
                    return se.m_icon;
                }
            }
            return null;
        }
    }
}
