// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserInterfaceV2
{
    /// <summary>
    /// Settings-form control for picking a single hue degree (0–359).
    /// </summary>
    /// <remarks>
    /// The popover deliberately exposes a HUE-ONLY picker (the upstream
    /// <see cref="OsuHSVColourPicker"/> includes a saturation/value square that
    /// the consumer of this control silently throws away — moving the marker
    /// inside that square produced no observable change in the actual UI hue,
    /// which felt like "the picker is broken"). A horizontal hue strip with a
    /// draggable nub gives the user 1:1 mapping between input and output, and
    /// a live-updating hex code below replaces the previously meaningless
    /// "23°" label.
    /// </remarks>
    public partial class FormHuePicker : CompositeDrawable, IHasCurrentValue<float>, IFormControl
    {
        public Bindable<float> Current
        {
            get => current.Current;
            set => current.Current = value;
        }

        private readonly BindableNumberWithCurrent<float> current = new BindableNumberWithCurrent<float>
        {
            MinValue = 0,
            MaxValue = 359,
            Precision = 1,
            Default = 300,
        };

        public LocalisableString Caption { get; init; }
        public LocalisableString HintText { get; init; }

        private FormControlBackground background = null!;
        private FormFieldCaption captionText = null!;
        private HueSwatchButton swatchButton = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChildren = new Drawable[]
            {
                background = new FormControlBackground(),
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding(9),
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Padding = new MarginPadding { Right = 130 },
                            Child = captionText = new FormFieldCaption
                            {
                                Caption = Caption,
                                TooltipText = HintText,
                            },
                        },
                        swatchButton = new HueSwatchButton
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            CurrentHue = { BindTarget = current },
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            colourProvider.ColoursChanged += updateState;

            current.BindValueChanged(_ =>
            {
                updateState();
                ValueChanged?.Invoke();
            }, true);

            current.BindDisabledChanged(_ => updateState(), true);
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateState();
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            base.OnHoverLost(e);
            updateState();
        }

        private void updateState()
        {
            captionText.Colour = current.Disabled ? colourProvider.Background1 : colourProvider.Content2;
            swatchButton.Disabled = current.Disabled;

            if (current.Disabled)
                background.VisualStyle = VisualStyle.Disabled;
            else if (IsHovered)
                background.VisualStyle = VisualStyle.Hovered;
            else
                background.VisualStyle = VisualStyle.Normal;
        }

        public IEnumerable<LocalisableString> FilterTerms => new[] { Caption, HintText };

        public event Action? ValueChanged;

        public bool IsDefault => Current.IsDefault;

        public void SetDefault() => Current.SetDefault();

        public bool IsDisabled => Current.Disabled;

        public float MainDrawHeight => DrawHeight;

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
                colourProvider.ColoursChanged -= updateState;

            base.Dispose(isDisposing);
        }

        // -----------------------------------------------------------------
        // Swatch button — clickable preview that opens the popover.
        // Shows the live hex code instead of the previous "23°" label,
        // since hex is what users recognise from every other colour tool.
        // -----------------------------------------------------------------
        private partial class HueSwatchButton : OsuClickableContainer, IHasPopover
        {
            public Bindable<float> CurrentHue { get; } = new Bindable<float>();

            public bool Disabled
            {
                set => Alpha = value ? 0.5f : 1f;
            }

            private Box fill = null!;
            private OsuSpriteText label = null!;

            [BackgroundDependencyLoader]
            private void load()
            {
                Size = new Vector2(120, 36);
                Action = this.ShowPopover;
                CornerRadius = 8;
                Masking = true;

                Children = new Drawable[]
                {
                    fill = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                    label = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.Default.With(weight: "Bold"),
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                CurrentHue.BindValueChanged(_ => updateState(), true);
            }

            private void updateState()
            {
                var colour = Colour4.FromHSV(normaliseHue(CurrentHue.Value) / 360f, 1f, 1f);
                fill.Colour = colour;
                label.Colour = OsuColour.ForegroundTextColourFor(colour);
                label.Text = colourToHex(colour);
            }

            public Popover GetPopover() => new HueOnlyPickerPopover
            {
                CurrentHue = { BindTarget = CurrentHue },
            };
        }

        // -----------------------------------------------------------------
        // Popover hosting the hue-only picker. Replaces the previous
        // OsuColourPicker (HSV + hex tabs) — that picker silently let the
        // user move sat/val even though we throw those values away, which
        // produced the "bottom-half does nothing" complaint.
        // -----------------------------------------------------------------
        private partial class HueOnlyPickerPopover : OsuPopover
        {
            public Bindable<float> CurrentHue { get; } = new Bindable<float>();

            public HueOnlyPickerPopover()
                : base(false)
            {
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                Body.BorderThickness = 2;
                Body.BorderColour = colourProvider.Highlight1;
                Content.Padding = new MarginPadding(8);

                Child = new HueOnlyPicker(colourProvider)
                {
                    Width = 280,
                    CurrentHue = { BindTarget = CurrentHue },
                };
            }
        }

        // -----------------------------------------------------------------
        // The actual hue-only picker: horizontal hue strip + draggable nub
        // + live hex readout. Built on top of osu-framework's HueSelector
        // so we get the proper HueSelectorBackground shader (smooth rainbow
        // gradient) for free.
        // -----------------------------------------------------------------
        private partial class HueOnlyPicker : CompositeDrawable
        {
            public Bindable<float> CurrentHue { get; } = new Bindable<float>();

            private readonly OverlayColourProvider colourProvider;
            private InlineHueSelector selector = null!;
            private OsuSpriteText hexLabel = null!;

            public HueOnlyPicker(OverlayColourProvider colourProvider)
            {
                this.colourProvider = colourProvider;
                AutoSizeAxes = Axes.Y;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                    Children = new Drawable[]
                    {
                        selector = new InlineHueSelector(),
                        hexLabel = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.Default.With(size: 16, weight: "SemiBold"),
                            Colour = colourProvider.Content1,
                        },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // selector.Hue is normalized 0..1; CurrentHue is degrees 0..359.
                // Bridge the two without recursion via a syncing flag.
                bool syncing = false;

                selector.Hue.BindValueChanged(h =>
                {
                    if (syncing) return;
                    syncing = true;
                    CurrentHue.Value = normaliseHue(h.NewValue * 360f);
                    syncing = false;

                    updateLabel();
                });

                CurrentHue.BindValueChanged(h =>
                {
                    if (syncing) return;
                    syncing = true;
                    selector.Hue.Value = normaliseHue(h.NewValue) / 360f;
                    syncing = false;

                    updateLabel();
                }, true);
            }

            private void updateLabel()
            {
                var colour = Colour4.FromHSV(normaliseHue(CurrentHue.Value) / 360f, 1f, 1f);
                hexLabel.Text = colourToHex(colour);
            }
        }

        // Concrete implementation of the framework's abstract HueSelector
        // with our nub style. (HueSelector is abstract and requires CreateSliderNub.)
        private partial class InlineHueSelector : HSVColourPicker.HueSelector
        {
            public InlineHueSelector()
            {
                SliderBar.CornerRadius = 8;
                SliderBar.Masking = true;
            }

            protected override Drawable CreateSliderNub() => new Nub(this);

            private partial class Nub : CompositeDrawable
            {
                private readonly Bindable<float> hue;
                private readonly Box fill;

                public Nub(InlineHueSelector selector)
                {
                    hue = selector.Hue.GetBoundCopy();

                    InternalChild = new CircularContainer
                    {
                        Width = 12,
                        Height = 38,
                        Origin = Anchor.Centre,
                        Anchor = Anchor.Centre,
                        Masking = true,
                        BorderColour = Colour4.White,
                        BorderThickness = 3,
                        EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                        {
                            Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                            Offset = new Vector2(0, 1),
                            Radius = 3,
                            Colour = Colour4.Black.Opacity(0.3f),
                        },
                        Child = fill = new Box { RelativeSizeAxes = Axes.Both },
                    };
                }

                protected override void LoadComplete()
                {
                    hue.BindValueChanged(h => fill.Colour = Colour4.FromHSV(h.NewValue, 1f, 1f), true);
                }
            }
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------
        private static float normaliseHue(float hue)
        {
            float normalised = hue % 360f;

            if (normalised < 0)
                normalised += 360f;

            return normalised;
        }

        private static string colourToHex(Colour4 colour)
        {
            int r = (int)Math.Round(colour.R * 255);
            int g = (int)Math.Round(colour.G * 255);
            int b = (int)Math.Round(colour.B * 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }
}
