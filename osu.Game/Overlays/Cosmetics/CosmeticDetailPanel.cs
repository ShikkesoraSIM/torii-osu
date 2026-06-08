// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
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

        [Resolved(CanBeNull = true)]
        private IDialogOverlay dialogOverlay { get; set; }

        // Current customisation (Length is a 0..1 scale, 1 = catalog default;
        // Density / Size are multipliers within the CosmeticEconomy ranges).
        private float curLen = 1f;
        private float curDens = 1f;
        private float curSize = 1f;

        // When the panel is too short to stack all sliders (e.g. big UI scale),
        // collapse to a single slider plus axis-picker buttons.
        private bool compact;
        private int selectedAxis;

        // Below this panel height the three stacked sliders don't fit, so we go
        // compact (one slider + Length/Density/Size buttons).
        private const float compact_threshold = 470f;

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

            flow.Add(preview = new CosmeticTrailPreview(def, 1.6f)
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
                    Action = () => CosmeticPurchaseDialog.Prompt(dialogOverlay, def.Name, def.Price, () =>
                    {
                        if (cosmetics != null && cosmetics.Buy(def.Id, def.Price))
                        {
                            notify?.Invoke($"Purchased {def.Name}");
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
                buildCustomisation();
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

        /// <summary>Build the length/density/size sliders. Normally all three
        /// are stacked; when the panel is too short (big UI scale) it collapses
        /// to one slider plus Length/Density/Size picker buttons.</summary>
        private void buildCustomisation()
        {
            bool showDensity = def.Family != CosmeticTrailFamily.Ribbon;

            // Length is a 0..1 scale; density/size are multipliers in range.
            var length = new BindableFloat(curLen) { MinValue = 0f, MaxValue = 1.3f, Precision = 0.05f };
            var density = new BindableFloat(curDens) { MinValue = CosmeticEconomy.MinDensityMultiplier, MaxValue = CosmeticEconomy.MaxDensityMultiplier, Precision = 0.05f };
            var size = new BindableFloat(curSize) { MinValue = CosmeticEconomy.MinSizeMultiplier, MaxValue = CosmeticEconomy.MaxSizeMultiplier, Precision = 0.05f };

            length.BindValueChanged(v => { curLen = v.NewValue; applyAll(); });
            density.BindValueChanged(v => { curDens = v.NewValue; applyAll(); });
            size.BindValueChanged(v => { curSize = v.NewValue; applyAll(); });

            var axes = new List<(string name, BindableFloat bindable)> { ("Length", length) };
            if (showDensity)
                axes.Add(("Density", density));
            axes.Add(("Size", size));

            if (!compact)
            {
                foreach (var a in axes)
                {
                    flow.Add(label(a.name));
                    flow.Add(new RoundedSliderBar<float> { RelativeSizeAxes = Axes.X, Current = a.bindable });
                }
            }
            else
            {
                selectedAxis = Math.Clamp(selectedAxis, 0, axes.Count - 1);
                flow.Add(axisSelector(axes));
                flow.Add(label(axes[selectedAxis].name));
                flow.Add(new RoundedSliderBar<float> { RelativeSizeAxes = Axes.X, Current = axes[selectedAxis].bindable });
            }

            applyAll();
        }

        /// <summary>The Length / Density / Size picker shown in compact mode.
        /// Equal flex columns with absolute gap columns between them, so buttons
        /// fill their cell exactly (no margin overflow clipping the last one).</summary>
        private Drawable axisSelector(List<(string name, BindableFloat bindable)> axes)
        {
            var cells = new List<Drawable>();
            var cols = new List<Dimension>();

            for (int i = 0; i < axes.Count; i++)
            {
                if (i > 0)
                {
                    cols.Add(new Dimension(GridSizeMode.Absolute, 6));
                    cells.Add(Empty());
                }

                cols.Add(new Dimension());

                int index = i;
                bool sel = index == selectedAxis;

                cells.Add(new OsuClickableContainer
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 30,
                    Masking = true,
                    CornerRadius = 6f,
                    Action = () =>
                    {
                        selectedAxis = index;
                        rebuild();
                    },
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = sel ? BriefingTheme.AccentPink : Color4.White.Opacity(0.08f) },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = axes[index].name,
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                            Colour = sel ? Color4.White : Color4.White.Opacity(BriefingTheme.InkSecondary),
                        },
                    },
                });
            }

            return new GridContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                ColumnDimensions = cols.ToArray(),
                RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                Content = new[] { cells.ToArray() },
            };
        }

        protected override void Update()
        {
            base.Update();

            if (flow == null)
                return;

            // Switch to the compact one-slider layout when the panel is too short
            // for three stacked sliders (e.g. at a large UI scale).
            bool wantCompact = DrawHeight > 0 && DrawHeight < compact_threshold;

            if (wantCompact != compact)
            {
                compact = wantCompact;
                rebuild();
            }
        }

        private void applyAll() => applyCustom(curLen, curDens, curSize);

        private void applyCustom(float length, float density, float size)
        {
            preview?.ApplyCustomisation(length, density, size);
            cosmetics?.SetCustomisation(def.Id, length, density, size);
        }
    }
}
