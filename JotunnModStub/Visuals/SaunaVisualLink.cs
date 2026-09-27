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
    /// Purely visual connection between new sauna pieces and the Sauna Stove.
    /// The accessories do not use StationExtension and the stove does not use CraftingStation;
    /// the mod only borrows vanilla vfx_ExtensionConnection from forge_ext1.
    /// </summary>
    internal static class SaunaVisualLink
    {
        // Close to vanilla workbench/forge extension distance.
        // Beyond this distance the line is simply hidden; placement is NOT blocked.
        public const float MaxLinkDistance = 5f;

        private static GameObject s_connectionPrefab;
        private static bool s_connectionPrefabSearched;
        private static GameObject s_connectionInstance;

        public static void UpdateForPlacementGhost(GameObject ghost)
        {
            if (ghost == null)
            {
                Hide();
                return;
            }

            Piece piece = ghost.GetComponent<Piece>();
            if (!IsLinkedSaunaPiece(piece, ghost))
            {
                Hide();
                return;
            }

            SaunaStove stove = SaunaStove.FindClosestForVisualLink(
                ghost.transform.position,
                MaxLinkDistance);

            if (stove == null)
            {
                Hide();
                return;
            }

            GameObject connectionPrefab = ConnectionPrefab();
            if (connectionPrefab == null)
            {
                Hide();
                return;
            }

            UnityEngine.Vector3 from = VisualCenter(ghost);
            UnityEngine.Vector3 to = stove.VisualLinkPoint();
            UnityEngine.Vector3 direction = to - from;

            if (direction.sqrMagnitude < 0.0001f)
            {
                Hide();
                return;
            }

            if (s_connectionInstance == null)
            {
                s_connectionInstance = UnityEngine.Object.Instantiate(
                    connectionPrefab,
                    from,
                    UnityEngine.Quaternion.identity);
                s_connectionInstance.name = "sauna_visual_connection";
            }

            float distance = direction.magnitude;
            s_connectionInstance.transform.position = from;
            s_connectionInstance.transform.rotation =
                UnityEngine.Quaternion.LookRotation(direction / distance);
            s_connectionInstance.transform.localScale =
                new UnityEngine.Vector3(1f, 1f, distance);

            if (!s_connectionInstance.activeSelf)
            {
                s_connectionInstance.SetActive(true);
            }
        }

        public static void Hide()
        {
            if (s_connectionInstance != null && s_connectionInstance.activeSelf)
            {
                s_connectionInstance.SetActive(false);
            }
        }

        private static bool IsLinkedSaunaPiece(Piece piece, GameObject ghost)
        {
            if (piece != null)
            {
                // m_name is the most reliable identifier for the placement ghost of our Pieces.
                if (piece.m_name == "$piece_sauna_wrisks" ||
                    piece.m_name == "$piece_sauna_bucket")
                {
                    return true;
                }
            }

            // Small fallback to prefab name in case a localization token changes in the future.
            string prefabName = Utils.GetPrefabName(ghost);
            return prefabName == "sauna_wrisks" || prefabName == "sauna_bucket";
        }

        private static GameObject ConnectionPrefab()
        {
            if (s_connectionPrefabSearched)
            {
                return s_connectionPrefab;
            }

            if (ZNetScene.instance == null)
            {
                // Do not cache failure permanently; during early loading the scene may not be ready yet.
                return null;
            }

            s_connectionPrefabSearched = true;

            // forge_ext1 and workbench extensions use the same golden connection VFX.
            string[] donors = { "forge_ext1", "piece_workbench_ext1" };
            foreach (string donorName in donors)
            {
                GameObject donor = ZNetScene.instance.GetPrefab(donorName);
                if (donor == null)
                {
                    continue;
                }

                StationExtension extension = donor.GetComponent<StationExtension>();
                if (extension != null && extension.m_connectionPrefab != null)
                {
                    s_connectionPrefab = extension.m_connectionPrefab;
                    Jotunn.Logger.LogInfo(
                        $"sauna visual link: borrowed connection VFX from {donorName}");
                    break;
                }
            }

            if (s_connectionPrefab == null)
            {
                Jotunn.Logger.LogWarning(
                    "sauna visual link: vanilla extension connection VFX not found; visual links disabled");
            }

            return s_connectionPrefab;
        }

        private static UnityEngine.Vector3 VisualCenter(GameObject ghost)
        {
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>(true);
            bool haveBounds = false;
            Bounds bounds = new Bounds(ghost.transform.position, UnityEngine.Vector3.zero);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                // Ignore possible helper particle/renderers; only the visual center of the Piece model is needed.
                if (renderer is ParticleSystemRenderer || renderer is LineRenderer)
                {
                    continue;
                }

                if (!haveBounds)
                {
                    bounds = renderer.bounds;
                    haveBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!haveBounds)
            {
                return ghost.transform.position + UnityEngine.Vector3.up * 0.25f;
            }

            return bounds.center;
        }
    }
}
