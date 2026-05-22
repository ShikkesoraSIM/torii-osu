// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Overlays;

namespace osu.Game.Graphics.UserInterfaceV2
{
    public partial class FormCheckBox : CompositeDrawable, IHasCurrentValue<bool>, IFormControl
    {
        public Bindable<bool> Current
        {
            get => current.Current;
            set => current.Current = value;
        }

        private readonly BindableWithCurrent<bool> current = new BindableWithCurrent<bool>();

        /// <summary>
        /// Caption describing this slider bar, displayed on top of the controls.
        /// </summary>
        public LocalisableString Caption { get; init; }

        /// <summary>
        /// Hint text containing an extended description of this slider bar, displayed in a tooltip when hovering the caption.
        /// </summary>
        public LocalisableString HintText { get; init; }

        /// <summary>
        /// When non-null, a "NEW" feature badge is appended to the caption
        /// row (same dismiss-after-N-interactions behaviour as
        /// <see cref="FormDropdown{T}.NewFeatureId"/>). The badge is
        /// rendered by <see cref="FormFieldCaption"/> which is shared
        /// across form controls — see <see cref="osu.Game.Configuration.NewFeatureRegistry"/>
        /// for the registration convention (every new ingame setting
        /// we add should register an ID here and reference it).
        /// </summary>
        public string? NewFeatureId { get; init; }

        /// <summary>
        /// When <c>true</c>, a small red "+18" pill is appended after
        /// the caption (and after the [NEW] badge if both are present).
        /// Visual cue for settings that unlock adult-rated content —
        /// the only current consumer is the NSFW profile media toggle
        /// in Settings → Torii → Interface, but the prop is on
        /// <see cref="FormCheckBox"/> so future similar settings can
        /// reuse it without re-writing the badge rendering.
        /// </summary>
        public bool ShowExplicitContentBadge { get; init; }

        private FormControlBackground background = null!;
        private FormFieldCaption caption = null!;

        private SwitchButton switchButton = null!;

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
                            Padding = new MarginPadding { Right = SwitchButton.WIDTH + 5 },
                            Children = new Drawable[]
                            {
                                caption = new FormFieldCaption
                                {
                                    Caption = Caption,
                                    TooltipText = HintText,
                                    NewFeatureId = NewFeatureId,
                                    ShowExplicitContentBadge = ShowExplicitContentBadge,
                                },
                            },
                        },
                        switchButton = new SwitchButton
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Current = Current,
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
                background.Flash();

                ValueChanged?.Invoke();
            });
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

        protected override bool OnClick(ClickEvent e)
        {
            switchButton.TriggerClick();
            return true;
        }

        private void updateState()
        {
            caption.Colour = Current.Disabled ? colourProvider.Background1 : colourProvider.Content2;

            if (IsDisabled)
                background.VisualStyle = VisualStyle.Disabled;
            else if (IsHovered)
                background.VisualStyle = VisualStyle.Hovered;
            else
                background.VisualStyle = VisualStyle.Normal;
        }

        public IEnumerable<LocalisableString> FilterTerms => Caption.Yield();

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
    }
}
