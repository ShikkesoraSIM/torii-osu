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
using osu.Game.Graphics.Containers;
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
            // Scrollable so a tall set of controls (or a small window) can never
            // clip the bottom slider. No visible bar; it just rescues overflow.
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

            bool owned = cosmetics?.IsOwned(def.Id) ?? false;
            bool equipped = cosmetics != null && cosmetics.EquippedTrailId.Value == def.Id;
            var (curLen, curDens, curSize) = cosmetics?.GetCustomisation(def.Id) ?? (1f, 1f, 1f);

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

            // One toggle button: pink "Equip" when not equipped, a muted blue
            // "Unequip" when it is (the label/colour carry the state, so we don't
            // need a separate disabled "Equipped" button taking up room).
            flow.Add(new RoundedButton
            {
                RelativeSizeAxes = Axes.X,
                Height = 40,
                Text = equipped ? "Unequip" : "Equip",
                BackgroundColour = equipped ? BriefingTheme.AccentSky : BriefingTheme.AccentPink,
                Action = () =>
                {
                    if (equipped)
                        cosmetics?.Unequip();
                    else
                        cosmetics?.Equip(def.Id);

                    notify?.Invoke(equipped ? $"Unequipped {def.Name}" : $"Equipped {def.Name}");
                    rebuild();
                },
            });

            if (cosmetics != null && cosmetics.AdjustUnlocked)
            {
                // Length is a 0..1 scale (1 = catalog default, 0 = a fixed short
                // floor shared by every trail). Density only makes sense for
                // dot / particle trails; a continuous ribbon hides it.
                bool showDensity = def.Family != CosmeticTrailFamily.Ribbon;

                var length = new BindableFloat(curLen) { MinValue = 0f, MaxValue = 1f, Precision = 0.05f };
                var density = new BindableFloat(curDens) { MinValue = CosmeticEconomy.MinDensityMultiplier, MaxValue = CosmeticEconomy.MaxDensityMultiplier, Precision = 0.05f };
                var size = new BindableFloat(curSize) { MinValue = CosmeticEconomy.MinSizeMultiplier, MaxValue = CosmeticEconomy.MaxSizeMultiplier, Precision = 0.05f };

                void apply() => applyCustom(length.Value, density.Value, size.Value);

                length.BindValueChanged(_ => apply());
                density.BindValueChanged(_ => apply());
                size.BindValueChanged(_ => apply());

                flow.Add(label("Length"));
                flow.Add(new RoundedSliderBar<float> { RelativeSizeAxes = Axes.X, Current = length });

                if (showDensity)
                {
                    flow.Add(label("Density"));
                    flow.Add(new RoundedSliderBar<float> { RelativeSizeAxes = Axes.X, Current = density });
                }

                flow.Add(label("Size"));
                flow.Add(new RoundedSliderBar<float> { RelativeSizeAxes = Axes.X, Current = size });

                apply();
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

        private void applyCustom(float length, float density, float size)
        {
            preview?.ApplyCustomisation(length, density, size);
            cosmetics?.SetCustomisation(def.Id, length, density, size);
        }
    }
}
