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
    /// Stores references to the liquid parts of one bucket instance.
    /// This lets the editor switch water/mead live
    /// and refresh buckets that are already placed in the world.
    /// </summary>
    internal class SaunaBucketLiquidVisual : MonoBehaviour
    {
        private static readonly HashSet<SaunaBucketLiquidVisual> Instances =
            new HashSet<SaunaBucketLiquidVisual>();

        [SerializeField] private MeshRenderer _waterSurface;
        [SerializeField] private MeshRenderer _infusionSurface;
        [SerializeField] private Renderer _mugRenderer;
        [SerializeField] private int _mugLiquidSlot = 1;

        private void Awake()
        {
            Instances.Add(this);
            ApplyPreview();
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
        }

        public void Configure(
            MeshRenderer waterSurface,
            MeshRenderer infusionSurface,
            Renderer mugRenderer,
            int mugLiquidSlot)
        {
            _waterSurface = waterSurface;
            _infusionSurface = infusionSurface;
            _mugRenderer = mugRenderer;
            _mugLiquidSlot = mugLiquidSlot;
            ApplyPreview();
        }

        /// <summary>
        /// Re-resolve the liquid renderers from the ACTUAL placed instance.
        /// Jotunn/Unity can clone a runtime-built Piece without preserving every
        /// private component reference exactly as it existed on the construction
        /// prefab. The gameplay ZDO can therefore be correct while the old cached
        /// renderers still point at the prefab (or are null). Names are unique inside
        /// our bucket model, so rebinding by hierarchy is deterministic.
        /// </summary>
        private void ResolveRuntimeReferences()
        {
            MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
            Renderer mug = null;

            foreach (MeshRenderer mr in meshRenderers)
            {
                if (mr == null)
                {
                    continue;
                }

                string objectName = mr.gameObject.name;
                if (objectName == "Cylinder")
                {
                    _waterSurface = mr;
                }
                else if (objectName == "Cylinder_low")
                {
                    _infusionSurface = mr;
                }
                else if (objectName == "fi_vil_container_ale_mug")
                {
                    Material[] mats = mr.sharedMaterials;
                    if (mats != null && mats.Length > _mugLiquidSlot)
                    {
                        mug = mr;
                    }
                }
            }

            if (mug != null)
            {
                _mugRenderer = mug;
            }
        }

        public void ApplyPreview()
        {
            // IMPORTANT: resolve against the live Piece every time. This makes the
            // visual independent of serialized/runtime prefab references.
            ResolveRuntimeReferences();
            SaunaPlugin.ApplyBucketLiquidTuningToMaterials();

            if (_waterSurface != null)
            {
                Material water = SaunaPlugin.GetBucketWaterMaterial();
                if (water != null)
                {
                    _waterSurface.sharedMaterial = water;
                }
                _waterSurface.enabled = true;
            }

            SaunaBucket bucket = GetComponent<SaunaBucket>();

            // Preview overrides the real contents only while the Bucket editor
            // section itself is selected. Everywhere else the live ZDO state wins.
            int preview = SaunaEditor.BucketLiquidPreviewActive
                ? Mathf.Clamp(SaunaBucketLiquidTuning.Preview, 0, 3)
                : bucket != null ? bucket.GetInfusionType() : 0;

            if (_infusionSurface != null)
            {
                if (preview == 0)
                {
                    _infusionSurface.enabled = false;
                }
                else
                {
                    Material mead = SaunaPlugin.GetBucketMeadMaterial(preview);
                    if (mead != null)
                    {
                        _infusionSurface.sharedMaterial = mead;
                        _infusionSurface.enabled = true;
                    }
                }
            }

            if (_mugRenderer != null)
            {
                Material[] mats = _mugRenderer.sharedMaterials;
                if (mats != null &&
                    _mugLiquidSlot >= 0 &&
                    _mugLiquidSlot < mats.Length)
                {
                    Material liquid = preview == 0
                        ? SaunaPlugin.GetBucketEmptyLiquidMaterial()
                        : SaunaPlugin.GetBucketMugMeadMaterial(preview);

                    if (liquid != null)
                    {
                        Material[] local = (Material[])mats.Clone();
                        local[_mugLiquidSlot] = liquid;
                        _mugRenderer.sharedMaterials = local;
                    }
                }
            }

            if (preview != 0 && (_infusionSurface == null || _mugRenderer == null))
            {
                Jotunn.Logger.LogWarning(
                    $"sauna bucket visual: infusion={preview}, " +
                    $"Cylinder_low={_infusionSurface != null}, mug={_mugRenderer != null}");
            }
        }

        public static void RefreshAll()
        {
            SaunaPlugin.ApplyBucketLiquidTuningToMaterials();

            // Use a copy because refreshing an icon may temporarily create or destroy
            // cloned prefabs and therefore change Instances during iteration.
            SaunaBucketLiquidVisual[] copy = new SaunaBucketLiquidVisual[Instances.Count];
            Instances.CopyTo(copy);

            foreach (SaunaBucketLiquidVisual visual in copy)
            {
                if (visual != null)
                {
                    visual.ApplyPreview();
                }
            }
        }
    }
}
