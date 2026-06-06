// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Text;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.Chat;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// Torii-glass briefing shown when a login is rejected because the account is
    /// restricted. A restricted account 403s on every normal endpoint, so without
    /// this the client just bounced the user back to the login form with no real
    /// explanation. APIAccess detects the restriction (via the dedicated
    /// /api/v2/torii/restriction endpoint, which is the one thing a restricted user
    /// can still read) and stashes it on <see cref="IAPIProvider.LastLoginError"/>
    /// as a <see cref="RestrictedAccountException"/>; this overlay watches the API
    /// state and pops the briefing whenever that happens.
    /// </summary>
    public partial class ToriiRestrictionOverlay : VisibilityContainer, IKeyBindingHandler<GlobalAction>
    {
        private const string discord_url = @"https://discord.gg/fZXsZFT5Xv";

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        // For opening the Discord invite. canBeNull for test scenes without a full game.
        [Resolved(canBeNull: true)]
        private OsuGame? game { get; set; }

        private Container panel = null!;
        private FillFlowContainer content = null!;

        // Guards against re-popping for the same restriction event (the state
        // bindable can fire more than once for a single login attempt).
        private RestrictedAccountException? lastShown;

        public ToriiRestrictionOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        // Only swallow input while actually visible (this lives in the always-present topmost layer).
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => State.Value == Visibility.Visible;

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.6f,
                },
                panel = new BriefingGlass
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 560,
                    AutoSizeAxes = Axes.Y,
                    CornerSize = BriefingTheme.CornerLg,
                    SpecularStrength = 0.18f,
                    SpecularHeight = 70,
                    ShadowOpacity = 0.4f,
                    ShadowRadius = 30,
                    RelativeContentSize = Axes.X,
                    Child = content = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                        Padding = new MarginPadding(BriefingTheme.SpacingXl),
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // LastLoginError is set (to a RestrictedAccountException) right before the
            // API flips to Offline, so reacting to state changes catches it. Marshalled
            // onto the update thread because the state bindable fires off-thread.
            api.State.BindValueChanged(_ => Schedule(evaluate), true);
        }

        private void evaluate()
        {
            if (api.LastLoginError is RestrictedAccountException rex && rex != lastShown)
            {
                lastShown = rex;
                buildContent(rex.Restriction);
                Show();
            }
        }

        private void buildContent(APIToriiUserRestriction restriction)
        {
            content.Clear();

            var body = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(restriction.Reason))
                body.Append($"Reason: {restriction.Reason}\n\n");

            if (restriction.Permanent)
                body.Append("This restriction is currently permanent.\n\n");
            else if (restriction.EndsAt is DateTimeOffset ends)
                body.Append($"It is scheduled to lift on {ends.ToLocalTime():dd MMM yyyy, HH:mm}.\n\n");

            body.Append("This can be a safety measure or simply while staff look into something, "
                        + "and is not necessarily permanent. If you think this is a mistake or want "
                        + "to appeal, reach out to the admins on our Discord.");

            content.AddRange(new Drawable[]
            {
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = FontAwesome.Solid.ToriiGate,
                            Size = new Vector2(BriefingTheme.TypeBody),
                            Colour = BriefingTheme.AccentPink,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "TORII",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                            Colour = BriefingTheme.AccentPink,
                        },
                    },
                },
                new OsuSpriteText
                {
                    Text = "Your account is restricted",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                },
                new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    Text = body.ToString(),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Y,
                    RelativeSizeAxes = Axes.X,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(BriefingTheme.SpacingMd, 0),
                    Children = new Drawable[]
                    {
                        new ActionButton(BriefingTheme.AccentPink, primary: true)
                        {
                            Width = 0.55f,
                            RelativeSizeAxes = Axes.X,
                            Height = 44,
                            LabelText = "Join our Discord",
                            Action = () => game?.OpenUrlExternally(discord_url, LinkWarnMode.NeverWarn),
                        },
                        new ActionButton(Color4.White, primary: false)
                        {
                            Width = 0.4f,
                            RelativeSizeAxes = Axes.X,
                            Height = 44,
                            LabelText = "Close",
                            Action = Hide,
                        },
                    },
                },
            });
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!panel.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
                Hide();

            return true;
        }

        protected override void PopIn()
        {
            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            panel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack)
                 .MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            panel.ScaleTo(0.97f, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat)
                return false;

            if (e.Action == GlobalAction.Back && State.Value == Visibility.Visible)
            {
                Hide();
                return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        /// <summary>Pill button styled to match the briefing CTA (primary = filled accent).</summary>
        private partial class ActionButton : OsuClickableContainer
        {
            public LocalisableString LabelText { private get; init; }

            private readonly Color4 accent;
            private readonly bool primary;
            private Box background = null!;

            public ActionButton(Color4 accent, bool primary)
            {
                this.accent = accent;
                this.primary = primary;
                Masking = true;
                CornerRadius = BriefingTheme.CornerSm;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = accent,
                        Alpha = primary ? 0.9f : 0.12f,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = LabelText,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                        Colour = primary ? Color4.Black : Color4.White,
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeTo(primary ? 1f : 0.2f, BriefingTheme.HoverDuration, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeTo(primary ? 0.9f : 0.12f, BriefingTheme.HoverDuration, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
