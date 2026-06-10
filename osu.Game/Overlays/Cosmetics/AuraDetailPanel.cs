// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>Right-side detail panel for a user aura: big live preview of your
    /// name wearing the aura, plus buy (buyable auras) / equip / unequip. Equip
    /// is delegated to the overlay so the whole game re-resolves in place.</summary>
    public partial class AuraDetailPanel : Container
    {
        private readonly AuraPreset preset;
        private readonly int? price;
        private readonly CosmeticTier tier;
        private readonly string displayName;
        private readonly ToriiCosmeticsManager cosmetics;
        private readonly Action<string> equip;
        private readonly Action unequip;
        private readonly Action<string> notify;

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private IDialogOverlay dialogOverlay { get; set; }

        private FillFlowContainer flow;

        private bool earned => price == null;
        private string title => string.IsNullOrEmpty(displayName) ? AuraCard.DisplayNameFor(preset.AuraId) : displayName;

        public AuraDetailPanel(AuraPreset preset, int? price, CosmeticTier tier, string displayName, ToriiCosmeticsManager cosmetics,
                               Action<string> equip, Action unequip, Action<string> notify)
        {
            this.preset = preset;
            this.price = price;
            this.tier = tier;
            this.displayName = displayName;
            this.cosmetics = cosmetics;
            this.equip = equip;
            this.unequip = unequip;
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

        private bool isEquipped => api?.LocalUser.Value != null
                                   && AuraRegistry.ResolveForUser(api.LocalUser.Value)?.AuraId == preset.AuraId;

        private void rebuild()
        {
            flow.Clear();

            bool owned = earned || (cosmetics?.IsOwned(preset.AuraId) ?? false);
            bool equipped = isEquipped;
            string username = api?.LocalUser.Value?.Username ?? "Aura";

            var sampleUser = new APIUser { Id = -1, Username = username, EquippedAura = preset.AuraId };

            flow.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 100,
                Masking = true,
                CornerRadius = BriefingTheme.CornerSm,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(16, 16, 24, 255) },
                    new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Child = UserAuraContainer.Wrap(sampleUser, new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = username,
                            Font = OsuFont.GetFont(size: 30, weight: FontWeight.SemiBold),
                        }),
                    },
                },
            });

            flow.Add(new OsuSpriteText
            {
                Text = title,
                Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
            });

            flow.Add(new OsuSpriteText
            {
                Text = earned ? "Aura · Earned (granted)" : $"{tier} · {price:N0} pts",
                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody),
                Colour = earned ? BriefingTheme.AccentAmber : Color4.White.Opacity(BriefingTheme.InkSecondary),
            });

            flow.Add(new OsuSpriteText
            {
                Text = "Shows everywhere your name appears.",
                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
            });

            if (!owned)
            {
                bool afford = cosmetics?.CanAfford(price ?? 0) ?? false;

                flow.Add(new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40,
                    Text = $"Buy  ·  {price:N0} pts",
                    BackgroundColour = BriefingTheme.AccentPink,
                    Enabled = { Value = afford },
                    Action = () => CosmeticPurchaseDialog.Prompt(dialogOverlay, title, price ?? 0, () =>
                    {
                        if (cosmetics != null && cosmetics.Buy(preset.AuraId, price ?? 0))
                        {
                            notify?.Invoke($"Purchased {title}");
                            // Auto-equip on purchase so the buy feels immediate.
                            equip?.Invoke(preset.AuraId);
                            rebuild();
                        }
                    }),
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
                        unequip?.Invoke();
                    else
                        equip?.Invoke(preset.AuraId);

                    notify?.Invoke(equipped
                        ? $"Unequipped {title}"
                        : $"Equipped {title}");
                    rebuild();
                },
            });
        }
    }
}
