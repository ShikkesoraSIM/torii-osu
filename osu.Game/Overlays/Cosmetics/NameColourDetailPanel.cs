// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>Right-side detail panel for a username colour: big live preview
    /// of your name in the colour, plus buy / equip.</summary>
    public partial class NameColourDetailPanel : Container
    {
        private readonly CosmeticNameColour colour;
        private readonly ToriiCosmeticsManager cosmetics;
        private readonly Action<string> notify;

        private FillFlowContainer flow;

        public NameColourDetailPanel(CosmeticNameColour colour, ToriiCosmeticsManager cosmetics, Action<string> notify)
        {
            this.colour = colour;
            this.cosmetics = cosmetics;
            this.notify = notify;
            RelativeSizeAxes = Axes.Both;
            Padding = new MarginPadding(BriefingTheme.SpacingMd);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = new OsuScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                ScrollbarVisible = false,
                Child = flow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                },
            };

            rebuild();
        }

        private void rebuild()
        {
            flow.Clear();

            bool owned = cosmetics?.IsOwned(colour.Id) ?? false;
            bool equipped = cosmetics != null && cosmetics.EquippedNameColourId.Value == colour.Id;

            flow.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 100,
                Masking = true,
                CornerRadius = BriefingTheme.CornerSm,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(16, 16, 24, 255) },
                    new NameColourText(colour, 30f)
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                },
            });

            flow.Add(new OsuSpriteText
            {
                Text = colour.Name,
                Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
            });

            flow.Add(new OsuSpriteText
            {
                Text = $"{colour.Tier} · {colour.Price:N0} pts",
                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody),
                Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
            });

            if (!owned)
            {
                bool afford = cosmetics?.CanAfford(colour.Price) ?? false;

                flow.Add(new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40,
                    Text = $"Buy  ·  {colour.Price:N0} pts",
                    BackgroundColour = BriefingTheme.AccentPink,
                    Enabled = { Value = afford },
                    Action = () =>
                    {
                        if (cosmetics != null && cosmetics.Buy(colour.Id, colour.Price))
                        {
                            notify?.Invoke($"Purchased {colour.Name}");
                            rebuild();
                        }
                    },
                });

                if (!afford)
                {
                    flow.Add(new OsuSpriteText
                    {
                        Text = "Not enough points yet.",
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                        Colour = BriefingTheme.AccentLoss,
                    });
                }

                return;
            }

            flow.Add(new RoundedButton
            {
                RelativeSizeAxes = Axes.X,
                Height = 40,
                Text = equipped ? "Unequip" : "Equip",
                BackgroundColour = equipped ? BriefingTheme.AccentSky : BriefingTheme.AccentPink,
                Action = () =>
                {
                    if (equipped)
                        cosmetics?.UnequipNameColour();
                    else
                        cosmetics?.EquipNameColour(colour.Id);

                    notify?.Invoke(equipped ? $"Unequipped {colour.Name}" : $"Equipped {colour.Name}");
                    rebuild();
                },
            });
        }
    }
}
