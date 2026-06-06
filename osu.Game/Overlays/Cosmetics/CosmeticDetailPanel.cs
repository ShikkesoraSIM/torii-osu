// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>Right-side detail panel: live preview, name/tier/price, buy or
    /// equip, and (once the customisation unlock is bought) length + density
    /// sliders.</summary>
    public partial class CosmeticDetailPanel : Container
    {
        private readonly CosmeticTrailDefinition def;
        private readonly ToriiCosmeticsManager cosmetics;
        private readonly Action<string> notify;

        private FillFlowContainer flow;
        private CosmeticTrailPreview preview;

        public CosmeticDetailPanel(CosmeticTrailDefinition def, ToriiCosmeticsManager cosmetics, Action<string> notify)
        {
            this.def = def;
            this.cosmetics = cosmetics;
            this.notify = notify;
            RelativeSizeAxes = Axes.Both;
            Padding = new MarginPadding(BriefingTheme.SpacingMd);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, BriefingTheme.SpacingMd),
            };

            rebuild();
        }

        private void rebuild()
        {
            flow.Clear();

            bool owned = cosmetics?.IsOwned(def.Id) ?? false;
            bool equipped = cosmetics != null && cosmetics.EquippedTrailId.Value == def.Id;
            var (curLen, curDens) = cosmetics?.GetCustomisation(def.Id) ?? (1f, 1f);

            flow.Add(preview = new CosmeticTrailPreview(def)
            {
                RelativeSizeAxes = Axes.X,
                Height = 150,
            });

            flow.Add(new OsuSpriteText
            {
                Text = def.Name,
                Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
            });

            flow.Add(new OsuSpriteText
            {
                Text = $"{def.Tier} · {def.Price:N0} pts",
                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody),
                Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
            });

            if (!owned)
            {
                bool afford = cosmetics?.CanAfford(def.Price) ?? false;

                flow.Add(new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40,
                    Text = $"Buy  ·  {def.Price:N0} pts",
                    BackgroundColour = BriefingTheme.AccentPink,
                    Enabled = { Value = afford },
                    Action = () =>
                    {
                        if (cosmetics != null && cosmetics.Buy(def.Id, def.Price))
                        {
                            notify?.Invoke($"Purchased {def.Name}");
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
                Text = equipped ? "Equipped ✓" : "Equip",
                BackgroundColour = equipped ? BriefingTheme.AccentGain : BriefingTheme.AccentPink,
                Enabled = { Value = !equipped },
                Action = () =>
                {
                    cosmetics?.Equip(def.Id);
                    notify?.Invoke($"Equipped {def.Name}");
                    rebuild();
                },
            });

            if (equipped)
            {
                flow.Add(new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 32,
                    Text = "Unequip",
                    Action = () =>
                    {
                        cosmetics?.Unequip();
                        rebuild();
                    },
                });
            }

            if (cosmetics != null && cosmetics.AdjustUnlocked)
            {
                var length = new BindableFloat(curLen) { MinValue = 0.5f, MaxValue = 2f, Precision = 0.05f };
                var density = new BindableFloat(curDens) { MinValue = 0.5f, MaxValue = 2f, Precision = 0.05f };

                length.BindValueChanged(_ => applyCustom(length.Value, density.Value));
                density.BindValueChanged(_ => applyCustom(length.Value, density.Value));

                flow.Add(label("Length"));
                flow.Add(new RoundedSliderBar<float> { RelativeSizeAxes = Axes.X, Current = length });
                flow.Add(label("Density"));
                flow.Add(new RoundedSliderBar<float> { RelativeSizeAxes = Axes.X, Current = density });

                applyCustom(curLen, curDens);
            }
            else
            {
                bool afford = cosmetics?.CanAfford(CosmeticEconomy.AdjustableLengthUnlock) ?? false;

                flow.Add(new OsuSpriteText
                {
                    Text = "Customise length & density:",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                });

                flow.Add(new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 36,
                    Text = $"Unlock  ·  {CosmeticEconomy.AdjustableLengthUnlock:N0} pts",
                    BackgroundColour = BriefingTheme.AccentSky,
                    Enabled = { Value = afford },
                    Action = () =>
                    {
                        if (cosmetics != null && cosmetics.BuyAdjustUnlock(CosmeticEconomy.AdjustableLengthUnlock))
                        {
                            notify?.Invoke("Customisation unlocked");
                            rebuild();
                        }
                    },
                });
            }
        }

        private OsuSpriteText label(string text) => new OsuSpriteText
        {
            Text = text,
            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
        };

        private void applyCustom(float length, float density)
        {
            preview?.ApplyCustomisation(length, density);
            cosmetics?.SetCustomisation(def.Id, length, density);
        }
    }
}
