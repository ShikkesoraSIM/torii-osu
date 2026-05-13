// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
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
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

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
        protected LegacyKiaiFlashingDrawable OverlaySprite = null!;

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
        /// Cached base textures (<c>hitcircle.png</c> / <c>hitcircleoverlay.png</c>).
        /// Used as the fallback whenever the active slot's variant is null.
        /// </summary>
        private Texture? baseCircleTexture;
        private Texture? baseOverlayTexture;

        /// <summary>Cached combo-colour count for slot resolution. Defaults to <see cref="max_combo_variant_slots"/> when the skin hasn't declared any.</summary>
        private int comboColourCount = max_combo_variant_slots;

        [Resolved(canBeNull: true)] // Can't really be null but required to handle potential of disposal before DI completes.
        private DrawableHitObject? drawableObject { get; set; }

        [Resolved]
        private ISkinSource skin { get; set; } = null!;

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
            comboColourCount = Math.Max(1,
                skin.GetConfig<GlobalSkinColours, IReadOnlyList<Color4>>(GlobalSkinColours.ComboColours)?.Value?.Count
                ?? max_combo_variant_slots);

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
                    Child = OverlaySprite = new LegacyKiaiFlashingDrawable(() => new Sprite { Texture = baseOverlayTexture })
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                }
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
        /// and return a fixed-size array of resolved textures. Missing files stay null
        /// and the consumer (see <see cref="updateCircleVariantTexture"/>) falls back to
        /// the base prefix's texture. Each lookup goes through <see cref="skin"/>.GetTexture
        /// so @2x / per-beatmap-skin / fallback chains all work normally.
        /// </summary>
        private Texture?[] probeComboVariants(string prefix, Vector2 maxSize)
        {
            var variants = new Texture?[max_combo_variant_slots];
            for (int i = 0; i < max_combo_variant_slots; i++)
                variants[i] = skin.GetTexture(@$"{prefix}{i + 1}")?.WithMaximumSize(maxSize);
            return variants;
        }

        /// <summary>
        /// Torii: pick the right hitcircle / hitcircleoverlay texture for the active
        /// combo colour slot and push it into <see cref="CircleSprite"/> +
        /// <see cref="OverlaySprite"/>. Slot index is
        /// <c>ComboIndexWithOffsets mod ComboColourCount</c> — identical to the modulo
        /// the skin lookup applies when picking the AccentColour itself
        /// (see <c>ArgonSkin.getComboColour</c> / <c>TrianglesSkin.getComboColour</c>),
        /// so the variant always pairs 1:1 with whatever colour the engine resolved
        /// for this hit object. The texture is still tinted via AccentColour by the
        /// CircleSprite.Colour assignment in LoadComplete — variant authors paint in
        /// white/greyscale to let the combo colour show through, or in their own
        /// pre-tinted colour and accept the additional combo-colour multiply on top.
        /// </summary>
        private void updateCircleVariantTexture()
        {
            if (circleVariantTextures == null || overlayVariantTextures == null)
                return;

            // Normalise negative results (defensive — ComboIndexWithOffsets should be
            // ≥0 by construction, but C#'s `%` is sign-preserving so we guard).
            int slot = ((comboIndexWithOffsets.Value % comboColourCount) + comboColourCount) % comboColourCount;

            Texture? circleVariant = slot < circleVariantTextures.Length ? circleVariantTextures[slot] : null;
            Texture? overlayVariant = slot < overlayVariantTextures.Length ? overlayVariantTextures[slot] : null;

            // Variant present → swap to it. Variant null → fall back to base, which
            // preserves today's behaviour for any skin that hasn't shipped numbered
            // variants. CircleSprite/OverlaySprite always reflect the right texture
            // even after pooling reuse (this is called on every comboIndexWithOffsets
            // change, including the initial bind in LoadComplete).
            CircleSprite.SetTexture(circleVariant ?? baseCircleTexture);
            OverlaySprite.SetTexture(overlayVariant ?? baseOverlayTexture);
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
                OverlaySprite.KiaiGlowColour = CircleSprite.KiaiGlowColour = LegacyColourCompatibility.DisallowZeroAlpha(kiaiTintColour);
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
                                // legacy skins of version 2.0 and newer only apply very short fade out to the number piece.
                                hitCircleText.FadeOut(legacy_fade_duration / 4);
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
