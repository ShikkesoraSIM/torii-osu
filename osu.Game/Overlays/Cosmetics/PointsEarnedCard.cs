// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
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
    /// One aggregated "points earned" summary for a finished play: every award
    /// (top play, daily, pp milestone, medals) folded into a single card with a
    /// grand total, a per-source breakdown and the new balance. Holds long enough
    /// to read, counts the total up, then bursts coins upward toward the toolbar
    /// balance. Replaces the old per-event toasts so medals + points don't stack
    /// and flicker past.
    /// </summary>
    internal partial class PointsEarnedCard : CompositeDrawable
    {
        public readonly record struct Line(int Amount, string Reason, string Ref);

        private const float card_width = 300f;
        private const double count_duration = 760;
        private const double hold = 5200;
        private const double exit_duration = 340;

        private readonly List<Line> lines;
        private readonly int total;
        private readonly int balanceAfter;
        private readonly bool reducedMotion;

        private OsuSpriteText totalText;
        private Container coinLayer;
        private Sample appearSample;

        private double countStart;
        private bool counting;
        private int displayed = -1;

        // The breakdown rows, kept so they can reveal one-by-one after the card
        // enters (each in its own reason colour).
        private readonly List<Drawable> revealRows = new List<Drawable>();

        public PointsEarnedCard(IEnumerable<Line> lines, int balanceAfter, bool reducedMotion)
        {
            this.lines = lines.ToList();
            this.balanceAfter = balanceAfter;
            this.reducedMotion = reducedMotion;
            total = this.lines.Sum(l => l.Amount);

            AutoSizeAxes = Axes.Both;
            Anchor = Anchor.TopCentre;
            Origin = Anchor.TopCentre;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            appearSample = audio.Samples.Get(@"SongSelect/confirm-selection");

            var content = new List<Drawable> { header() };
            foreach (var row in summarise())
            {
                var d = breakdownRow(row.label, row.amount, row.icon, row.accent);

                // Start hidden + AlwaysPresent (so layout is stable while they fade
                // in) — LoadComplete reveals them staggered, each in its own colour.
                if (!reducedMotion)
                {
                    d.Alpha = 0;
                    d.AlwaysPresent = true;
                }

                revealRows.Add(d);
                content.Add(d);
            }

            // If a top play got soft-capped today, say so (the server only paid the
            // pp bonus on it, not the full rank reward).
            if (lines.Any(l => l.Reason == "top_play" && parseTagInt(l.Ref, "capped:") == 1))
                content.Add(capNote());

            if (balanceAfter > 0)
                content.Add(balanceFooter());

            InternalChildren = new Drawable[]
            {
                new BriefingGlass
                {
                    AutoSizeAxes = Axes.Both,
                    RelativeContentSize = Axes.None,
                    CornerSize = BriefingTheme.CornerLg,
                    SurfaceLift = 1.5f,
                    // Near-solid so the summary reads cleanly over gameplay (no
                    // backdrop blur in the framework; opacity is the readable win).
                    SurfaceOpacity = 1.5f,
                    ShadowColor = BriefingTheme.AccentAmber,
                    ShadowOpacity = 0.4f,
                    ShadowRadius = 28f,
                    Child = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Y,
                        Width = card_width,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                        Margin = new MarginPadding { Horizontal = BriefingTheme.SpacingLg, Vertical = BriefingTheme.SpacingMd },
                        Children = content.ToArray(),
                    },
                },
                // Free-flying coins on exit. BypassAutoSize so the burst never
                // resizes the card while the coins travel.
                coinLayer = new Container
                {
                    BypassAutoSizeAxes = Axes.Both,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                },
            };
        }

        private Drawable header() => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Margin = new MarginPadding { Bottom = 2 },
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(8, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = FontAwesome.Solid.Coins,
                            Size = new Vector2(21),
                            Colour = BriefingTheme.AccentAmber,
                        },
                        totalText = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = $"+{(reducedMotion ? total : 0):N0}",
                            Font = OsuFont.TorusAlternate.With(size: BriefingTheme.TypeTitle, weight: FontWeight.Bold),
                            Colour = BriefingTheme.AccentAmber,
                        },
                    },
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Text = "Points earned",
                    Font = OsuFont.Torus.With(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                },
            },
        };

        private Drawable breakdownRow(string label, int amount, IconUsage icon, Color4 accent) => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(8, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = icon,
                            Size = new Vector2(13),
                            Colour = accent,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = label,
                            Font = OsuFont.Torus.With(size: BriefingTheme.TypeBody),
                            Colour = Color4.White,
                        },
                    },
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Text = $"+{amount:N0}",
                    Font = OsuFont.Torus.With(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                    // Amount in the reason's own colour (daily green, top play gold,
                    // medal pink, ...) so each source reads distinct at a glance.
                    Colour = accent,
                },
            },
        };

        private Drawable balanceFooter() => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Margin = new MarginPadding { Top = 4 },
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Colour = Color4.White.Opacity(0.08f),
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Margin = new MarginPadding { Top = 6 },
                    Text = $"Balance  {balanceAfter:N0}",
                    Font = OsuFont.Torus.With(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                },
            },
        };

        private Drawable capNote() => new OsuSpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = "Daily top-play limit reached — pp bonus only",
            Font = OsuFont.Torus.With(size: BriefingTheme.TypeCaption, italics: true),
            Colour = BriefingTheme.AccentAmber.Opacity(0.85f),
            Margin = new MarginPadding { Top = 2 },
        };

        /// <summary>Fold the raw ledger lines into display rows: medals aggregate
        /// into one, each other reason keeps its own line with a friendly label.</summary>
        private IEnumerable<(string label, int amount, IconUsage icon, Color4 accent)> summarise()
        {
            foreach (var l in lines.Where(l => l.Reason != "medal"))
            {
                var info = PointsReasonInfo.Resolve(l.Reason, l.Ref);
                yield return (labelFor(l), l.Amount, info.Icon, info.Accent);
            }

            var medals = lines.Where(l => l.Reason == "medal").ToList();
            if (medals.Count > 0)
            {
                var info = PointsReasonInfo.Resolve("medal", null);
                int sum = medals.Sum(m => m.Amount);
                yield return (medals.Count == 1 ? "Medal" : $"{medals.Count} medals", sum, info.Icon, info.Accent);
            }
        }

        private static string labelFor(Line l)
        {
            switch (l.Reason)
            {
                case "top_play":
                    int rank = parseTagInt(l.Ref, "rank:");
                    return rank > 0 ? $"Top play #{rank}" : "Top play";

                case "daily_play":
                    int streak = parseTagInt(l.Ref, "streak:");
                    return streak >= 2 ? $"{streak} day streak" : "First play today";

                case "milestone":
                    int pp = parseTagInt(l.Ref, "pp:");
                    return pp > 0 ? $"{pp:N0}pp milestone" : "pp milestone";

                default:
                    return string.IsNullOrEmpty(l.Reason) ? "Points" : char.ToUpperInvariant(l.Reason[0]) + l.Reason.Substring(1).Replace('_', ' ');
            }
        }

        /// <summary>Pull an integer that follows <paramref name="tag"/> in a ledger
        /// ref. The ref is either a bare value (e.g. <c>streak:2</c>) or a pipe list
        /// (e.g. <c>score:ID|rank:3|b:45|pp:20</c>).</summary>
        private static int parseTagInt(string reasonRef, string tag)
        {
            if (string.IsNullOrEmpty(reasonRef))
                return 0;

            foreach (string part in reasonRef.Split('|'))
            {
                if (part.StartsWith(tag, StringComparison.Ordinal)
                    && int.TryParse(part.Substring(tag.Length), out int v))
                    return v;
            }

            return 0;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            appearSample?.Play();

            double entrance = reducedMotion ? 160 : BriefingTheme.EntranceDuration;

            if (reducedMotion)
            {
                this.FadeInFromZero(entrance);
            }
            else
            {
                this.FadeInFromZero(entrance, Easing.OutQuad);
                this.MoveToY(18).MoveToY(0, entrance, Easing.OutBack);
                this.ScaleTo(0.92f).ScaleTo(1, entrance, Easing.OutBack);

                countStart = Time.Current + 140;
                counting = true;

                // Reveal each source line one-by-one (in its own colour) after the
                // card lands, so you still get the "green daily, gold top play" beat
                // even though it's a single card. Alpha-only (rows live in a flow
                // that owns their position) + AlwaysPresent keeps the layout steady.
                for (int i = 0; i < revealRows.Count; i++)
                    revealRows[i].Delay(entrance + i * 150).FadeIn(260, Easing.OutQuint);
            }

            // Hold, then burst coins up toward the balance + fade out.
            Scheduler.AddDelayed(burstCoinsAndExit, entrance + hold);
        }

        protected override void Update()
        {
            base.Update();

            if (!counting)
                return;

            double p = Math.Clamp((Time.Current - countStart) / count_duration, 0, 1);
            double eased = 1 - Math.Pow(1 - p, 3);
            int val = (int)Math.Round(total * eased);

            if (val != displayed)
            {
                displayed = val;
                totalText.Text = $"+{val:N0}";
            }

            if (p >= 1)
                counting = false;
        }

        private void burstCoinsAndExit()
        {
            if (!reducedMotion && coinLayer != null)
            {
                int n = Math.Clamp(2 + total / 40, 4, 10);
                for (int i = 0; i < n; i++)
                {
                    var coin = new SpriteIcon
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Solid.Coins,
                        Size = new Vector2(14),
                        Colour = BriefingTheme.AccentAmber,
                        Y = 14,
                    };
                    coinLayer.Add(coin);

                    // Fly up and out toward the top-right (where the toolbar balance
                    // sits), with a spread + stagger so it reads as a little shower.
                    float dx = 60 + i * 26;
                    float dy = -70 - (i % 3) * 22;
                    coin.Delay(i * 32f)
                        .ScaleTo(1.25f, 120, Easing.OutQuint)
                        .MoveTo(new Vector2(dx, dy), 520, Easing.OutQuint)
                        .FadeOut(520, Easing.OutQuint);
                }
            }

            this.Delay(reducedMotion ? 0 : 120)
                .FadeOut(exit_duration, Easing.OutQuad)
                .ScaleTo(0.95f, exit_duration, Easing.OutQuad)
                .Expire();
        }
    }
}
