// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Audio;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Contracted;
using osu.Game.Screens.Ranking.Expanded;
using osu.Game.Users;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Ranking
{
    public partial class ScorePanel : CompositeDrawable, IStateful<PanelState>
    {
        /// <summary>
        /// Width of the panel when contracted.
        /// </summary>
        // Torii: fsyori bumps the contracted panels MUCH bigger
        // (130→300, 385→500) because in the grayscale theme the
        // results screen shows contracted panels as the same
        // expanded content scaled down rather than a separate
        // visual style — there's no narrow "ranked spine"
        // anymore, just a smaller version of the full panel. The
        // adjusted geometry below + the contracted-state animation
        // changes in updateState (Color4.FromHex("#777") tint +
        // ScaleTo 0.85f) implement that fade-down look.
        public static readonly float CONTRACTED_WIDTH = ThemeAware.Pick(130f, 300f);

        /// <summary>
        /// Height of the panel when contracted.
        /// </summary>
        public static readonly float CONTRACTED_HEIGHT = ThemeAware.Pick(385f, 500f);

        /// <summary>
        /// Width of the panel when expanded.
        /// </summary>
        public static readonly float EXPANDED_WIDTH = ThemeAware.Pick(360f, 350f);

        /// <summary>
        /// Height of the panel when expanded.
        /// </summary>
        private const float expanded_height = 586;

        /// <summary>
        /// Height of the top layer when the panel is expanded.
        /// </summary>
        private const float expanded_top_layer_height = 53;

        /// <summary>
        /// Height of the top layer when the panel is contracted.
        /// </summary>
        private const float contracted_top_layer_height = 30;

        /// <summary>
        /// Duration for the panel to resize into its expanded/contracted size.
        /// </summary>
        public const double RESIZE_DURATION = 200;

        /// <summary>
        /// Delay after <see cref="RESIZE_DURATION"/> before the top layer is expanded.
        /// </summary>
        public const double TOP_LAYER_EXPAND_DELAY = 100;

        /// <summary>
        /// Duration for the top layer expansion.
        /// </summary>
        private const double top_layer_expand_duration = 200;

        /// <summary>
        /// Duration for the panel contents to fade in.
        /// </summary>
        private const double content_fade_duration = 50;

        // Torii: fsyori replaces all four panel-layer colours with
        // pure black (#000) in the grayscale theme — gradients
        // collapse to flat black, so the panel reads as a solid
        // dark card rather than the subtle 0.2-luminance ramp the
        // default Torii panels use. The Torii defaults remain
        // exactly as upstream.
        private static readonly ColourInfo expanded_top_layer_colour = ColourInfo.GradientVertical(
            Color4Extensions.FromHex(ThemeAware.Pick("#444", "#000")),
            Color4Extensions.FromHex(ThemeAware.Pick("#333", "#000")));
        private static readonly ColourInfo expanded_middle_layer_colour = ColourInfo.GradientVertical(
            Color4Extensions.FromHex(ThemeAware.Pick("#555", "#000")),
            Color4Extensions.FromHex(ThemeAware.Pick("#333", "#000")));
        private static readonly Color4 contracted_top_layer_colour = Color4Extensions.FromHex(ThemeAware.Pick("#353535", "#000"));
        private static readonly Color4 contracted_middle_layer_colour = Color4Extensions.FromHex(ThemeAware.Pick("#353535", "#000"));

        public event Action<PanelState>? StateChanged;

        /// <summary>
        /// The position of the score in the rankings.
        /// </summary>
        public readonly Bindable<int?> ScorePosition = new Bindable<int?>();

        /// <summary>
        /// An action to be invoked if this <see cref="ScorePanel"/> is clicked while in an expanded state.
        /// </summary>
        public Action? PostExpandAction;

        public readonly ScoreInfo Score;

        [Resolved]
        private OsuGameBase game { get; set; } = null!;

        private AudioContainer audioContent = null!;

        private bool displayWithFlair;

        private Container topLayerContainer = null!;
        private Drawable topLayerBackground = null!;
        private Container topLayerContentContainer = null!;
        private Drawable? topLayerContent;

        private Container middleLayerContainer = null!;
        private Drawable middleLayerBackground = null!;
        private Container middleLayerContentContainer = null!;
        private Drawable? middleLayerContent;

        private ScorePanelTrackingContainer? trackingContainer;

        private DrawableSample? samplePanelFocus;

        public ScorePanel(ScoreInfo score, bool isNewLocalScore = false)
        {
            Score = score;
            displayWithFlair = isNewLocalScore;

            ScorePosition.Value = score.Position;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            // ScorePanel doesn't include the top extruding area in its own size.
            // Adding a manual offset here allows the expanded version to take on an "acceptable" vertical centre when at 100% UI scale.
            const float vertical_fudge = 20;

            InternalChild = audioContent = new AudioContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(40),
                Y = vertical_fudge,
                Children = new Drawable[]
                {
                    topLayerContainer = new Container
                    {
                        Name = "Top layer",
                        RelativeSizeAxes = Axes.X,
                        Alpha = 0,
                        Height = 120,
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                // Torii: fsyori switches from the
                                // smoothed superellipse (20px + 2.5
                                // exponent) to a plain 8px corner —
                                // sharper, harder-edged card that
                                // matches the squared aesthetic of
                                // the rest of the grayscale reskin.
                                CornerRadius = ThemeAware.Pick(20f, 8f),
                                CornerExponent = ThemeAware.Pick(2.5f, 2f),
                                Masking = true,
                                Child = topLayerBackground = new Box { RelativeSizeAxes = Axes.Both }
                            },
                            topLayerContentContainer = new Container { RelativeSizeAxes = Axes.Both }
                        }
                    },
                    middleLayerContainer = new Container
                    {
                        Name = "Middle layer",
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                CornerRadius = ThemeAware.Pick(20f, 8f),
                                CornerExponent = ThemeAware.Pick(2.5f, 2f),
                                Masking = true,
                                // Torii: fsyori drops the
                                // UserCoverBackground entirely in
                                // the grayscale theme — the avatar
                                // cover tint would clash with the
                                // pure-black panel, so the panel is
                                // just a flat dark card. Conditional
                                // construction keeps Torii's
                                // existing avatar-tint look intact.
                                Children = OsuColour.UsesGrayscaleStructure
                                    ? new Drawable[]
                                    {
                                        middleLayerBackground = new Box { RelativeSizeAxes = Axes.Both },
                                    }
                                    : new Drawable[]
                                    {
                                        middleLayerBackground = new Box { RelativeSizeAxes = Axes.Both },
                                        new UserCoverBackground
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            User = Score.User,
                                            Colour = ColourInfo.GradientVertical(Color4.White.Opacity(0.5f), Color4Extensions.FromHex("#444").Opacity(0))
                                        }
                                    }
                            },
                            middleLayerContentContainer = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                // Torii: in grayscale theme fsyori
                                // anchors the middle content centre
                                // so the contracted-state scale-down
                                // animates around the panel's centre
                                // rather than the top-left corner.
                                Anchor = OsuColour.UsesGrayscaleStructure ? Anchor.Centre : Anchor.TopLeft,
                                Origin = OsuColour.UsesGrayscaleStructure ? Anchor.Centre : Anchor.TopLeft,
                            }
                        }
                    },
                    samplePanelFocus = new DrawableSample(audio.Samples.Get(@"Results/score-panel-focus"))
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            updateState();

            topLayerBackground.FinishTransforms(false, nameof(Colour));
            middleLayerBackground.FinishTransforms(false, nameof(Colour));
        }

        private PanelState state = PanelState.Contracted;

        public PanelState State
        {
            get => state;
            set
            {
                if (state == value)
                    return;

                state = value;

                if (IsLoaded)
                {
                    updateState();

                    if (value == PanelState.Expanded)
                        playAppearSample();
                }

                StateChanged?.Invoke(value);
            }
        }

        protected override void Update()
        {
            base.Update();
            audioContent.Balance.Value = (Math.Clamp(ScreenSpaceDrawQuad.Centre.X / game.ScreenSpaceDrawQuad.Width, -1, 1) * 2 - 1) * OsuGameBase.SFX_STEREO_STRENGTH;
        }

        private void playAppearSample()
        {
            var channel = samplePanelFocus?.GetChannel();
            if (channel == null) return;

            channel.Frequency.Value = 0.99 + RNG.NextDouble(0.2);
            channel.Play();
        }

        private void updateState()
        {
            topLayerContent?.FadeOut(content_fade_duration).Expire();
            middleLayerContent?.FadeOut(content_fade_duration).Expire();

            // Torii: fsyori's reskin treats Contracted as a scaled-down
            // Expanded — same panel content but FadeColour'd to #777
            // and ScaleTo 0.85f. Captures the firstLoad flag in scope
            // so both cases skip the resize animation on first set.
            bool firstLoad = topLayerContent == null;
            double duration = firstLoad ? 0 : RESIZE_DURATION;

            switch (state)
            {
                case PanelState.Expanded:
                    Size = new Vector2(EXPANDED_WIDTH, expanded_height);

                    topLayerBackground.FadeColour(expanded_top_layer_colour, RESIZE_DURATION, Easing.OutQuint);
                    middleLayerBackground.FadeColour(expanded_middle_layer_colour, RESIZE_DURATION, Easing.OutQuint);

                    topLayerContentContainer.Add(topLayerContent = new ExpandedPanelTopContent(Score.User, firstLoad) { Alpha = 0 });
                    middleLayerContentContainer.Add(middleLayerContent = new ExpandedPanelMiddleContent(Score, displayWithFlair) { Alpha = 0 });

                    if (OsuColour.UsesGrayscaleStructure)
                    {
                        // Torii: in grayscale theme, "expanded" is the
                        // baseline visual — full colour, full scale.
                        // We have to explicitly re-set both because
                        // the Contracted case below modifies the
                        // container's Colour + Scale, and toggling
                        // back to Expanded should reset them.
                        middleLayerContentContainer.FadeColour(Color4.White, duration, Easing.OutQuint);
                        middleLayerContentContainer.ScaleTo(1.0f, duration, Easing.OutQuint);
                    }

                    // only the first expanded display should happen with flair.
                    displayWithFlair = false;
                    break;

                case PanelState.Contracted:
                    Size = new Vector2(CONTRACTED_WIDTH, CONTRACTED_HEIGHT);

                    topLayerBackground.FadeColour(contracted_top_layer_colour, duration, Easing.OutQuint);
                    middleLayerBackground.FadeColour(contracted_middle_layer_colour, duration, Easing.OutQuint);

                    if (OsuColour.UsesGrayscaleStructure)
                    {
                        // Torii: fsyori reuses the Expanded content
                        // for Contracted in the grayscale theme,
                        // tinted gray + scaled to 0.85 to read as
                        // "dimmer cousin of the highlighted panel".
                        // The classic Contracted layout (separate
                        // top-content "score position spine" + cover
                        // ribbon) doesn't fit the wider 300px
                        // grayscale panel geometry — so we drop it.
                        topLayerContentContainer.Add(topLayerContent = new ExpandedPanelTopContent(Score.User, firstLoad) { Alpha = 0 });
                        middleLayerContentContainer.Add(middleLayerContent = new ExpandedPanelMiddleContent(Score, displayWithFlair) { Alpha = 0 });
                        middleLayerContentContainer.FadeColour(Color4Extensions.FromHex("#777"), duration, Easing.OutQuint);
                        middleLayerContentContainer.ScaleTo(0.85f, duration, Easing.OutQuint);

                        displayWithFlair = false;
                    }
                    else
                    {
                        // Default Torii: the original Contracted
                        // layout with the narrow ranked-spine top
                        // content and the cover ribbon underneath.
                        topLayerContentContainer.Add(topLayerContent = new ContractedPanelTopContent
                        {
                            ScorePosition = { BindTarget = ScorePosition },
                            Alpha = 0
                        });

                        middleLayerContentContainer.Add(middleLayerContent = new ContractedPanelMiddleContent(Score) { Alpha = 0 });
                    }
                    break;
            }

            audioContent.ResizeTo(Size, RESIZE_DURATION, Easing.OutQuint);

            bool topLayerExpanded = topLayerContainer.Y < 0;

            // If the top layer was already expanded, then we don't need to wait for the resize and can instead transform immediately. This looks better when changing the panel state.
            using (BeginDelayedSequence(topLayerExpanded ? 0 : RESIZE_DURATION + TOP_LAYER_EXPAND_DELAY))
            {
                topLayerContainer.FadeIn();

                switch (state)
                {
                    case PanelState.Expanded:
                        topLayerContainer.MoveToY(-expanded_top_layer_height / 2, top_layer_expand_duration, Easing.OutQuint);
                        middleLayerContainer.MoveToY(expanded_top_layer_height / 2, top_layer_expand_duration, Easing.OutQuint);
                        break;

                    case PanelState.Contracted:
                        topLayerContainer.MoveToY(-contracted_top_layer_height / 2, top_layer_expand_duration, Easing.OutQuint);
                        middleLayerContainer.MoveToY(contracted_top_layer_height / 2, top_layer_expand_duration, Easing.OutQuint);
                        break;
                }

                topLayerContent?.FadeIn(content_fade_duration);
                middleLayerContent?.FadeIn(content_fade_duration);
            }
        }

        public override Vector2 Size
        {
            get => base.Size;
            set
            {
                base.Size = value;

                // Auto-size isn't used to avoid 1-frame issues and because the score panel is removed/re-added to the container.
                if (trackingContainer != null)
                    trackingContainer.Size = value;
            }
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (State == PanelState.Contracted)
            {
                State = PanelState.Expanded;
                return true;
            }

            PostExpandAction?.Invoke();

            return true;
        }

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
            => base.ReceivePositionalInputAt(screenSpacePos)
               || topLayerContainer.ReceivePositionalInputAt(screenSpacePos)
               || middleLayerContainer.ReceivePositionalInputAt(screenSpacePos);

        /// <summary>
        /// Creates a <see cref="ScorePanelTrackingContainer"/> which this <see cref="ScorePanel"/> can reside inside.
        /// The <see cref="ScorePanelTrackingContainer"/> will track the size of this <see cref="ScorePanel"/>.
        /// </summary>
        /// <remarks>
        /// This <see cref="ScorePanel"/> is immediately added as a child of the <see cref="ScorePanelTrackingContainer"/>.
        /// </remarks>
        /// <returns>The <see cref="ScorePanelTrackingContainer"/>.</returns>
        /// <exception cref="InvalidOperationException">If a <see cref="ScorePanelTrackingContainer"/> already exists.</exception>
        public ScorePanelTrackingContainer CreateTrackingContainer()
        {
            if (trackingContainer != null)
                throw new InvalidOperationException("A score panel container has already been created.");

            return trackingContainer = new ScorePanelTrackingContainer(this);
        }
    }
}
