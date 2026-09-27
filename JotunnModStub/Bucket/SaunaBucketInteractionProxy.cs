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
    /// Interaction proxy lives on the SAME GameObject as the bucket collider.
    /// Valheim's hover/interact raycast can resolve interfaces on the hit object itself,
    /// while the real SaunaBucket state stays safely on the Piece root with its ZNetView.
    /// </summary>
    internal class SaunaBucketInteractionProxy : MonoBehaviour, Interactable, Hoverable
    {
        private SaunaBucket Target => GetComponentInParent<SaunaBucket>();

        public string GetHoverName()
        {
            SaunaBucket target = Target;
            if (target != null)
            {
                return target.GetHoverName();
            }

            return Localization.instance != null
                ? Localization.instance.Localize("$piece_sauna_bucket")
                : "$piece_sauna_bucket";
        }

        public string GetHoverText()
        {
            SaunaBucket target = Target;
            if (target != null)
            {
                return target.GetHoverText();
            }

            return Localization.instance != null
                ? Localization.instance.Localize("$piece_sauna_bucket")
                : "$piece_sauna_bucket";
        }

        public float GetHoverOffset()
        {
            SaunaBucket target = Target;
            return target != null ? target.GetHoverOffset() : 0f;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            SaunaBucket target = Target;
            return target != null && target.Interact(user, hold, alt);
        }

        public bool Interact(Humanoid user, bool hold)
        {
            return Interact(user, hold, false);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            SaunaBucket target = Target;
            return target != null && target.UseItem(user, item);
        }
    }
}
