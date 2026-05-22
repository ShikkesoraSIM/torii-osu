// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserInterfaceV2
{
    public partial class FormFieldCaption : CompositeDrawable, IHasTooltip
    {
        private OsuTextFlowContainer textFlow = null!;

        private LocalisableString caption;

        public LocalisableString Caption
        {
            get => caption;
            set
            {
                caption = value;

                if (IsLoaded)
                    updateDisplay();
            }
        }

        private LocalisableString tooltipText;

        public LocalisableString TooltipText
        {
            get => tooltipText;
            set
            {
                tooltipText = value;

                if (IsLoaded)
                    updateDisplay();
            }
        }

        // Torii: when set, a NewFeatureBadge is appended to the caption
        // textflow right after the tooltip icon — landing the pill INSIDE
        // the form-control card, immediately to the right of the help
        // bubble, rather than floating as its own row above the card
        // (which was the original SettingsItemV2.MarkAsNew layout and
        // looked detached). Form controls that want to expose a [NEW]
        // badge forward an init-time property to this field — see
        // FormDropdown<T>.NewFeatureId for the canonical wiring.
        private string? newFeatureId;

        public string? NewFeatureId
        {
            get => newFeatureId;
            set
            {
                newFeatureId = value;

                if (IsLoaded)
                    updateDisplay();
            }
        }

        // Reference to the badge once it's added to the textflow, so
        // RegisterInteraction can forward clicks without re-querying
        // the textflow's internal children every time.
        private NewFeatureBadge? badge;

        // Torii: when true, a small "+18" red pill is appended after
        // the [NEW] badge (or after the tooltip icon if no [NEW] badge
        // is attached). Used for settings that toggle access to
        // adult-rated content — currently the NSFW profile media
        // toggle in Settings → Torii → Interface. The pill is a
        // visual cue only; the enforcement is server-side via
        // UserPreference.profile_media_show_nsfw + apply_nsfw_media_policy.
        private bool showExplicitContentBadge;

        public bool ShowExplicitContentBadge
        {
            get => showExplicitContentBadge;
            init
            {
                showExplicitContentBadge = value;

                if (IsLoaded)
                    updateDisplay();
            }
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChild = textFlow = new OsuTextFlowContainer(t => t.Font = OsuFont.Style.Caption1)
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateDisplay();
        }

        private void updateDisplay()
        {
            textFlow.Text = caption;
            badge = null;

            if (TooltipText != default)
            {
                textFlow.AddArbitraryDrawable(new SpriteIcon
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Size = new Vector2(10),
                    Icon = FontAwesome.Solid.QuestionCircle,
                    Margin = new MarginPadding { Left = 5 },
                    Y = 1f,
                });
            }

            if (!string.IsNullOrEmpty(newFeatureId))
            {
                // Compact-mode badge — caption text is ~caption-size, so
                // the default badge font (10pt + 6/2 padding) reads too
                // chunky next to it. NewFeatureBadge.Compact dials font /
                // padding down to match the caption's visual weight.
                badge = new NewFeatureBadge(newFeatureId)
                {
                    Compact = true,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Margin = new MarginPadding { Left = 6 },
                };
                textFlow.AddArbitraryDrawable(badge);
            }

            if (showExplicitContentBadge)
            {
                textFlow.AddArbitraryDrawable(new ExplicitContentBadge
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Margin = new MarginPadding { Left = 6 },
                });
            }
        }

        /// <summary>
        /// Forwarded by parent form controls when the user clicks (taps)
        /// the control card — counts as one interaction toward dismissing
        /// the [NEW] badge. No-op when no badge is currently attached
        /// (caption has no NewFeatureId set, or the badge has already
        /// been dismissed and expired).
        /// </summary>
        public void RegisterInteraction()
        {
            badge?.RegisterInteraction();
        }

        /// <summary>
        /// Small red "+18" pill rendered inline in the caption flow when
        /// <see cref="ShowExplicitContentBadge"/> is set. Visual cue
        /// flagging that the host setting unlocks adult-rated content.
        /// Sized + padded to roughly match the [NEW] badge's footprint so
        /// the two read as siblings when both are present.
        /// </summary>
        private partial class ExplicitContentBadge : CompositeDrawable
        {
            public ExplicitContentBadge()
            {
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 3.5f;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Red.Opacity(0.85f),
                    },
                    new OsuSpriteText
                    {
                        Text = "+18",
                        Font = OsuFont.Default.With(weight: FontWeight.Bold, size: 10),
                        Colour = Color4.White,
                        Margin = new MarginPadding { Horizontal = 5, Vertical = 1 },
                    },
                };
            }
        }
    }
}
