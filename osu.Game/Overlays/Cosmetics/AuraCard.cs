// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>Inventory/store tile for a user aura: shows a sample username with
    /// the aura's glow + particles playing behind it, plus a name / earned /
    /// equipped footer. Click equips it (Inventory) or selects it (Store).</summary>
    public partial class AuraCard : OsuClickableContainer, IStoreCard
    {
        public string ItemId => preset.AuraId;

        private readonly AuraPreset preset;
        private readonly bool startSelected;

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        private Container content;
        private Container selectionBorder;
        private Container footerHolder;
        private Container badgeHolder;
        private Box hoverHighlight;

        public AuraCard(AuraPreset preset, bool selected = false)
        {
            this.preset = preset;
            startSelected = selected;
            Size = new Vector2(244, 168);
        }

        private bool isEquipped => api?.LocalUser.Value != null
                                   && AuraRegistry.ResolveForUser(api.LocalUser.Value)?.AuraId == preset.AuraId;

        [BackgroundDependencyLoader]
        private void load()
        {
            bool equipped = isEquipped;
            string username = api?.LocalUser.Value?.Username ?? "Aura";

            // A throwaway user with this aura equipped, so the shared
            // UserAuraContainer renders the real glow + particles in the preview.
            var sampleUser = new APIUser { Id = -1, Username = username, EquippedAura = preset.AuraId };

            Child = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Children = new Drawable[]
                {
                    new BriefingGlass
                    {
                        RelativeSizeAxes = Axes.Both,
                        RelativeContentSize = Axes.Both,
                        CornerSize = BriefingTheme.CornerSm,
                        SurfaceLift = 1.2f,
                        ShadowOpacity = 0.2f,
                        ShadowRadius = 10f,
                        Child = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = BriefingTheme.CornerSm,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(16, 16, 24, 255) },
                                // Sample name with the aura playing behind it.
                                new Container
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    AutoSizeAxes = Axes.Both,
                                    Y = -10,
                                    Child = UserAuraContainer.Wrap(sampleUser, new OsuSpriteText
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Text = username,
                                        Font = OsuFont.GetFont(size: 22, weight: FontWeight.SemiBold),
                                    }),
                                },
                                new Box
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 70,
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.BottomLeft,
                                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0f), Color4.Black.Opacity(0.82f)),
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.BottomLeft,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 3),
                                    Padding = new MarginPadding { Horizontal = 12, Bottom = 10 },
                                    Children = new Drawable[]
                                    {
                                        new TruncatingSpriteText
                                        {
                                            Text = displayName,
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                                            RelativeSizeAxes = Axes.X,
                                        },
                                        footerHolder = new Container
                                        {
                                            AutoSizeAxes = Axes.Both,
                                            Child = createFooter(equipped),
                                        },
                                    },
                                },
                                hoverHighlight = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = Color4.White.Opacity(0.06f),
                                    Blending = BlendingParameters.Additive,
                                    Alpha = 0,
                                },
                            },
                        },
                    },
                    badgeHolder = new Container
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        AutoSizeAxes = Axes.Both,
                        Child = badge(equipped),
                    },
                    // Auras are "earned" — give them the rare amber border.
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = BriefingTheme.CornerSm,
                        BorderThickness = 2.5f,
                        BorderColour = BriefingTheme.AccentAmber.Opacity(0.65f),
                        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
                    },
                    selectionBorder = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = BriefingTheme.CornerSm,
                        BorderThickness = 2.5f,
                        BorderColour = BriefingTheme.AccentPink,
                        Alpha = 0,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
                    },
                },
            };

            if (startSelected)
                selectionBorder.Alpha = 1;
        }

        private Drawable createFooter(bool equipped)
        {
            Color4 footerColour = equipped ? BriefingTheme.AccentGain : BriefingTheme.AccentAmber;

            var row = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(6, 0),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "AURA",
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Colour = BriefingTheme.AccentAmber,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "·",
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = equipped ? "EQUIPPED" : "EARNED",
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Colour = footerColour,
                    },
                },
            };

            return row;
        }

        private Drawable badge(bool equipped) => new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Margin = new MarginPadding(8),
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 5f,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = (equipped ? BriefingTheme.AccentGain : BriefingTheme.AccentAmber).Opacity(0.92f),
                },
                new OsuSpriteText
                {
                    Margin = new MarginPadding { Horizontal = 7, Vertical = 3 },
                    Text = equipped ? "EQUIPPED" : "EARNED",
                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                    Colour = Color4.Black.Opacity(0.85f),
                },
            },
        };

        public void SetSelected(bool selected) => selectionBorder?.FadeTo(selected ? 1 : 0, 140, Easing.OutQuint);

        public void RefreshState()
        {
            if (footerHolder == null)
                return;

            bool equipped = isEquipped;
            footerHolder.Child = createFooter(equipped);
            badgeHolder.Child = badge(equipped);
        }

        protected override bool OnHover(HoverEvent e)
        {
            content.ScaleTo(1.035f, 220, Easing.OutQuint);
            hoverHighlight.FadeTo(1, 160, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            content.ScaleTo(1f, 260, Easing.OutQuint);
            hoverHighlight.FadeTo(0, 220, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        private string displayName => DisplayNameFor(preset.AuraId);

        // Server catalog owns the canonical names; offline we derive a friendly
        // label from the aura id ("admin-embers" -> "Admin Embers").
        public static string DisplayNameFor(string auraId)
        {
            string[] parts = (auraId ?? string.Empty).Split('-', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts.Select(p => p.Length == 0
                ? p
                : char.ToUpper(p[0], CultureInfo.InvariantCulture) + p.Substring(1)));
        }
    }
}
