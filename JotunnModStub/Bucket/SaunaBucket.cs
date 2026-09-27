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
    /// Gameplay state of the sauna bucket. Mead type is stored in ZDO,
    /// so it survives world saves and synchronizes between clients.
    /// </summary>
    internal class SaunaBucket : MonoBehaviour, Interactable, Hoverable
    {
        public const string ZdoInfusion = "sauna_infusion";

        private static readonly HashSet<SaunaBucket> Instances =
            new HashSet<SaunaBucket>();

        private ZNetView m_nview;
        private SaunaBucketLiquidVisual m_visual;
        private int m_lastVisualType = -1;
        private float m_poll;

        private void Awake()
        {
            Instances.Add(this);
            m_nview = GetComponent<ZNetView>();
            m_visual = GetComponent<SaunaBucketLiquidVisual>();
        }

        private void Start()
        {
            RefreshVisual(true);
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
        }

        private void Update()
        {
            m_poll -= Time.deltaTime;
            if (m_poll > 0f)
            {
                return;
            }

            m_poll = 0.25f;
            RefreshVisual(false);
        }

        public int GetInfusionType()
        {
            if (m_nview == null || !m_nview.IsValid() || m_nview.GetZDO() == null)
            {
                return 0;
            }

            return Mathf.Clamp(m_nview.GetZDO().GetInt(ZdoInfusion, 0), 0, 3);
        }

        private void SetInfusionType(int type)
        {
            type = Mathf.Clamp(type, 0, 3);

            if (m_nview == null || !m_nview.IsValid() || m_nview.GetZDO() == null)
            {
                return;
            }

            if (!m_nview.IsOwner())
            {
                m_nview.ClaimOwnership();
            }

            m_nview.GetZDO().Set(ZdoInfusion, type);
            m_lastVisualType = -1;
            RefreshVisual(true);
        }

        private void RefreshVisual(bool force)
        {
            int type = GetInfusionType();
            if (!force && type == m_lastVisualType)
            {
                return;
            }

            m_lastVisualType = type;
            if (m_visual == null)
            {
                m_visual = GetComponent<SaunaBucketLiquidVisual>();
            }

            if (m_visual != null)
            {
                m_visual.ApplyPreview();
            }
        }

        private static string LocalizeHover(string text)
        {
            return Localization.instance != null
                ? Localization.instance.Localize(text)
                : text;
        }

        public string GetHoverName()
        {
            return LocalizeHover("$piece_sauna_bucket");
        }

        public float GetHoverOffset()
        {
            return 0f;
        }

        public string GetHoverText()
        {
            int infusion = GetInfusionType();
            if (infusion != SaunaMeadSystem.None)
            {
                string bucketName = LocalizeHover("$piece_sauna_bucket");
                string contains = LocalizeHover("$msg_sauna_bucket_contains");
                return bucketName + "\n" + contains + ": " +
                    SaunaMeadSystem.LocalizedName(infusion);
            }

            // Hoverable.GetHoverText is rendered as-is. Unlike many vanilla Piece
            // paths, this custom proxy is not localized again by the HUD, so resolve
            // every token here (including the vanilla $KEY_Use token).
            return LocalizeHover(
                "$piece_sauna_bucket\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] $msg_sauna_bucket_use_mead");
        }

        // E is useful even without a special item-targeting gesture: if the player
        // carries only one supported resistance-mead TYPE, use one bottle directly.
        // If several different types are present, do not guess — the player chooses
        // one by using that mead from the hotbar while aiming at the bucket.
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user == null)
            {
                return false;
            }

            if (GetInfusionType() != SaunaMeadSystem.None)
            {
                user.Message(MessageHud.MessageType.Center, "$msg_sauna_bucket_full");
                return true;
            }

            bool multipleTypes;
            ItemDrop.ItemData onlyMead = SaunaMeadSystem.FindSingleAvailableMead(
                user, out multipleTypes);

            if (onlyMead != null)
            {
                return TryUseMead(user, onlyMead);
            }

            user.Message(
                MessageHud.MessageType.Center,
                multipleTypes ? "$msg_sauna_bucket_choose_mead" : "$msg_sauna_bucket_no_mead");
            return true;
        }

        public bool Interact(Humanoid user, bool hold)
        {
            return Interact(user, hold, false);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return TryUseMead(user, item);
        }

        public bool TryUseMead(Humanoid user, ItemDrop.ItemData item)
        {
            if (user == null || item == null)
            {
                return false;
            }

            int type = SaunaMeadSystem.GetType(item);
            if (type == SaunaMeadSystem.None)
            {
                if (SaunaMeadSystem.IsMeadLike(item))
                {
                    user.Message(MessageHud.MessageType.Center, "$msg_sauna_bucket_resistance_only");
                    return true;
                }

                return false;
            }

            if (GetInfusionType() != SaunaMeadSystem.None)
            {
                user.Message(MessageHud.MessageType.Center, "$msg_sauna_bucket_full");
                return true;
            }

            if (!user.GetInventory().RemoveItem(item, 1))
            {
                return false;
            }

            SetInfusionType(type);

            string fmt = Localization.instance != null
                ? Localization.instance.Localize("$msg_sauna_bucket_filled")
                : "Added {0} to the sauna bucket";
            user.Message(
                MessageHud.MessageType.Center,
                string.Format(fmt, SaunaMeadSystem.LocalizedName(type)));

            Jotunn.Logger.LogInfo(
                $"sauna bucket: infused type={type} by '{(user is Player p ? p.GetPlayerName() : user.name)}'");
            return true;
        }

        public static int ConsumeLinkedInfusion(SaunaStove stove)
        {
            if (stove == null || stove.GetWellSteamedSaunaTier() < 3)
            {
                return SaunaMeadSystem.None;
            }

            SaunaBucket best = null;
            float bestDistance = float.MaxValue;

            foreach (SaunaBucket bucket in Instances)
            {
                if (bucket == null)
                {
                    continue;
                }

                int type = bucket.GetInfusionType();
                if (type == SaunaMeadSystem.None)
                {
                    continue;
                }

                SaunaStove linked = SaunaStove.FindClosestForVisualLink(
                    bucket.transform.position,
                    SaunaVisualLink.MaxLinkDistance);

                if (linked != stove)
                {
                    continue;
                }

                float distance = UnityEngine.Vector3.Distance(
                    bucket.transform.position,
                    stove.transform.position);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = bucket;
                }
            }

            if (best == null)
            {
                return SaunaMeadSystem.None;
            }

            int infusion = best.GetInfusionType();
            best.SetInfusionType(SaunaMeadSystem.None);
            return infusion;
        }
    }
}
