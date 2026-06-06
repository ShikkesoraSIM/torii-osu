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

        // Current customisation, mutated by the preset chips below.
        private float curLen = 1f;
        private float curDens = 1f;
        private float curSize = 1f;

        // Preset chips per axis. Length is a 0..1 scale (1 = catalog default);
        // density / size are multipliers within the CosmeticEconomy ranges.
        private static readonly (string name, float value)[] length_presets = { ("Short", 0.35f), ("Normal", 0.7f), ("Long", 1f) };
        private static readonly (string name, float value)[] density_presets = { ("Low", 0.7f), ("Normal", 1f), ("High", 1.35f) };
        private static readonly (string name, float value)[] size_presets = { ("Small", 0.75f), ("Normal", 1f), ("Big", 1.4f) };

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
            (curLen, curDens, curSize) = cosmetics?.GetCustomisation(def.Id) ?? (1f, 1f, 1f);

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
                // Preset chips per axis (clearer than sliders). Density only makes
                // sense for dot / particle trails; a continuous ribbon hides it.
                bool showDensity = def.Family != CosmeticTrailFamily.Ribbon;

                flow.Add(presetRow("Length", length_presets, curLen, v => { curLen = v; applyAll(); }));

                if (showDensity)
                    flow.Add(presetRow("Density", density_presets, curDens, v => { curDens = v; applyAll(); }));

                flow.Add(presetRow("Size", size_presets, curSize, v => { curSize = v; applyAll(); }));

                applyAll();
            }
            else
            {
                bool afford = cosmetics?.CanAfford(CosmeticEconomy.AdjustableLengthUnlock) ?? false;

                flow.Add(new OsuSpriteText
                {
                    Text = "Customise length, density & size:",
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

        /// <summary>A labelled row of preset chips; the chip closest to the
        /// current value is highlighted, tapping one sets the value.</summary>
        private Drawable presetRow(string title, (string name, float value)[] presets, float current, Action<float> onSet)
        {
            var chips = new List<(Box bg, OsuSpriteText text, float value)>();

            var chipFlow = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(6, 0),
            };

            void refresh(float selected)
            {
                // Highlight the chip whose value is closest to the current value.
                float best = presets[0].value;
                foreach (var pr in presets)
                {
                    if (Math.Abs(pr.value - selected) < Math.Abs(best - selected))
                        best = pr.value;
                }

                foreach (var c in chips)
                {
                    bool sel = Math.Abs(c.value - best) < 0.001f;
                    c.bg.FadeColour(sel ? BriefingTheme.AccentPink : Color4.White.Opacity(0.08f), 120, Easing.OutQuint);
                    c.text.FadeColour(sel ? Color4.White : Color4.White.Opacity(BriefingTheme.InkSecondary), 120, Easing.OutQuint);
                }
            }

            foreach (var p in presets)
            {
                Box bg;
                OsuSpriteText txt;
                float val = p.value;

                chipFlow.Add(new OsuClickableContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 6f,
                    Action = () =>
                    {
                        onSet(val);
                        refresh(val);
                    },
                    Children = new Drawable[]
                    {
                        bg = new Box { RelativeSizeAxes = Axes.Both },
                        txt = new OsuSpriteText
                        {
                            Margin = new MarginPadding { Horizontal = 13, Vertical = 6 },
                            Text = p.name,
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                        },
                    },
                });

                chips.Add((bg, txt, val));
            }

            refresh(current);

            return new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[] { label(title), chipFlow },
            };
        }

        private void applyAll() => applyCustom(curLen, curDens, curSize);

        private void applyCustom(float length, float density, float size)
        {
            preview?.ApplyCustomisation(length, density, size);
            cosmetics?.SetCustomisation(def.Id, length, density, size);
        }
    }
}
