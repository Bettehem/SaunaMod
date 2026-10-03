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
    /// Small vanilla tier star shown on top of the Well Steamed icon.
    /// No custom star PNG is used; clone the existing MinLevel UI element
    /// from InventoryGui into the HUD.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
    internal static class SaunaWellSteamedTierBadgePatch
    {
        private const string BadgeName = "SaunaWellSteamedTierBadge";

        // Editable position and scale parameters for the tier star on Well Steamed.
        public static float BadgeOffsetX = 7f;
        public static float BadgeOffsetY = -60f;
        public static float BadgeScale = 0.55f;

        private static readonly List<GameObject> s_badges = new List<GameObject>();
        private static readonly List<UnityEngine.UI.Image> s_hudImages = new List<UnityEngine.UI.Image>();
        private static bool s_loggedSource;
        private static bool s_loggedMissingSource;

        private static void Postfix(Hud __instance)
        {
            HideTrackedBadges();

            if (__instance == null || Player.m_localPlayer == null ||
                SaunaPlugin.WellSteamedHash == 0)
            {
                return;
            }

            SEMan seman = Player.m_localPlayer.GetSEMan();
            if (seman == null)
            {
                return;
            }

            SE_WellSteamed live = seman.GetStatusEffect(SaunaPlugin.WellSteamedHash) as SE_WellSteamed;
            if (live == null || live.m_icon == null)
            {
                return;
            }

            int tier = Mathf.Clamp(live.SaunaTier, 1, 4);

            // HUD does not expose a direct StatusEffect -> Image reference,
            // so locate the active Image reliably by the unique sprite of our effect.
            s_hudImages.Clear();
            __instance.GetComponentsInChildren<UnityEngine.UI.Image>(true, s_hudImages);
            foreach (UnityEngine.UI.Image image in s_hudImages)
            {
                if (image == null || !image.gameObject.activeInHierarchy || image.sprite != live.m_icon)
                {
                    continue;
                }

                GameObject badge = GetOrCreateBadge(image);
                if (badge == null)
                {
                    continue;
                }

                ApplyBadgeTransform(badge);

                TMPro.TMP_Text tierText = badge.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (tierText != null)
                {
                    tierText.text = tier.ToString();
                    // The vanilla MinLevel text is recolored red when a crafting
                    // station requirement is not met. Our clone must not inherit
                    // that transient UI state, so keep the sauna tier number white.
                    tierText.color = UnityEngine.Color.white;
                    tierText.raycastTarget = false;
                    tierText.gameObject.SetActive(true);
                }

                badge.SetActive(true);
            }
        }

        private static void HideTrackedBadges()
        {
            for (int i = s_badges.Count - 1; i >= 0; i--)
            {
                GameObject badge = s_badges[i];
                if (badge == null)
                {
                    s_badges.RemoveAt(i);
                    continue;
                }

                badge.SetActive(false);
            }
        }

        private static GameObject GetOrCreateBadge(UnityEngine.UI.Image statusIcon)
        {
            Transform existing = statusIcon.transform.Find(BadgeName);
            if (existing != null)
            {
                GameObject existingGo = existing.gameObject;
                if (!s_badges.Contains(existingGo))
                {
                    s_badges.Add(existingGo);
                }
                return existingGo;
            }

            UnityEngine.UI.Image sourceImage;
            TMPro.TMP_Text sourceText;
            if (!TryGetVanillaStationLevelUi(out sourceImage, out sourceText))
            {
                if (!s_loggedMissingSource)
                {
                    s_loggedMissingSource = true;
                    Jotunn.Logger.LogWarning("Well Steamed tier badge: vanilla MinLevel UI source not found");
                }
                return null;
            }

            GameObject badge = UnityEngine.Object.Instantiate(sourceImage.gameObject, statusIcon.transform, false);
            badge.name = BadgeName;
            badge.SetActive(true);

            ApplyBadgeTransform(badge);

            UnityEngine.UI.Image badgeImage = badge.GetComponent<UnityEngine.UI.Image>();
            if (badgeImage != null)
            {
                badgeImage.raycastTarget = false;
            }

            TMPro.TMP_Text badgeText = badge.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (badgeText != null)
            {
                badgeText.color = UnityEngine.Color.white;
                badgeText.raycastTarget = false;
                badgeText.gameObject.SetActive(true);
            }

            s_badges.Add(badge);

            if (!s_loggedSource)
            {
                s_loggedSource = true;
                Jotunn.Logger.LogInfo(
                    $"Well Steamed tier badge: vanilla source='{sourceImage.gameObject.name}', " +
                    $"sprite='{sourceImage.sprite?.name}', size={sourceImage.rectTransform.sizeDelta}, " +
                    $"text='{sourceText?.gameObject.name}'");
            }

            return badge;
        }

        private static void ApplyBadgeTransform(GameObject badge)
        {
            if (badge == null)
            {
                return;
            }

            RectTransform rt = badge.transform as RectTransform;
            if (rt == null)
            {
                return;
            }

            rt.anchorMin = new UnityEngine.Vector2(0f, 1f);
            rt.anchorMax = new UnityEngine.Vector2(0f, 1f);
            rt.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new UnityEngine.Vector2(BadgeOffsetX, BadgeOffsetY);
            rt.localRotation = UnityEngine.Quaternion.identity;
            rt.localScale = UnityEngine.Vector3.one * Mathf.Max(0.1f, BadgeScale);
        }

        private static bool TryGetVanillaStationLevelUi(
            out UnityEngine.UI.Image image,
            out TMPro.TMP_Text text)
        {
            image = null;
            text = null;

            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
            {
                return false;
            }

            // These fields were confirmed from a Valheim 1.0.12 dump:
            // Inventory_screen/root/Crafting/Decription/requirements/level/MinLevel
            // and its MinLevel/level_text child.
            System.Reflection.FieldInfo imageField =
                AccessTools.Field(typeof(InventoryGui), "m_minStationLevelIcon");
            System.Reflection.FieldInfo textField =
                AccessTools.Field(typeof(InventoryGui), "m_minStationLevelText");

            if (imageField != null)
            {
                image = imageField.GetValue(gui) as UnityEngine.UI.Image;
            }
            if (textField != null)
            {
                text = textField.GetValue(gui) as TMPro.TMP_Text;
            }

            return image != null && image.sprite != null && text != null;
        }
    }
}
