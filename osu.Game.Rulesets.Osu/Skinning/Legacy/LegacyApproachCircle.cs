// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Osu.Skinning.Legacy
{
    public partial class LegacyApproachCircle : Sprite
    {
        [Resolved]
        private DrawableHitObject drawableObject { get; set; } = null!;

        private IBindable<Color4> accentColour = null!;

        /// <summary>Torii: see LegacyMainCirclePiece doc-block — same per-combo variant model, applied to approachcircle.</summary>
        private const int max_combo_variant_slots = 8;

        /// <summary>Resolved <c>approachcircle1..8.png</c> entries. Null entries mean the slot didn't ship; <see cref="approachVariantPresentSlots"/> tracks which ones did so the cycling fallback can skip the gaps.</summary>
        private Texture?[]? approachVariantTextures;

        /// <summary>Ascending-order list of slot indices that have a variant. Empty = no variants shipped (skin keeps using the plain approachcircle.png the way it always has).</summary>
        private int[] approachVariantPresentSlots = Array.Empty<int>();

        /// <summary>Cached base approachcircle.png — only used as fallback when the skin shipped zero numbered variants (preserves today's behaviour for skins that didn't opt in).</summary>
        private Texture? baseApproachTexture;

        /// <summary>Combo-colour count for the fallback slot resolution path. Defaults to <see cref="max_combo_variant_slots"/> when the skin didn't declare any.</summary>
        private int comboColourCount = max_combo_variant_slots;

        /// <summary>
        /// Torii: cached snapshot of the active skin's combo-colour list at load time.
        /// Used by <see cref="findSlotForColour"/> to map the engine-resolved
        /// <see cref="accentColour"/> back to its slot index in this skin. See the
        /// long comment on <see cref="LegacyMainCirclePiece.updateCircleVariantTexture"/>
        /// for why this is necessary — same desync, same fix.
        /// </summary>
        private IReadOnlyList<Color4>? cachedSkinComboColours;

        /// <summary>Locally-bound combo-index used by the fallback path in <see cref="findSlotForColour"/>.</summary>
        private readonly IBindable<int> comboIndexWithOffsets = new Bindable<int>();

        [BackgroundDependencyLoader]
        private void load(ISkinSource skin)
        {
            var maxSize = OsuHitObject.OBJECT_DIMENSIONS * 2;

            baseApproachTexture = skin.GetTexture(@"approachcircle")?.WithMaximumSize(maxSize);
            Debug.Assert(baseApproachTexture != null);

            // Torii: probe per-combo variants (approachcircle1.png..approachcircle8.png).
            // Same model as the hitcircle variants (see LegacyMainCirclePiece): missing
            // slots aren't an error — the cycling fallback in updateVariantTexture()
            // wraps through the slots that DO exist, so a skin shipping
            // approachcircle1 + approachcircle2 against a 4-colour skin.ini reuses
            // those two textures for Combo3/4 instead of dropping back to plain
            // approachcircle.png. @2x / per-beatmap-skin / fallback chains all flow
            // through skin.GetTexture as usual — no extra plumbing for HD textures.
            approachVariantTextures = new Texture?[max_combo_variant_slots];
            for (int i = 0; i < max_combo_variant_slots; i++)
                approachVariantTextures[i] = skin.GetTexture(@$"approachcircle{i + 1}")?.WithMaximumSize(maxSize);

            approachVariantPresentSlots = collectPresentSlots(approachVariantTextures);

            cachedSkinComboColours = skin.GetConfig<GlobalSkinColours, IReadOnlyList<Color4>>(GlobalSkinColours.ComboColours)?.Value;
            comboColourCount = Math.Max(1, cachedSkinComboColours?.Count ?? max_combo_variant_slots);

            // Default texture is the base approachcircle.png; updateVariantTexture()
            // in LoadComplete will swap to the appropriate variant once the bindable
            // settles. We assign here so the sprite isn't briefly null between
            // load() and the first BindValueChanged tick.
            Texture = baseApproachTexture;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            accentColour = drawableObject.AccentColour.GetBoundCopy();
            accentColour.BindValueChanged(colour => Colour = LegacyColourCompatibility.DisallowZeroAlpha(colour.NewValue), true);

            // Torii: bind to the same ComboIndexWithOffsetsBindable LegacyMainCirclePiece
            // uses. When it fires (load-time-initial via BindValueChanged(initial:true),
            // and again on every pool reuse via DrawableOsuHitObject.OnApply), pick the
            // right variant + push it into Texture.
            if (drawableObject is DrawableOsuHitObject osuObject)
            {
                comboIndexWithOffsets.BindTo(osuObject.ComboIndexWithOffsetsBindable);
                comboIndexWithOffsets.BindValueChanged(_ => updateVariantTexture(), true);
            }
        }

        /// <summary>
        /// Torii: identical resolution chain to <see cref="LegacyMainCirclePiece"/>:
        /// (1) variant for this slot exists → use it; (2) skin shipped ≥1 variant
        /// anywhere → cycle through the present slots; (3) no numbered variants at
        /// all → fall back to <see cref="baseApproachTexture"/>. The combo-colour
        /// tint applies via the accentColour binding above regardless of which
        /// branch resolved the texture.
        /// </summary>
        private void updateVariantTexture()
        {
            if (approachVariantTextures == null)
                return;

            // Torii: derive the slot from the engine-resolved AccentColour rather
            // than from ComboIndexWithOffsets. See the equivalent comment block
            // on LegacyMainCirclePiece.updateCircleVariantTexture for why — same
            // desync (default skins resolve by ComboIndex while we were keying off
            // ComboIndexWithOffsets, and beatmap-skin colour overrides aren't
            // visible in our cached GlobalSkinColours list), same fix.
            int slot = findSlotForColour(accentColour.Value);

            Texture? resolved;
            if (slot < approachVariantTextures.Length && approachVariantTextures[slot] != null)
                resolved = approachVariantTextures[slot];
            else if (approachVariantPresentSlots.Length > 0)
                resolved = approachVariantTextures[approachVariantPresentSlots[slot % approachVariantPresentSlots.Length]];
            else
                resolved = baseApproachTexture;

            if (resolved != null)
                Texture = resolved;
        }

        /// <summary>
        /// Torii: find the slot index for the supplied combo colour by matching it
        /// against the cached skin combo-colour list. Mirrors
        /// <see cref="LegacyMainCirclePiece"/>'s findSlotForColour so the approach
        /// circle and the hit circle always agree on which variant index pairs
        /// with which colour. Falls back to index-cycling for colours sourced
        /// from outside the active skin (e.g. beatmap-overridden colours).
        /// </summary>
        private int findSlotForColour(Color4 colour)
        {
            if (cachedSkinComboColours != null)
            {
                for (int i = 0; i < cachedSkinComboColours.Count; i++)
                {
                    if (coloursApproxEqual(cachedSkinComboColours[i], colour))
                        return i;
                }
            }

            return ((comboIndexWithOffsets.Value % comboColourCount) + comboColourCount) % comboColourCount;
        }

        /// <summary>Torii: tolerant Color4 equality for the AccentColour → slot lookup. Same epsilon as in LegacyMainCirclePiece.</summary>
        private static bool coloursApproxEqual(Color4 a, Color4 b)
            => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 0.01f;

        /// <summary>
        /// Torii: collapse a sparse variant array into ascending-ordered indices of
        /// non-null entries. Empty array = "no variants shipped" (cycling fallback
        /// is bypassed; base texture is used instead).
        /// </summary>
        private static int[] collectPresentSlots(Texture?[] variants)
        {
            int count = 0;
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] != null)
                    count++;
            }

            if (count == 0)
                return Array.Empty<int>();

            var present = new int[count];
            int writeIndex = 0;
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] != null)
                    present[writeIndex++] = i;
            }
            return present;
        }
    }
}
