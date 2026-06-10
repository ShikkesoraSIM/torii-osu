// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.Cosmetics;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// Always-visible currency pill in the toolbar's right cluster: a coin glyph
    /// plus the player's live Torii points balance. Clicking it opens the
    /// <see cref="PointsHistoryOverlay"/>. Mirrors the height / corner radius /
    /// glass-y look of <see cref="ToriiServerPulseButton"/> so it reads as a sibling.
    /// Collapses out of the toolbar (alpha 0 → not laid out by the FillFlow) when
    /// the user is signed out.
    /// </summary>
    public partial class ToolbarPointsButton : OsuClickableContainer, IHasTooltip
    {
        // Warm coin-gold, distinct from the pulse widget's vermillion so the two
        // pills read apart at a glance.
        private static readonly Color4 coin_gold = new Color4(245, 197, 66, 255);

        private const float pill_height = 32f;
        private const float pill_corner_radius = 12f;

        public LocalisableString TooltipText => "Your Torii points — click for history";

        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private PointsHistoryOverlay history { get; set; }

        private readonly Bindable<int> balance = new Bindable<int>();
        private readonly IBindable<APIState> apiState = new Bindable<APIState>();

        private Box hoverGlow = null!;
        private OsuSpriteText countText = null!;

        public ToolbarPointsButton()
        {
            AutoSizeAxes = Axes.X;
            Height = pill_height;
            Action = () => history?.ToggleVisibility();
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = new Container
            {
                AutoSizeAxes = Axes.X,
                RelativeSizeAxes = Axes.Y,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                CornerRadius = pill_corner_radius,
                CornerExponent = 2.4f,
                MaskingSmoothness = 1.4f,
                BorderThickness = 1f,
                BorderColour = coin_gold.Opacity(0.45f),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Radius = 8,
                    Roundness = 6,
                    Colour = coin_gold.Opacity(0.16f),
                    Offset = new Vector2(0, 1),
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(28, 24, 16, 230),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = coin_gold.Opacity(0.10f),
                        Blending = BlendingParameters.Additive,
                    },
                    hoverGlow = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = coin_gold.Opacity(0.18f),
                        Blending = BlendingParameters.Additive,
                        Alpha = 0,
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(7, 0),
                        Padding = new MarginPadding { Horizontal = 12 },
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = FontAwesome.Solid.Coins,
                                Size = new Vector2(12),
                                Colour = coin_gold,
                            },
                            countText = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                Text = @"--",
                                Colour = Color4.White,
                            },
                        },
                    },
                },
            };

            if (cosmetics != null)
                balance.BindTo(cosmetics.PointsBalance);
            balance.BindValueChanged(updateBalance, true);

            if (api != null)
            {
                apiState.BindTo(api.State);
                apiState.BindValueChanged(onStateChanged, true);
            }
        }

        private void updateBalance(ValueChangedEvent<int> e) => Schedule(() =>
        {
            countText.Text = e.NewValue.ToString("N0");

            // Tiny pop when the number changes so an earn/spend registers visually.
            countText.ScaleTo(1.16f, 80, Easing.OutQuint)
                     .Then()
                     .ScaleTo(1f, 220, Easing.OutBack);
        });

        private void onStateChanged(ValueChangedEvent<APIState> e) => Schedule(() =>
        {
            // Pill stays always-visible (like the server-pulse pill) so the balance
            // is always in reach; on login refresh the balance AND the owned set so
            // the account's real cosmetics apply (and anything you don't own gets
            // un-equipped — important when switching accounts on one install).
            if (e.NewValue == APIState.Online)
            {
                fetchBalance();
                fetchOwned();
            }
        });

        /// <summary>Pull the authoritative owned set on login so cosmetics are
        /// server-truth per account (the manager un-equips anything not owned).</summary>
        private void fetchOwned()
        {
            if (api?.IsLoggedIn != true)
                return;

            var req = new GetOwnedCosmeticsRequest();
            req.Success += res => Schedule(() =>
            {
                if (cosmetics != null && res != null)
                    cosmetics.SyncOwned(res.Owned ?? System.Array.Empty<string>());
            });
            api.Queue(req);
        }

        /// <summary>Pull the authoritative balance on login so the pill is accurate before
        /// the first play (the points watcher only syncs after a play / at the menu).</summary>
        private void fetchBalance()
        {
            if (api?.IsLoggedIn != true)
                return;

            var req = new GetMyPointsRequest();
            req.Success += res => Schedule(() =>
            {
                if (cosmetics != null && res != null)
                    cosmetics.PointsBalance.Value = res.Balance;
            });
            api.Queue(req);
        }

        protected override bool OnHover(HoverEvent e)
        {
            hoverGlow.FadeTo(0.30f, 200, Easing.OutQuint);
            this.ScaleTo(1.04f, 200, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hoverGlow.FadeTo(0f, 280, Easing.OutQuint);
            this.ScaleTo(1f, 280, Easing.OutQuint);
            base.OnHoverLost(e);
        }
    }
}
