// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Localisation.SkinComponents;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Mania.Skinning.Default
{
    /// <summary>
    /// Default ("Torii") visual for <see cref="ManiaRatioCounter"/>:
    /// a small uppercase "RATIO" label sitting above a pink-gradient
    /// numeric value. Same colour language as the alpha toolbar pill
    /// and the active-chip state — keeps the Torii brand consistent
    /// across the HUD without slapping a literal logo on a counter.
    ///
    /// Used for both the modern Argon skin and the Triangles default
    /// skin. The original suggestion noted that the Argon variant
    /// (with its wireframe-digit treatment) doesn't render decimal
    /// points cleanly, so we ship this single shared "default"
    /// variant for both modern skins.
    /// </summary>
    public partial class DefaultManiaRatioCounter : ManiaRatioCounter
    {
        // Torii signature gradient — same stops the alpha pill uses
        // for its active-chip fill. Vertical because the number sits
        // tall enough that a top-to-bottom gradient reads cleaner
        // than horizontal.
        private static readonly ColourInfo torii_value_colour = ColourInfo.GradientVertical(
            new Color4(255, 138, 211, 255),
            new Color4(253, 138, 152, 255));

        [SettingSource(typeof(SkinnableComponentStrings), nameof(SkinnableComponentStrings.ShowLabel))]
        public Bindable<bool> ShowLabel { get; } = new BindableBool(true);

        protected override IHasText CreateText() => new RatioTextComponent(ShowLabel);

        private partial class RatioTextComponent : CompositeDrawable, IHasText
        {
            private readonly OsuSpriteText valueText;
            private readonly Container labelContainer;

            public LocalisableString Text
            {
                get => valueText.Text;
                set
                {
                    valueText.Text = value;
                    pulse();
                }
            }

            public RatioTextComponent(IBindable<bool> showLabel)
            {
                AutoSizeAxes = Axes.Both;

                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, -2),
                    Children = new Drawable[]
                    {
                        labelContainer = new Container
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Child = new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = @"RATIO",
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                Spacing = new Vector2(1.4f, 0),
                                Colour = Color4.White.Opacity(0.55f),
                            },
                        },
                        valueText = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            // Numeric font matches the rest of the HUD
                            // (combo, score, accuracy) so it doesn't
                            // visually fight its neighbours.
                            Font = OsuFont.Numeric.With(size: 26, fixedWidth: true),
                            // Bake the pink gradient on top of the
                            // numeric font with a soft shadow so the
                            // value pops against any beatmap
                            // background without needing its own
                            // pill / box. Same approach the alpha
                            // toolbar takes for its active chips.
                            Colour = torii_value_colour,
                            Shadow = true,
                            ShadowColour = new Color4(0, 0, 0, 110),
                            ShadowOffset = new Vector2(0, 0.08f),
                        },
                    }
                };

                showLabel.BindValueChanged(s => labelContainer.Alpha = s.NewValue ? 1 : 0, true);
            }

            // Tiny scale-up + glow flash on each value change. Easy
            // to disable later if it turns out to be distracting; we
            // keep it subtle on purpose (~6% scale, 200ms).
            private void pulse()
            {
                valueText.ClearTransforms();
                valueText
                    .ScaleTo(1.06f, 90, Easing.OutQuint)
                    .Then()
                    .ScaleTo(1f, 240, Easing.OutElasticHalf);
            }
        }
    }
}
