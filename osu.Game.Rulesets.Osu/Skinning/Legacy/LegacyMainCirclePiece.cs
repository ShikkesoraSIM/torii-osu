// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using System.Collections.Generic;

namespace osu.Game.Rulesets.Osu.Skinning.Legacy
{
    public partial class LegacyMainCirclePiece : CompositeDrawable
    {
        public override bool RemoveCompletedTransforms => false;

        /// <summary>
        /// A prioritised prefix to perform texture lookups with.
        /// </summary>
        private readonly string? priorityLookupPrefix;

        private readonly bool hasNumber;

        protected LegacyKiaiFlashingDrawable CircleSprite = null!;
        protected Sprite OverlaySprite = null!;

        protected Container OverlayLayer { get; private set; } = null!;

        private SkinnableSpriteText hitCircleText = null!;

        private readonly Bindable<Color4> accentColour = new Bindable<Color4>();
        private readonly IBindable<int> indexInCurrentCombo = new Bindable<int>();

        /// <summary>
        /// Torii: per-combo-colour hitcircle variants. When the player has
        /// shipped <c>hitcircle1.png</c> / <c>hitcircle2.png</c> / … alongside
        /// the standard <c>hitcircle.png</c>, the variant matching the active
        /// combo colour slot gets swapped in here. Driven by
        /// <see cref="comboIndexWithOffsets"/> below — see the doc-block on
        /// <see cref="updateCircleVariantTexture"/> for the lookup details.
        /// </summary>
        private readonly IBindable<int> comboIndexWithOffsets = new Bindable<int>();

        /// <summary>
        /// Max number of per-combo-colour hitcircle variants we look up. Matches
        /// the maximum number of <c>Combo1..ComboN</c> entries supported by
        /// legacy stable's skin.ini (8). Skins defining fewer combo colours
        /// simply never hit the higher-index variants — they're inert files,
        /// not errors.
        /// </summary>
        private const int max_combo_variant_slots = 8;

        /// <summary>
        /// Pre-resolved per-slot variant textures (index 0 = <c>hitcircle1.png</c>,
        /// index 1 = <c>hitcircle2.png</c>, …). Null entries mean the slot has no
        /// variant and falls back to <see cref="baseCircleTexture"/> at draw time.
        /// Populated once in <see cref="load"/> and frozen afterwards — combo-index
        /// changes only swap which slot is shown, never re-probe disk.
        /// </summary>
        private Texture?[]? circleVariantTextures;

        /// <summary>Sibling array of overlay variants — <c>hitcircleoverlay1.png</c> etc.</summary>
        private Texture?[]? overlayVariantTextures;

        /// <summary>
        /// Pre-sorted slot indices for which a circle variant actually exists, in ascending order.
        /// Used to cycle the lookup through available variants for combo slots that don't have
        /// their own variant — see <see cref="updateCircleVariantTexture"/> for the rules.
        /// Empty (length 0) when the skin ships no numbered variants at all, in which case the
        /// base <see cref="baseCircleTexture"/> is the fallback (= today's behaviour).
        /// </summary>
        private int[] circleVariantPresentSlots = Array.Empty<int>();

        /// <summary>Sibling array for the overlay layer.</summary>
        private int[] overlayVariantPresentSlots = Array.Empty<int>();

        /// <summary>
        /// Cached base textures (<c>hitcircle.png</c> / <c>hitcircleoverlay.png</c>).
        /// Used as the fallback ONLY when the skin shipped zero numbered variants — if the skin
        /// shipped at least one, missing slots cycle through the available variants instead of
        /// dropping back to the base (this lets a skin ship `hitcircle1.png` + `hitcircle2.png`
        /// and have Combo3..N reuse those two textures instead of mixing in the plain
        /// `hitcircle.png`, which was the original confusing default).
        /// </summary>
        private Texture? baseCircleTexture;
        private Texture? baseOverlayTexture;

        /// <summary>Cached combo-colour count for the fallback slot resolution. Defaults to <see cref="max_combo_variant_slots"/> when the skin hasn't declared any.</summary>
        private int comboColourCount = max_combo_variant_slots;

        /// <summary>
        /// Torii: cached snapshot of the active skin's combo-colour list at load time.
        /// Used by <see cref="findSlotForColour"/> to map the engine-resolved
        /// <see cref="accentColour"/> back to its slot index in this skin, so the
        /// variant texture pairs 1:1 with whatever colour the engine actually picked
        /// (regardless of whether the engine internally cycled by ComboIndex or
        /// ComboIndexWithOffsets — those two diverge in maps that use "new combo +
        /// skip N" hit-object flags, which was the cause of the original desync
        /// where hitcircle1 ended up paired with combo colour 2/3/…).
        /// </summary>
        private IReadOnlyList<Color4>? cachedSkinComboColours;

        [Resolved(canBeNull: true)] // Can't really be null but required to handle potential of disposal before DI completes.
        private DrawableHitObject? drawableObject { get; set; }

        [Resolved]
        private ISkinSource skin { get; set; } = null!;

        [Resolved]
        private OsuRulesetConfigManager? osuConfig { get; set; }

        public LegacyMainCirclePiece(string? priorityLookupPrefix = null, bool hasNumber = true)
        {
            this.priorityLookupPrefix = priorityLookupPrefix;
            this.hasNumber = hasNumber;

            Size = OsuHitObject.OBJECT_DIMENSIONS;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            const string base_lookup = @"hitcircle";

            var drawableOsuObject = (DrawableOsuHitObject?)drawableObject;

            // As a precondition, prefer that any *prefix* lookups are run against the skin which is providing "hitcircle".
            // This is to correctly handle a case such as:
            //
            // - Beatmap provides `hitcircle`
            // - User skin provides `sliderstartcircle`
            //
            // In such a case, the `hitcircle` should be used for slider start circles rather than the user's skin override.
            //
            // Of note, this consideration should only be used to decide whether to continue looking up the prefixed name or not.
            // The final lookups must still run on the full skin hierarchy as per usual in order to correctly handle fallback cases.
            var provider = skin.FindProvider(s => s.GetTexture(base_lookup) != null) ?? skin;

            // if a base texture for the specified prefix exists, continue using it for subsequent lookups.
            // otherwise fall back to the default prefix "hitcircle".
            string circleName = (priorityLookupPrefix != null && provider.GetTexture(priorityLookupPrefix) != null) ? priorityLookupPrefix : base_lookup;

            Vector2 maxSize = OsuHitObject.OBJECT_DIMENSIONS * 2;

            // Torii: pre-resolve the base + variant textures upfront. Variants are
            // `{circleName}{1..8}.png` (1-indexed to match skin.ini's Combo1..N).
            // Each lookup goes through the normal skin hierarchy so @2x / per-beatmap
            // skins / per-skin fallbacks all work transparently. If a slot has no
            // variant the array entry stays null and updateCircleVariantTexture()
            // falls back to the base texture. Lookup happens ONCE per piece
            // construction — the pool reuses the same piece across hit objects, so
            // combo-index changes only mutate which already-resolved texture is shown.
            baseCircleTexture = skin.GetTexture(circleName)?.WithMaximumSize(maxSize);
            baseOverlayTexture = skin.GetTexture(@$"{circleName}overlay")?.WithMaximumSize(maxSize);
            circleVariantTextures = probeComboVariants(circleName, maxSize);
            overlayVariantTextures = probeComboVariants(@$"{circleName}overlay", maxSize);
            circleVariantPresentSlots = collectPresentSlots(circleVariantTextures);
            overlayVariantPresentSlots = collectPresentSlots(overlayVariantTextures);
            cachedSkinComboColours = skin.GetConfig<GlobalSkinColours, IReadOnlyList<Color4>>(GlobalSkinColours.ComboColours)?.Value;
            comboColourCount = Math.Max(1, cachedSkinComboColours?.Count ?? max_combo_variant_slots);

            // at this point, any further texture fetches should be correctly using the priority source if the base texture was retrieved using it.
            // the conditional above handles the case where a sliderendcircle.png is retrieved from the skin, but sliderendcircleoverlay.png doesn't exist.
            // expected behaviour in this scenario is not showing the overlay, rather than using hitcircleoverlay.png.
            InternalChildren = new[]
            {
                CircleSprite = new LegacyKiaiFlashingDrawable(() => new Sprite { Texture = baseCircleTexture })
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                },
                OverlayLayer = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Child = OverlaySprite = new Sprite
                    {
                        Texture = baseOverlayTexture,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                },
                CircleSprite.FlashingDrawable.CreateProxy(),
            };

            if (hasNumber)
            {
                OverlayLayer.Add(hitCircleText = new SkinnableSpriteText(new OsuSkinComponentLookup(OsuSkinComponents.HitCircleText), _ => new OsuSpriteText
                {
                    Font = OsuFont.Numeric.With(size: 40),
                    UseFullGlyphHeight = false,
                }, confineMode: ConfineMode.NoScaling)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                });
            }

            bool overlayAboveNumber = skin.GetConfig<OsuSkinConfiguration, bool>(OsuSkinConfiguration.HitCircleOverlayAboveNumber)?.Value ?? true;

            if (overlayAboveNumber)
                OverlayLayer.ChangeChildDepth(OverlaySprite, float.MinValue);

            if (drawableOsuObject != null)
            {
                accentColour.BindTo(drawableOsuObject.AccentColour);
                indexInCurrentCombo.BindTo(drawableOsuObject.IndexInCurrentComboBindable);
                // Torii: bind to combo-index-with-offsets so the per-combo variant
                // texture follows whichever colour slot is active. The bind happens
                // here (load-time, before LoadComplete) so the BindValueChanged
                // handler in LoadComplete fires with the initial value.
                comboIndexWithOffsets.BindTo(drawableOsuObject.ComboIndexWithOffsetsBindable);
            }
        }

        /// <summary>
        /// Torii: probe disk for <c>{prefix}1.png</c> through <c>{prefix}{max_combo_variant_slots}.png</c>
        /// and return a fixed-size array of resolved textures. Missing files stay null;
        /// <see cref="collectPresentSlots"/> turns the array into a compact list of
        /// "which slots actually shipped a variant" for the cycling fallback logic in
        /// <see cref="updateCircleVariantTexture"/>. Each lookup goes through
        /// <see cref="skin"/>.GetTexture so @2x / per-beatmap-skin / fallback chains
        /// all work normally.
        /// </summary>
        private Texture?[] probeComboVariants(string prefix, Vector2 maxSize)
        {
            var variants = new Texture?[max_combo_variant_slots];
            for (int i = 0; i < max_combo_variant_slots; i++)
                variants[i] = skin.GetTexture(@$"{prefix}{i + 1}")?.WithMaximumSize(maxSize);
            return variants;
        }

        /// <summary>
        /// Torii: return the slot indices (0-based) for which <paramref name="variants"/>
        /// has a non-null entry, in ascending order. Used to cycle the variant lookup
        /// through ONLY the slots a skin actually shipped — see
        /// <see cref="updateCircleVariantTexture"/> for the rules.
        /// Returns an empty array (not null) when no variants shipped, which triggers
        /// the base-texture fallback path. Result length is ≤ <see cref="max_combo_variant_slots"/>.
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

        /// <summary>
        /// Torii: pick the right hitcircle / hitcircleoverlay texture for the active
        /// combo colour slot and push it into <see cref="CircleSprite"/> +
        /// <see cref="OverlaySprite"/>.
        /// <para>
        /// Slot index for the active hit object is
        /// <c>ComboIndexWithOffsets mod ComboColourCount</c> — identical to the modulo
        /// the skin lookup applies when picking the AccentColour itself
        /// (see <c>ArgonSkin.getComboColour</c> / <c>TrianglesSkin.getComboColour</c>),
        /// so the variant always pairs 1:1 with whatever colour the engine resolved
        /// for this hit object.
        /// </para>
        /// <para>
        /// Texture resolution order, per layer (circle and overlay handled identically):
        /// </para>
        /// <list type="number">
        /// <item><description>If the variant at the active slot is non-null → use it.</description></item>
        /// <item><description>Else, if the skin shipped ≥1 variant anywhere → cycle through the present-slots list:
        /// <c>variants[presentSlots[activeSlot mod presentSlots.Length]]</c>. This makes a skin shipping only
        /// <c>hitcircle1.png</c> + <c>hitcircle2.png</c> against a 4-colour <c>skin.ini</c> reuse those two textures
        /// for Combo3 + Combo4 instead of dropping back to plain <c>hitcircle.png</c>.</description></item>
        /// <item><description>Else (no variants shipped at all) → fall back to <see cref="baseCircleTexture"/> /
        /// <see cref="baseOverlayTexture"/>. This is the today-behaviour preserved unchanged for skins that
        /// never opted in to numbered variants.</description></item>
        /// </list>
        /// <para>
        /// The texture is still tinted via AccentColour by the CircleSprite.Colour assignment in
        /// LoadComplete — variant authors paint in white/greyscale to let the combo colour show
        /// through, or in their own pre-tinted colour and accept the additional combo-colour
        /// multiply on top.
        /// </para>
        /// </summary>
        private void updateCircleVariantTexture()
        {
            if (circleVariantTextures == null || overlayVariantTextures == null)
                return;

            // Torii: pick the variant slot from the engine-resolved AccentColour
            // rather than from `ComboIndexWithOffsets % comboColourCount`.
            //
            // The previous index-based path silently desync'd in two real-world
            // configurations:
            //
            // 1. Default skins (Argon / Triangles / LegacySkin without a beatmap-skin
            //    override) resolve combo colours via `ComboIndex`, NOT
            //    `ComboIndexWithOffsets`. The two diverge on any map that uses
            //    "new combo + skip N" hit-object flags (a common choreography
            //    trick), so our `% comboColourCount` slot would land on a different
            //    colour than the engine actually rendered.
            //
            // 2. When a beatmap ships its own `[Colours]` block, `LegacyBeatmapSkin`
            //    intercepts colour resolution and uses the BEATMAP's colour list,
            //    while our `comboColourCount` is read from `GlobalSkinColours.ComboColours`
            //    which can resolve to a different list.
            //
            // Driving from AccentColour sidesteps both problems: the colour stored
            // there has already been through the full engine resolution chain,
            // including beatmap skin overrides + whichever ComboIndex variant the
            // active skin uses internally. We just have to find that colour in
            // our cached skin colour list to recover the slot — and fall back to
            // index-cycling when the engine picked a colour from a source we
            // don't have cached (e.g. beatmap-overridden colours not present in
            // the user skin's `[Colours]`).
            int slot = findSlotForColour(accentColour.Value);

            CircleSprite.SetTexture(resolveVariant(slot, circleVariantTextures, circleVariantPresentSlots, baseCircleTexture));
            OverlaySprite.Texture = resolveVariant(slot, overlayVariantTextures, overlayVariantPresentSlots, baseOverlayTexture);
        }

        /// <summary>
        /// Torii: find the slot index for the supplied combo colour by matching it
        /// against the cached skin combo-colour list. Returns the matching index
        /// when found, otherwise falls back to the legacy index-cycling path so
        /// behaviour is preserved for colours sourced from outside the skin
        /// (e.g. beatmap-overridden colours not in the active skin's
        /// <c>[Colours]</c> section).
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

            // Fallback: defensive normalise of the modulo result (C#'s `%` is
            // sign-preserving, ComboIndexWithOffsets should be ≥ 0 by
            // construction but the guard is cheap).
            return ((comboIndexWithOffsets.Value % comboColourCount) + comboColourCount) % comboColourCount;
        }

        /// <summary>
        /// Torii: tolerant Color4 comparison — the engine passes through colour
        /// values from the skin / beatmap configuration verbatim, but a small
        /// epsilon guards against float-precision noise from any
        /// LegacyColourCompatibility / DisallowZeroAlpha rounding that may occur
        /// further along the pipeline.
        /// </summary>
        private static bool coloursApproxEqual(Color4 a, Color4 b)
            => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 0.01f;

        /// <summary>
        /// Torii: resolve the texture for a given combo slot via the three-stage chain
        /// documented on <see cref="updateCircleVariantTexture"/>. Pulled out so circle
        /// and overlay layers share one implementation.
        /// </summary>
        private static Texture? resolveVariant(int slot, Texture?[] variants, int[] presentSlots, Texture? fallbackBase)
        {
            // Stage 1: this slot has its own variant.
            if (slot < variants.Length && variants[slot] != null)
                return variants[slot];

            // Stage 2: skin shipped ≥1 variant somewhere — cycle through the present
            // slots so Combo{N} (N > number of shipped variants) loops back to the
            // first one instead of falling through to the base.
            if (presentSlots.Length > 0)
                return variants[presentSlots[slot % presentSlots.Length]];

            // Stage 3: skin shipped zero numbered variants. Original pre-feature
            // behaviour: just use the base hitcircle.png / hitcircleoverlay.png.
            return fallbackBase;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            accentColour.BindValueChanged(colour =>
            {
                Color4 objectColour = colour.NewValue;
                int add = Math.Max(25, 300 - (int)(objectColour.R * 255) - (int)(objectColour.G * 255) - (int)(objectColour.B * 255));

                var kiaiTintColour = new Color4(
                    (byte)Math.Min((byte)(objectColour.R * 255) + add, 255),
                    (byte)Math.Min((byte)(objectColour.G * 255) + add, 255),
                    (byte)Math.Min((byte)(objectColour.B * 255) + add, 255),
                    255);

                CircleSprite.Colour = LegacyColourCompatibility.DisallowZeroAlpha(colour.NewValue);
                CircleSprite.KiaiGlowColour = LegacyColourCompatibility.DisallowZeroAlpha(kiaiTintColour);
            }, true);

            if (hasNumber)
                indexInCurrentCombo.BindValueChanged(index => hitCircleText.Text = (index.NewValue + 1).ToString(), true);

            // Torii: swap to the per-combo variant texture for the active slot.
            // Fires once with the initial value (because BindValueChanged with
            // initial:true), and again on every ComboIndexWithOffsetsBindable change
            // — including pool reuse, which rebinds this through DrawableOsuHitObject.
            comboIndexWithOffsets.BindValueChanged(_ => updateCircleVariantTexture(), true);

            if (drawableObject != null)
            {
                drawableObject.ApplyCustomUpdateState += updateStateTransforms;
                updateStateTransforms(drawableObject, drawableObject.State.Value);
            }
        }

        private void updateStateTransforms(DrawableHitObject drawableHitObject, ArmedState state)
        {
            const double legacy_fade_duration = 240;

            using (BeginAbsoluteSequence(drawableObject.AsNonNull().HitStateUpdateTime))
            {
                switch (state)
                {
                    case ArmedState.Hit:
                        CircleSprite.FadeOut(legacy_fade_duration);
                        CircleSprite.ScaleTo(1.4f, legacy_fade_duration, Easing.Out);

                        OverlaySprite.FadeOut(legacy_fade_duration);
                        OverlaySprite.ScaleTo(1.4f, legacy_fade_duration, Easing.Out);

                        if (hasNumber)
                        {
                            decimal? legacyVersion = skin.GetConfig<SkinConfiguration.LegacySetting, decimal>(SkinConfiguration.LegacySetting.Version)?.Value;

                            if (legacyVersion > 1.0m)
                            {
                                // legacy skins of version 2.0 and newer only apply very short fade out to the number piece.
                                //
                                // if the new hit animation setting is disabled, the fade is bypassed here to avoid users abusing this to achieve "even better" results.
                                // note that this means the number fades slightly slower than other components when hit animations are off.
                                // in practice, the fade is so short this is not perceivable.
                                if (osuConfig?.Get<bool>(OsuRulesetSetting.HitAnimations) != false)
                                    hitCircleText.FadeOut(legacy_fade_duration / 4);
                            }
                            else
                            {
                                // old skins scale and fade it normally along other pieces.
                                hitCircleText.FadeOut(legacy_fade_duration);
                                hitCircleText.ScaleTo(1.4f, legacy_fade_duration, Easing.Out);
                            }
                        }

                        break;
                }
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (drawableObject != null)
                drawableObject.ApplyCustomUpdateState -= updateStateTransforms;
        }
    }
}
