// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    public partial class ToriiPpDevIndicator : CompositeDrawable, IHasTooltip
    {
        private const float collapsed_width = 0;
        private const float expanded_width = 68;
        private const float badge_height = 24;

        private IBindable<bool> ppDevBindable = null!;

        [Resolved(CanBeNull = true)]
        private IAPIProvider? api { get; set; }

        public LocalisableString TooltipText => "Using latest pp-dev calculations.";

        public ToriiPpDevIndicator()
        {
            AutoSizeAxes = Axes.None;
            Size = new Vector2(collapsed_width, badge_height);
            Alpha = 0;
            AlwaysPresent = true;

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                CornerRadius = 8,
                CornerExponent = 3f,
                BorderThickness = 1f,
                BorderColour = new Color4(125, 196, 255, 200),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(16, 33, 58, 185),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(70, 155, 255, 55),
                        Blending = BlendingParameters.Additive,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                        Text = "PP-DEV",
                        Colour = new Color4(186, 226, 255, 255),
                    }
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ppDevBindable = ToriiPpVariantState.UsePpDevVariantBindable.GetBoundCopy();
            ppDevBindable.BindValueChanged(_ => updateVisibility(), true);
        }

        private void updateVisibility()
        {
            bool show = ToriiPpVariantState.UsePpDevVariant;

            if (api is APIAccess apiAccess)
                show &= apiAccess.IsLikelyToriiEndpoint && !apiAccess.IsUnsafeOfficialEndpoint;

            if (show)
            {
                this.FadeTo(1, 180, Easing.OutQuint);
                this.ClearTransforms(false, nameof(Scale));
                this.ResizeWidthTo(expanded_width, 140, Easing.OutQuint);
                this.ScaleTo(new Vector2(1.03f, 1f), 900, Easing.InOutSine)
                    .Then()
                    .ScaleTo(new Vector2(1f, 1f), 900, Easing.InOutSine)
                    .Loop();
            }
            else
            {
                this.FadeOut(140, Easing.OutQuint);
                this.ClearTransforms(false, nameof(Scale));
                this.ResizeWidthTo(collapsed_width, 140, Easing.OutQuint);
                this.Scale = Vector2.One;
            }
        }
    }
}
