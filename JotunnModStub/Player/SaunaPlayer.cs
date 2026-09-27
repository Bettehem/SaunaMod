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
    internal class SaunaPlayer : MonoBehaviour
    {
        public const string RpcName = "SaunaMod_Steamed";
        public const string MeadRpcName = "SaunaMod_Mead";

        private ZNetView m_nview;

        // Use Start instead of Awake so behavior does not depend on component order on the player prefab.
        private void Start()
        {
            m_nview = GetComponent<ZNetView>();

            if (m_nview != null && m_nview.IsValid())
            {
                m_nview.Register(RpcName, new Action<long>(RPC_Steamed));
                m_nview.Register<int, UnityEngine.Vector3>(
                    MeadRpcName,
                    new Action<long, int, UnityEngine.Vector3>(RPC_Mead));
            }
        }

        public void Broadcast()
        {
            if (m_nview != null && m_nview.IsValid())
            {
                m_nview.InvokeRPC(ZNetView.Everybody, RpcName);
            }
            else
            {
                RPC_Steamed(0L);
            }
        }

        private void RPC_Steamed(long sender)
        {
            SaunaPlugin.SpawnSteamVfxOn(gameObject);
        }

        public void GrantMeadToOwner(int type, UnityEngine.Vector3 stovePosition)
        {
            Player player = GetComponent<Player>();
            if (player == null)
            {
                return;
            }

            if (player == Player.m_localPlayer)
            {
                RPC_Mead(0L, type, stovePosition);
                return;
            }

            if (m_nview != null && m_nview.IsValid() && m_nview.GetZDO() != null)
            {
                long owner = m_nview.GetZDO().GetOwner();
                if (owner != 0L)
                {
                    m_nview.InvokeRPC(owner, MeadRpcName, type, stovePosition);
                }
            }
        }

        private void RPC_Mead(long sender, int type, UnityEngine.Vector3 stovePosition)
        {
            Player player = GetComponent<Player>();
            if (player == null || player != Player.m_localPlayer)
            {
                return;
            }

            if (UnityEngine.Vector3.Distance(player.transform.position, stovePosition) >
                SaunaMeadTuning.EffectRadius)
            {
                return;
            }

            SEMan seman = player.GetSEMan();
            if (seman == null || !seman.HaveStatusEffect(SEMan.s_statusEffectShelter))
            {
                return;
            }

            SaunaStove closest = SaunaStove.FindClosestForVisualLink(
                player.transform.position,
                SaunaMeadTuning.EffectRadius);
            if (closest == null ||
                UnityEngine.Vector3.Distance(closest.transform.position, stovePosition) > 0.75f)
            {
                return;
            }

            SaunaMeadSystem.ApplyToPlayer(player, type);
        }
    }
}
