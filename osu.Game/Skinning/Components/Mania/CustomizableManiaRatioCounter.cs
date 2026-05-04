// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Settings;
using osuTK;

namespace osu.Game.Skinning.Components.Mania
{
    /// <summary>
    /// Fully customisable visual for <see cref="ManiaRatioCounter"/>. Boots
    /// as a plain white number — no label, no decoration — but every
    /// surface trait is opt-in via the skin editor's per-component
    /// settings panel:
    ///
    /// - Show label + Label text — toggle the small "RATIO" header above
    ///   the value, edit what it reads.
    /// - Font + Text weight — pick from the typeface family the user
    ///   prefers (Torus, Inter, Venera...). The weight dropdown is
    ///   typeface-aware: when Venera is selected only its three shipped
    ///   weights (Light / Bold / Black) are exposed, because the rest
    ///   silently fall back to Torus and look broken.
    /// - Value colour — colour picker for the number itself. Want the
    ///   Torii pink? set it here. Want pure white? leave default.
    /// - Decimal places — 1-3 decimals after the dot. Some users want
    ///   "2.4", others "2.345".
    /// - Pulse on update — toggle the brief scale-flash on each
    ///   judgement. Off for a calmer HUD.
    ///
    /// All settings live as <see cref="SettingSourceAttribute"/>-decorated
    /// bindables on this class so they appear in the in-game skin
    /// editor's right-side settings panel when the component is
    /// selected. No code change needed for users to tweak them.
    /// </summary>
    /// <remarks>
    /// Renamed from "DefaultManiaRatioCounter" because the lazer convention
    /// is that <c>Default*</c>-prefixed components use the Venera font and
    /// expose ZERO customisation surfaces — they're the minimal, fixed
    /// variants. This counter is the opposite: heavily customisable, and
    /// the Venera font is just one of several typeface choices. Calling
    /// it "Default" was misleading.
    /// </remarks>
    public partial class CustomizableManiaRatioCounter : ManiaRatioCounter
    {
        [SettingSource("Show label", "Show a small header above the ratio value.")]
        public Bindable<bool> ShowLabel { get; } = new BindableBool(false);

        [SettingSource("Label text", "What the header above the value reads. Uppercase recommended.")]
        public Bindable<string> LabelText { get; } = new Bindable<string>(@"RATIO");

        [SettingSource("Font", "Typeface for the ratio number.")]
        public Bindable<Typeface> Font { get; } = new Bindable<Typeface>(Typeface.Torus);

        // Weight dropdown is typeface-aware via TypefaceAwareWeightDropdown
        // — see that nested class for the filtering logic. Without the
        // custom control the dropdown exposed every FontWeight (Regular,
        // Medium, SemiBold, etc.), but Venera only ships Light/Bold/Black
        // so picking any other weight silently fell back to Torus,
        // confusing users who thought their Venera selection wasn't being
        // honoured.
        [SettingSource("Text weight", "Weight of the ratio number font.", SettingControlType = typeof(TypefaceAwareWeightDropdown))]
        public Bindable<FontWeight> TextWeight { get; } = new Bindable<FontWeight>(FontWeight.SemiBold);

        [SettingSource("Value colour", "Colour of the ratio number.")]
        public BindableColour4 ValueColour { get; } = new BindableColour4(Colour4.White);

        [SettingSource("Label colour", "Colour of the optional header label.")]
        public BindableColour4 LabelColour { get; } = new BindableColour4(new Colour4(255, 255, 255, 140));

        [SettingSource("Decimal places", "How many decimals to render (1-3).")]
        public BindableInt DecimalPlaces { get; } = new BindableInt(2)
        {
            MinValue = 1,
            MaxValue = 3,
        };

        [SettingSource("Pulse on update", "Brief scale flash on each new judgement. Turn off for a calmer HUD.")]
        public Bindable<bool> PulseOnUpdate { get; } = new BindableBool(true);

        // Cached so we can poke its settings live as bindables change
        // without re-creating the whole text drawable (which would
        // reset RollingCounter's transform mid-flight).
        private RatioTextComponent textComponent = null!;

        protected override IHasText CreateText() => textComponent = new RatioTextComponent(this);

        protected override LocalisableString FormatCount(double count)
        {
            // Sentinel handling matches the base class. Decimal count
            // pulled fresh from the bindable so changing the setting
            // re-renders the next time UpdateDisplay fires.
            if (double.IsNaN(count))
                return @"--";

            if (double.IsPositiveInfinity(count))
                return @"MAX";

            // "F1" / "F2" / "F3" — culture-agnostic decimal format.
            return count.ToString($@"F{DecimalPlaces.Value}");
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // DecimalPlaces affects the formatter output but RollingCounter
            // only re-runs FormatCount on a real value change. Force a
            // refresh when the setting changes so users see the new
            // precision immediately (no need to wait for the next hit).
            DecimalPlaces.BindValueChanged(_ => UpdateDisplay());

            // If the typeface changes to one that doesn't support the
            // currently-selected weight, snap to a supported one. Catches
            // saved layouts authored before the weight dropdown was
            // typeface-aware (e.g. someone saved Font=Venera + Weight=
            // SemiBold last week, which silently rendered as Torus
            // SemiBold). Bind both directions so changing weight while
            // on Venera also coerces.
            Font.BindValueChanged(_ => coerceWeightToTypeface(), true);
            TextWeight.BindValueChanged(_ => coerceWeightToTypeface());
        }

        /// <summary>
        /// Snap <see cref="TextWeight"/> to a weight the active
        /// <see cref="Font"/> actually ships. Currently only Venera has
        /// a restricted set; Torus / Inter accept any weight osu! ships.
        /// </summary>
        private void coerceWeightToTypeface()
        {
            if (Font.Value != Typeface.Venera)
                return;

            // Venera ships Light / Bold / Black only (see
            // OsuGameBase.cs:544-546). Anything else falls back to Torus
            // at render time, so snap to the nearest supported weight
            // here so the bindable matches what's actually rendered.
            switch (TextWeight.Value)
            {
                case FontWeight.Light:
                case FontWeight.Bold:
                case FontWeight.Black:
                    return;

                case FontWeight.Regular:
                case FontWeight.Medium:
                    TextWeight.Value = FontWeight.Light;
                    break;

                case FontWeight.SemiBold:
                    TextWeight.Value = FontWeight.Bold;
                    break;

                default:
                    TextWeight.Value = FontWeight.Bold;
                    break;
            }
        }

        /// <summary>
        /// Inner composite that owns the actual visible drawables and
        /// reacts to setting changes by updating in-place rather than
        /// rebuilding. Keeps the rolling-counter's transform pipeline
        /// intact — rebuilding mid-roll would freeze the displayed
        /// value at whatever it interpolated to.
        /// </summary>
        private partial class RatioTextComponent : CompositeDrawable, IHasText
        {
            private readonly CustomizableManiaRatioCounter owner;

            private OsuSpriteText labelText = null!;
            private OsuSpriteText valueText = null!;
            private Container labelContainer = null!;

            public LocalisableString Text
            {
                get => valueText.Text;
                set
                {
                    valueText.Text = value;
                    if (owner.PulseOnUpdate.Value)
                        pulse();
                }
            }

            public RatioTextComponent(CustomizableManiaRatioCounter owner)
            {
                this.owner = owner;

                AutoSizeAxes = Axes.Both;

                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, -2),
                    Children = new Drawable[]
                    {
                        labelContainer = new Container
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Child = labelText = new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                Spacing = new Vector2(1.4f, 0),
                            },
                        },
                        valueText = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                        },
                    }
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // Settings ↔ visuals wiring. Each binding fires once
                // immediately so the drawables match the saved setting
                // state on first appearance (e.g. a user reopens their
                // skin and the colour they picked yesterday is honoured
                // before any new judgement happens).
                owner.ShowLabel.BindValueChanged(s => labelContainer.Alpha = s.NewValue ? 1 : 0, true);
                owner.LabelText.BindValueChanged(t => labelText.Text = t.NewValue, true);

                owner.LabelColour.BindValueChanged(c => labelText.Colour = c.NewValue, true);

                owner.Font.BindValueChanged(_ => updateValueFont(), true);
                owner.TextWeight.BindValueChanged(_ => updateValueFont(), true);
                owner.ValueColour.BindValueChanged(c => valueText.Colour = c.NewValue, true);
            }

            private void updateValueFont()
            {
                // Numeric font kept fixedWidth so the value doesn't jump
                // horizontally when digits change (1.00 → 2.34 etc.).
                // Size hardcoded at 26 — exposing it as a setting would
                // clash with the skin editor's existing resize-via-corner-
                // handle gesture, so we leave size adjustment to that
                // ergonomic.
                valueText.Font = OsuFont.GetFont(
                    typeface: owner.Font.Value,
                    size: 26,
                    weight: owner.TextWeight.Value,
                    fixedWidth: true);
            }

            private void pulse()
            {
                valueText.ClearTransforms();
                valueText
                    .ScaleTo(1.06f, 90, Easing.OutQuint)
                    .Then()
                    .ScaleTo(1f, 240, Easing.OutElasticHalf);
            }
        }

        // -----------------------------------------------------------------
        // TypefaceAwareWeightDropdown
        //
        // SettingControlType for the Text Weight bindable. Watches the
        // sibling Font bindable on the same component and rebuilds the
        // dropdown items whenever the typeface changes — exposes only
        // Venera's three shipped weights when Venera is selected, and
        // the standard four-weight set otherwise.
        //
        // Direct sibling of FontAdjustableSkinComponent.WeightDropdown.
        // We can't reuse that one as-is because it casts SettingSourceObject
        // to FontAdjustableSkinComponent and our class extends ManiaRatioCounter
        // (RollingCounter) instead — same shape, different parent type.
        // -----------------------------------------------------------------
        private partial class TypefaceAwareWeightDropdown : SettingsDropdown<FontWeight>
        {
            private CustomizableManiaRatioCounter component => (CustomizableManiaRatioCounter)SettingSourceObject;
            protected override OsuDropdown<FontWeight> CreateDropdown() => new TypefaceAwareDropdownControl(this);

            private new partial class TypefaceAwareDropdownControl : SettingsDropdown<FontWeight>.DropdownControl
            {
                private readonly TypefaceAwareWeightDropdown settingsDropdown;

                private IBindable<Typeface> font = null!;

                public TypefaceAwareDropdownControl(TypefaceAwareWeightDropdown settingsDropdown)
                {
                    this.settingsDropdown = settingsDropdown;
                }

                protected override void LoadComplete()
                {
                    base.LoadComplete();

                    font = settingsDropdown.component.Font.GetBoundCopy();
                    font.BindValueChanged(_ => updateItems(), true);
                }

                private void updateItems()
                {
                    ClearItems();

                    switch (font.Value)
                    {
                        case Typeface.Venera:
                            // Venera ships only these three weights — see
                            // OsuGameBase.cs:544-546. Picking any other
                            // weight silently falls back to Torus.
                            AddDropdownItem(FontWeight.Light);
                            AddDropdownItem(FontWeight.Bold);
                            AddDropdownItem(FontWeight.Black);

                            Current.Default = FontWeight.Bold;

                            if (!Items.Contains(Current.Value))
                                Current.SetDefault();
                            break;

                        default:
                            // Torus / Inter / etc. ship the full standard set.
                            AddDropdownItem(FontWeight.Light);
                            AddDropdownItem(FontWeight.Regular);
                            AddDropdownItem(FontWeight.SemiBold);
                            AddDropdownItem(FontWeight.Bold);

                            Current.Default = FontWeight.SemiBold;

                            if (!Items.Contains(Current.Value))
                                Current.SetDefault();
                            break;
                    }
                }
            }
        }
    }
}
