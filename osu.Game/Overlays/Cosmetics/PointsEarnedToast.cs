// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// A small celebratory toast that animates a "+N points" gain and says why it
    /// was earned (top play, daily play, ...). Self-dismissing: it plays an
    /// entrance, rolls the number up, holds, then fades out and expires. Styled
    /// with the Torii Briefing glass so it matches the rest of the suite.
    /// </summary>
    internal partial class PointsEarnedToast : CompositeDrawable
    {
        private readonly int amount;
        private readonly string reason;
        private readonly string reasonRef;
        private readonly bool reducedMotion;

        private OsuSpriteText amountText;
        private SpriteIcon icon;

        private const double count_duration = 560;
        private const double hold = 2600;
        private const double exit_duration = 260;

        private double countStart;
        private bool counting;
        private int displayed = -1;

        public PointsEarnedToast(int amount, string reason, string reasonRef, bool reducedMotion)
        {
            this.amount = amount;
            this.reason = reason ?? string.Empty;
            this.reasonRef = reasonRef;
            this.reducedMotion = reducedMotion;

            AutoSizeAxes = Axes.Both;
            Anchor = Anchor.TopCentre;
            Origin = Anchor.TopCentre;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var info = PointsReasonInfo.Resolve(reason, reasonRef);

            amountText = new OsuSpriteText
            {
                Text = $"+{(reducedMotion ? amount : 0):N0}",
                Font = OsuFont.TorusAlternate.With(size: BriefingTheme.TypeTitle, weight: FontWeight.Bold),
                Colour = info.Accent,
            };

            var textChildren = new List<Drawable>
            {
                amountText,
                new OsuSpriteText
                {
                    Text = info.Headline,
                    Font = OsuFont.Torus.With(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                    Colour = Color4.White,
                },
            };

            if (!string.IsNullOrEmpty(info.Subtitle))
            {
                textChildren.Add(new OsuSpriteText
                {
                    Text = info.Subtitle,
                    Font = OsuFont.Torus.With(size: BriefingTheme.TypeCaption),
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                });
            }

            InternalChild = new BriefingGlass
            {
                AutoSizeAxes = Axes.Both,
                RelativeContentSize = Axes.None,
                CornerSize = BriefingTheme.CornerMd,
                SurfaceLift = 1.45f,
                ShadowColor = info.Accent,
                ShadowOpacity = 0.4f,
                ShadowRadius = 26f,
                Child = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(BriefingTheme.SpacingMd, 0),
                    Margin = new MarginPadding { Horizontal = BriefingTheme.SpacingLg, Vertical = BriefingTheme.SpacingMd },
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(46),
                            Children = new Drawable[]
                            {
                                new Circle
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = info.Accent.Opacity(0.16f),
                                },
                                icon = new SpriteIcon
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Size = new Vector2(22),
                                    Icon = info.Icon,
                                    Colour = info.Accent,
                                },
                            },
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 2),
                            Children = textChildren.ToArray(),
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            double entrance = reducedMotion ? 160 : BriefingTheme.EntranceDuration;

            if (reducedMotion)
            {
                this.FadeInFromZero(entrance);
            }
            else
            {
                this.FadeInFromZero(entrance, Easing.OutQuad);
                this.MoveToY(16).MoveToY(0, entrance, Easing.OutBack);
                this.ScaleTo(0.9f).ScaleTo(1, entrance, Easing.OutBack);

                // a little pop on the icon as the number starts rolling
                icon.Delay(120).ScaleTo(1.18f, 140, Easing.OutQuad).Then().ScaleTo(1, 240, Easing.OutQuad);

                countStart = Time.Current + 120;
                counting = true;
            }

            this.Delay(entrance + hold)
                .FadeOut(exit_duration, Easing.OutQuad)
                .ScaleTo(0.94f, exit_duration, Easing.OutQuad)
                .Expire();
        }

        protected override void Update()
        {
            base.Update();

            if (!counting)
                return;

            double p = Math.Clamp((Time.Current - countStart) / count_duration, 0, 1);
            double eased = 1 - Math.Pow(1 - p, 3); // OutCubic
            int val = (int)Math.Round(amount * eased);

            if (val != displayed)
            {
                displayed = val;
                amountText.Text = $"+{val:N0}";
            }

            if (p >= 1)
                counting = false;
        }
    }

    /// <summary>Maps a ledger reason (+ ref) to a headline, subtitle, icon and accent.</summary>
    internal readonly struct PointsReasonInfo
    {
        public readonly string Headline;
        public readonly string Subtitle;
        public readonly IconUsage Icon;
        public readonly Color4 Accent;

        private PointsReasonInfo(string headline, string subtitle, IconUsage icon, Color4 accent)
        {
            Headline = headline;
            Subtitle = subtitle;
            Icon = icon;
            Accent = accent;
        }

        public static PointsReasonInfo Resolve(string reason, string reasonRef)
        {
            switch (reason)
            {
                case "top_play":
                    return new PointsReasonInfo("Top play!", "New personal best", FontAwesome.Solid.Trophy, BriefingTheme.AccentAmber);

                case "daily_play":
                    int streak = parseStreak(reasonRef);
                    if (streak >= 2)
                        return new PointsReasonInfo($"{streak} day streak!", "Daily play bonus", FontAwesome.Solid.FireAlt, BriefingTheme.AccentGain);

                    return new PointsReasonInfo("First play today!", "Daily bonus", FontAwesome.Solid.CalendarCheck, BriefingTheme.AccentGain);

                case "gift":
                    return new PointsReasonInfo("Gift from Torii Halo!", null, FontAwesome.Solid.Gift, BriefingTheme.AccentPink);

                case "access_code":
                    return new PointsReasonInfo("Code redeemed!", null, FontAwesome.Solid.Award, BriefingTheme.AccentSky);

                default:
                    return new PointsReasonInfo("Points earned!", null, FontAwesome.Solid.Star, BriefingTheme.AccentAmber);
            }
        }

        private static int parseStreak(string reasonRef)
        {
            if (string.IsNullOrEmpty(reasonRef) || !reasonRef.StartsWith("streak:", StringComparison.Ordinal))
                return 0;

            return int.TryParse(reasonRef.Substring("streak:".Length), out int n) ? n : 0;
        }
    }
}
