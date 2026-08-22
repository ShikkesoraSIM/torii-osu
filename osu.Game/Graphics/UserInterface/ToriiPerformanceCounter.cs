// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using osu.Framework.Timing;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserInterface
{
    /// <summary>
    /// torii: the detailed performance readout. Where the plain FPS counter answers "how
    /// many frames", this one answers "how long does each part of the chain take": what
    /// your headphones cost, what your keyboard costs, what drawing costs, and the frame
    /// rate on the end. Uncapped frame limiter modes are called out visually, because
    /// running with no limits should look like it.
    /// </summary>
    public partial class ToriiPerformanceCounter : VisibilityContainer
    {
        private const float height = 26;
        private const double damp_time = 100;
        private const double min_time_between_updates = 40;

        private readonly BindableBool enabled = new BindableBool();
        private readonly Bindable<FrameSync> frameSync = new Bindable<FrameSync>();
        private readonly BindableBool noLimits = new BindableBool();
        private IBindable<double> audioLatency = null!;

        private Container background = null!;
        private Box backgroundBox = null!;
        private Container flairContainer = null!;
        private SpriteIcon flairIcon = null!;

        private Stat audioStat = null!;
        private Stat inputStat = null!;
        private Stat drawStat = null!;
        private OsuSpriteText fpsText = null!;

        private ThrottledFrameClock drawClock = null!;
        private ThrottledFrameClock inputClock = null!;

        private double displayedFps;
        private double displayedInputTime;
        private double displayedDrawTime;
        private double lastUpdate;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        public ToriiPerformanceCounter()
        {
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, FrameworkConfigManager frameworkConfig, GameHost host, AudioManager audio)
        {
            InternalChild = new Container
            {
                AutoSizeAxes = Axes.X,
                Height = height,
                Children = new Drawable[]
                {
                    background = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        CornerRadius = 6,
                        CornerExponent = 5f,
                        Masking = true,
                        Child = backgroundBox = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.Black.Opacity(0.65f),
                        },
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Spacing = new Vector2(9, 0),
                        Padding = new MarginPadding { Horizontal = 9 },
                        Children = new Drawable[]
                        {
                            flairContainer = new Container
                            {
                                AutoSizeAxes = Axes.Both,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Alpha = 0,
                                Child = flairIcon = new SpriteIcon
                                {
                                    Icon = FontAwesome.Solid.Bolt,
                                    Size = new Vector2(11),
                                },
                            },
                            audioStat = new Stat(FontAwesome.Solid.Headphones),
                            inputStat = new Stat(FontAwesome.Solid.Keyboard),
                            drawStat = new Stat(OsuIcon.EditorHitCircle),
                            fpsText = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = OsuFont.Default.With(size: 15, weight: FontWeight.Bold, fixedWidth: true),
                                Spacing = new Vector2(-1, 0),
                            },
                        },
                    },
                },
            };

            config.BindWith(OsuSetting.ToriiPerformanceCounter, enabled);
            frameworkConfig.BindWith(FrameworkSetting.FrameSync, frameSync);
            frameworkConfig.BindWith(FrameworkSetting.AllowDangerousUnlimitedNoCap, noLimits);
            audioLatency = audio.OutputLatency.GetBoundCopy();

            drawClock = host.DrawThread.Clock;
            inputClock = host.InputThread.Clock;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            enabled.BindValueChanged(e => State.Value = e.NewValue ? Visibility.Visible : Visibility.Hidden, true);
            FinishTransforms(true);

            frameSync.BindValueChanged(_ => updateMode());
            noLimits.BindValueChanged(_ => updateMode(), true);

            audioLatency.BindValueChanged(_ => updateAudio(), true);
        }

        protected override void PopIn() => this.FadeIn(150, Easing.OutQuint);
        protected override void PopOut() => this.FadeOut(150, Easing.OutQuint);

        /// <summary>
        /// The counter dresses itself according to how far past the safety rails the user
        /// has gone: normal, uncapped drawing, and no limits at all.
        /// </summary>
        private void updateMode()
        {
            bool uncapped = frameSync.Value == FrameSync.UnlimitedNoCap;
            bool unhinged = uncapped && noLimits.Value;

            flairContainer.ClearTransforms(true);
            flairIcon.ClearTransforms(true);
            backgroundBox.ClearTransforms();
            background.EdgeEffect = default;

            if (unhinged)
            {
                // no limits: the thing is on fire and it will not sit still.
                flairContainer.Alpha = 1;
                flairIcon.Icon = FontAwesome.Solid.Meteor;
                flairIcon.Colour = colours.Yellow;
                flairIcon.Size = new Vector2(13);

                flairIcon.Spin(1400, RotationDirection.Clockwise);
                flairContainer.ScaleTo(1.15f, 260, Easing.OutQuint).Then().ScaleTo(0.95f, 260, Easing.InQuint).Loop();

                backgroundBox.Colour = ColourInfo.GradientHorizontal(
                    colours.Pink2.Opacity(0.75f),
                    colours.Orange1.Opacity(0.75f));

                background.EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = colours.Orange1.Opacity(0.5f),
                    Radius = 12,
                };

                backgroundBox.FadeTo(0.85f, 400, Easing.OutQuint).Then().FadeTo(1f, 400, Easing.InQuint).Loop();
            }
            else if (uncapped)
            {
                // uncapped drawing: a bolt, and everything goes blue.
                flairContainer.Alpha = 1;
                flairIcon.Icon = FontAwesome.Solid.Bolt;
                flairIcon.Colour = colours.Blue0;
                flairIcon.Size = new Vector2(11);
                flairContainer.Scale = Vector2.One;

                backgroundBox.Colour = colours.Blue3.Opacity(0.5f);

                background.EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = colours.Blue1.Opacity(0.35f),
                    Radius = 8,
                };
            }
            else
            {
                flairContainer.Alpha = 0;
                flairContainer.Scale = Vector2.One;
                backgroundBox.Colour = Color4.Black.Opacity(0.65f);
            }

            updateAudio();
        }

        private void updateAudio()
        {
            double latency = audioLatency.Value;

            audioStat.Value = latency > 0 ? format(latency) : @"--";
            audioStat.Colour = tint(latency <= 0 ? colours.Gray6 : latencyColour(latency, 12, 30));
        }

        protected override void Update()
        {
            base.Update();

            displayedFps = Interpolation.DampContinuously(displayedFps, drawClock.FramesPerSecond, damp_time, Time.Elapsed);
            displayedDrawTime = Interpolation.DampContinuously(displayedDrawTime, drawClock.ElapsedFrameTime, damp_time, Time.Elapsed);
            displayedInputTime = Interpolation.DampContinuously(displayedInputTime, inputClock.ElapsedFrameTime, damp_time, Time.Elapsed);

            if (Time.Current - lastUpdate < min_time_between_updates)
                return;

            lastUpdate = Time.Current;

            inputStat.Value = format(displayedInputTime);
            inputStat.Colour = tint(latencyColour(displayedInputTime, 1.5, 5));

            drawStat.Value = format(displayedDrawTime);
            drawStat.Colour = tint(latencyColour(displayedDrawTime, 8, 20));

            fpsText.Text = $"{displayedFps:#,0}";
            fpsText.Colour = tint(colours.GrayF);
        }

        /// <summary>Blue everything while uncapped, so the state reads at a glance.</summary>
        private ColourInfo tint(ColourInfo normal) => frameSync.Value == FrameSync.UnlimitedNoCap && !noLimits.Value ? colours.Blue0 : normal;

        private Color4 latencyColour(double ms, double good, double bad) =>
            ms <= good
                ? colours.Lime0
                : ms >= bad
                    ? colours.Red
                    : Interpolation.ValueAt(ms, colours.Lime0, colours.Orange2, good, bad);

        private static string format(double ms) => ms < 10 ? $"{ms:0.0}" : $"{ms:0}";

        /// <summary>An icon and its number, the unit tucked in small at the end.</summary>
        private partial class Stat : FillFlowContainer
        {
            private readonly SpriteIcon icon;
            private readonly OsuSpriteText value;

            public string Value
            {
                set => this.value.Text = value;
            }

            public new ColourInfo Colour
            {
                set
                {
                    icon.Colour = value;
                    this.value.Colour = value;
                }
            }

            public Stat(IconUsage iconUsage)
            {
                AutoSizeAxes = Axes.Both;
                Direction = FillDirection.Horizontal;
                Anchor = Anchor.CentreLeft;
                Origin = Anchor.CentreLeft;
                Spacing = new Vector2(3, 0);

                Children = new Drawable[]
                {
                    icon = new SpriteIcon
                    {
                        Icon = iconUsage,
                        Size = new Vector2(10),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                    value = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.Default.With(size: 13, weight: FontWeight.SemiBold, fixedWidth: true),
                        Spacing = new Vector2(-1, 0),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = @"ms",
                        Alpha = 0.6f,
                        Font = OsuFont.Default.With(size: 9, weight: FontWeight.SemiBold),
                        Margin = new MarginPadding { Left = 1 },
                    },
                };
            }
        }
    }
}
