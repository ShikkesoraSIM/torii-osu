// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
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
    /// torii: the latency counter. Where the plain FPS counter answers "how many frames",
    /// this one answers "how long does each part of the chain take": what your headphones
    /// cost, what your keyboard costs, what drawing costs, and the frame rate on the end.
    /// It lives in the gameplay corner, so it stays deliberately quiet: state shows
    /// through colour only, no glows, no motion.
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

        private SpriteIcon boltIcon = null!;

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

        // paleta del modo descapeado: de cyan (todo bien) hacia azul/violeta/purpura
        // a medida que la latencia sube.
        private static readonly Color4 cyan = Color4Extensions.FromHex(@"4dd9ff");
        private static readonly Color4 blue = Color4Extensions.FromHex(@"5d7bff");
        private static readonly Color4 purple = Color4Extensions.FromHex(@"a64dff");
        private static readonly Color4 red = Color4Extensions.FromHex(@"ff4d4d");

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
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        CornerRadius = 6,
                        CornerExponent = 5f,
                        Masking = true,
                        Child = new Box
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
                            boltIcon = new SpriteIcon
                            {
                                Icon = FontAwesome.Solid.Bolt,
                                Size = new Vector2(10),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Alpha = 0,
                            },
                            audioStat = new Stat(FontAwesome.Solid.Headphones),
                            inputStat = new Stat(FontAwesome.Solid.Keyboard),
                            drawStat = new Stat(OsuIcon.EditorHitCircle),
                            new Container
                            {
                                // casillero fijo para 4 caracteres: la barra no puede
                                // cambiar de ancho cuando los fps cruzan de 999 a 1000.
                                Width = 36,
                                RelativeSizeAxes = Axes.Y,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Child = fpsText = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    Font = OsuFont.Default.With(size: 15, weight: FontWeight.Bold, fixedWidth: true),
                                    Spacing = new Vector2(-1, 0),
                                },
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

        private bool uncapped => frameSync.Value == FrameSync.UnlimitedNoCap;
        private bool unhinged => uncapped && noLimits.Value;

        /// <summary>
        /// State shows through colour alone: normal keeps the usual green-to-red scale,
        /// uncapped turns cyan and shows the bolt, no-limits paints everything red.
        /// </summary>
        private void updateMode()
        {
            boltIcon.Alpha = uncapped ? 1 : 0;
            boltIcon.Colour = unhinged ? red : cyan;

            updateAudio();
        }

        private void updateAudio()
        {
            double latency = audioLatency.Value;

            audioStat.Value = latency > 0 ? format(latency) : @"--";
            audioStat.Colour = latency <= 0 ? (ColourInfo)colours.Gray6 : latencyColour(latency, 15, 45);
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
            inputStat.Colour = latencyColour(displayedInputTime, 1.5, 5);

            drawStat.Value = format(displayedDrawTime);
            drawStat.Colour = latencyColour(displayedDrawTime, 8, 20);

            fpsText.Text = displayedFps < 9999.5 ? $"{displayedFps:0}" : $"{displayedFps / 1000:0}k";
            fpsText.Colour = unhinged ? red : uncapped ? cyan : colours.GrayF;
        }

        /// <summary>
        /// The per-stat colour for a latency of <paramref name="ms"/>, where
        /// <paramref name="good"/> and below is ideal and <paramref name="bad"/> and
        /// above is a problem. Each mode has its own palette.
        /// </summary>
        private ColourInfo latencyColour(double ms, double good, double bad)
        {
            if (unhinged)
                return red;

            if (uncapped)
            {
                // cyan mientras este todo bajo; hacia azul y purpura cuando sube.
                if (ms <= good)
                    return cyan;
                if (ms >= bad)
                    return purple;

                double mid = (good + bad) / 2;
                return ms < mid
                    ? Interpolation.ValueAt(ms, cyan, blue, good, mid)
                    : Interpolation.ValueAt(ms, blue, purple, mid, bad);
            }

            if (ms <= good)
                return colours.Lime0;
            if (ms >= bad)
                return colours.Red;

            return Interpolation.ValueAt(ms, colours.Lime0, colours.Orange2, good, bad);
        }

        private static string format(double ms) => ms < 10 ? $"{ms:0.0}" : $"{System.Math.Min(ms, 999):0}";

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
                    new Container
                    {
                        // casillero fijo para "88.8"/"999": pasar de un digito a dos
                        // (o el punto que aparece y desaparece) no mueve la barra.
                        Width = 27,
                        RelativeSizeAxes = Axes.Y,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Child = value = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Font = OsuFont.Default.With(size: 13, weight: FontWeight.SemiBold, fixedWidth: true),
                            Spacing = new Vector2(-1, 0),
                        },
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
