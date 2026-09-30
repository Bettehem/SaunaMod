// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using UnityEngine;

namespace SaunaMod
{
    internal partial class SaunaPlugin
    {
        // 0 = normal skin, 1 = fully red. Only the local player's value is tracked here;
        // other clients see the result through the synchronized VisEquipment skin color.
        private static float _skinRedness;

        // Steam progress seen by the previous redness update, used to measure how much
        // steaming time the gameplay tick has counted since then.
        private float _skinLastSteamTime;
        private int _skinLastTimeTier;

        /// Without whisks the player can earn only the 5- and 10-minute tiers.
        /// Whisks (SaunaTier 2+) unlock the third 15-minute tier.
        private static int MaxEarnableTimeTier(int saunaTier)
        {
            return saunaTier >= 2
                ? WellSteamedTimeTiers.Length
                : Mathf.Min(2, WellSteamedTimeTiers.Length);
        }

        private void ResetSkinRedness()
        {
            _skinRedness = 0f;
            _skinLastSteamTime = 0f;
            _skinLastTimeTier = 0;
        }

        /// Runs at the start of each gameplay tick and reads the progress made by the previous one.
        /// Redness is tied to Well Steamed progress rather than to a separate clock: it reaches 1
        /// exactly when the maximum time tier available at this sauna is granted.
        private void UpdateSkinRedness(Player player, SEMan seman, float elapsed)
        {
            float progress;
            if (_timeTier == _skinLastTimeTier && _steamTime >= _skinLastSteamTime)
            {
                progress = _steamTime - _skinLastSteamTime;
            }
            else
            {
                // A grant reset the steam counter in between.
                progress = Mathf.Max(0f, SteamTimeToBuff - _skinLastSteamTime) + _steamTime;
            }

            _skinLastSteamTime = _steamTime;
            _skinLastTimeTier = _timeTier;

            if (seman.HaveStatusEffect(_steaming.NameHash()))
            {
                // No progress means the player is in the grace gap: keep the current color.
                if (progress > 0f)
                {
                    HeatSkin(player, seman, progress);
                }
            }
            else
            {
                _skinRedness = Mathf.Max(0f,
                    _skinRedness - elapsed / Mathf.Max(1f, SkinRednessTuning.CoolSeconds));
            }

            ApplySkinRedness(player);
        }

        private void HeatSkin(Player player, SEMan seman, float progress)
        {
            int maxTier = MaxEarnableTimeTier(
                SaunaStove.GetWellSteamedSaunaTierNear(player.transform.position));

            if (_timeTier >= maxTier)
            {
                _skinRedness = 1f;
                return;
            }

            // Mirror GrantWellSteamed: the next grant continues from the tier the active effect has.
            SE_WellSteamed active = seman.GetStatusEffect(WellSteamedHash) as SE_WellSteamed;
            int reached = Mathf.Max(_timeTier, active != null ? active.TimeTier : 0);
            int nextTier = Mathf.Clamp(Mathf.Max(reached, Mathf.Min(reached + 1, maxTier)),
                1, WellSteamedTimeTiers.Length);

            // Steaming seconds left until the max tier is granted.
            float remaining = Mathf.Max(0f, SteamTimeToBuff - _steamTime) +
                Mathf.Max(0, maxTier - nextTier) * SteamTimeToBuff;

            // Move linearly toward full redness so both arrive at the same moment,
            // also when the player comes back still partly red or with a tier already earned.
            _skinRedness += (1f - _skinRedness) * Mathf.Clamp01(progress / (remaining + progress));
        }

        private static float DisplayedSkinRedness()
        {
            return SaunaEditor.Active && SkinRednessTuning.Preview > 0 ? 1f : _skinRedness;
        }

        /// Writes the tinted color to VisEquipment only. Player.m_skinColor is left untouched,
        /// so the saved character keeps its original color and zero redness restores it exactly.
        /// VisEquipment stores the color in the player's ZDO, which syncs it to everyone else.
        internal static void ApplySkinRedness(Player player)
        {
            if (player == null || player.m_visEquipment == null)
            {
                return;
            }

            player.m_visEquipment.SetSkinColor(
                SkinRednessTuning.Apply(player.m_skinColor, DisplayedSkinRedness()));
        }
    }
}
