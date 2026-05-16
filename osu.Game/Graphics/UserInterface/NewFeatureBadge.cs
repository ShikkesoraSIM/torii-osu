// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserInterface
{
    /// <summary>
    /// Small "NEW" pill with a soft pink glow, used to flag recently-
    /// added features in the settings panel and other menu surfaces.
    /// Visibility is driven by <see cref="NewFeatureTracker"/>: the
    /// badge appears on first load if the user hasn't yet interacted
    /// with the host control enough times, and auto-fades once the
    /// interaction count crosses the dismiss threshold.
    ///
    /// Dismiss model — interactions, not views
    /// ---------------------------------------
    /// The badge does NOT count "this UI was visible" as a dismissal
    /// signal — a user opening the settings panel and scrolling past a
    /// new toggle without engaging with it leaves the badge intact.
    /// Dismissal requires direct user input on the host control
    /// (typically a click), which the host forwards via
    /// <see cref="RegisterInteraction"/>. Two interactions are enough
    /// to consider the feature "noticed" — the first lights up the
    /// pill in the user's attention, the second is reinforcement.
    ///
    /// Sizing — default vs. <see cref="Compact"/>
    /// ------------------------------------------
    /// Default: 10 pt bold text with 6 px horizontal / 2 px vertical
    /// padding, big enough to read on its own as a row-level pill.
    /// Compact: 8 pt text with 4 px / 1 px padding, sized to sit
    /// inline next to a caption-size text + tooltip icon without
    /// dominating the row.
    /// </summary>
    public partial class NewFeatureBadge : CompositeDrawable
    {
        private readonly string featureId;

        [Resolved]
        private NewFeatureTracker tracker { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        /// <summary>
        /// When true, renders at the inline-with-caption size profile
        /// (smaller font + tighter padding). Set this at construction
        /// time — runtime mutation has no effect because the inner
        /// container is built in <see cref="load"/>.
        /// </summary>
        public bool Compact { get; init; }

        public NewFeatureBadge(string featureId)
        {
            this.featureId = featureId;
            AutoSizeAxes = Axes.Both;

            // Start invisible — we decide whether to show in
            // LoadComplete after the tracker has been resolved.
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Pink chosen over yellow/green because:
            //  - Yellow already means "warning" elsewhere in settings
            //    (SetNoticeText uses colours.Yellow for warnings)
            //  - Green means "OK / success / info notice"
            //  - Pink is otherwise unused as a status colour in
            //    settings, so it reads unambiguously as a positive
            //    "hey, check this out" rather than a warning.
            var fill = colours.Pink;
            var glow = colours.PinkLight;

            float fontSize = Compact ? 8 : 10;
            var padding = Compact
                ? new MarginPadding { Horizontal = 4, Vertical = 1 }
                : new MarginPadding { Horizontal = 6, Vertical = 2 };
            float cornerRadius = Compact ? 4 : 6;
            float glowRadius = Compact ? 4 : 6;

            InternalChild = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = cornerRadius,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = new Color4(glow.R, glow.G, glow.B, 0.55f),
                    Radius = glowRadius,
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = fill,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "NEW",
                        Colour = Color4.White,
                        Font = OsuFont.GetFont(size: fontSize, weight: FontWeight.Bold),
                        Margin = padding,
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // If the badge has already been fully dismissed for this
            // feature, skip the show. Saves a JSON disk read churn on
            // every settings-panel open after the dismiss boundary.
            if (!tracker.ShouldShowBadge(featureId))
            {
                Expire();
                return;
            }

            // Show immediately. Unlike the previous view-based model,
            // we do NOT bump the counter at load time — that's reserved
            // for actual user interactions on the host control (see
            // RegisterInteraction below).
            this.FadeIn(200, Easing.OutQuint);
        }

        /// <summary>
        /// Called by the host control when the user clicks (taps) it.
        /// Bumps the per-feature interaction counter; if this call
        /// crosses the dismiss threshold the badge plays a final visible
        /// beat and then fades out. Safe to call multiple times after
        /// dismissal — the tracker treats post-threshold calls as
        /// no-ops.
        /// </summary>
        public void RegisterInteraction()
        {
            bool stillVisible = tracker.RecordInteraction(featureId);

            if (stillVisible)
                return;

            // Threshold crossed this interaction. Schedule the final
            // fade-out for after a short delay so the user gets one
            // last beat of "this was the new thing" before it leaves
            // the UI, rather than ripping the badge away mid-click.
            this.Delay(800).FadeOut(400, Easing.OutQuint).Expire();
        }
    }
}
