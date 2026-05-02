// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;

// The accent bar under the active tab is sourced from
// OverlayColourProvider.Highlight1 — keeping a live subscription to
// ColoursChanged here means the entire tab strip (Friends / Currently
// Online in the Dashboard, the section tabs in the User profile, etc.)
// re-tints in lock-step with the user's CustomUIHue picker.

namespace osu.Game.Overlays
{
    public abstract partial class OverlayTabControl<T> : OsuTabControl<T>
    {
        private readonly Box bar;

        protected float BarHeight
        {
            set => bar.Height = value;
        }

        public override Color4 AccentColour
        {
            get => base.AccentColour;
            set
            {
                base.AccentColour = value;
                bar.Colour = value;
            }
        }

        protected OverlayTabControl()
        {
            TabContainer.Masking = false;
            TabContainer.Spacing = new Vector2(20, 0);

            AddInternal(bar = new Box
            {
                RelativeSizeAxes = Axes.X,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft
            });
        }

        private OverlayColourProvider boundColourProvider;

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            boundColourProvider = colourProvider;
            AccentColour = colourProvider.Highlight1;
            colourProvider.ColoursChanged += updateAccentFromTheme;
        }

        private void updateAccentFromTheme()
        {
            if (boundColourProvider == null)
                return;

            // Re-runs the AccentColour setter, which fans the new value out
            // to bar.Colour AND propagates to every OverlayTabItem (the tab
            // items pull from this AccentColour at construction).
            AccentColour = boundColourProvider.Highlight1;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (boundColourProvider != null)
                boundColourProvider.ColoursChanged -= updateAccentFromTheme;
            base.Dispose(isDisposing);
        }

        protected override Dropdown<T> CreateDropdown() => null;

        protected override TabItem<T> CreateTabItem(T value) => new OverlayTabItem(value);

        protected partial class OverlayTabItem : TabItem<T>, IHasAccentColour
        {
            protected readonly ExpandingBar Bar;
            protected readonly OsuSpriteText Text;

            private Color4 accentColour;

            public Color4 AccentColour
            {
                get => accentColour;
                set
                {
                    if (accentColour == value)
                        return;

                    accentColour = value;
                    Bar.Colour = value;

                    updateState();
                }
            }

            private Sample selectSample = null!;

            public OverlayTabItem(T value)
                : base(value)
            {
                AutoSizeAxes = Axes.X;
                RelativeSizeAxes = Axes.Y;

                Children = new Drawable[]
                {
                    Text = new OsuSpriteText
                    {
                        Margin = new MarginPadding { Bottom = 10 },
                        Origin = Anchor.BottomLeft,
                        Anchor = Anchor.BottomLeft,
                        Font = OsuFont.GetFont(),
                    },
                    Bar = new ExpandingBar
                    {
                        Anchor = Anchor.BottomCentre,
                        ExpandedSize = 5f,
                        CollapsedSize = 0
                    },
                    new HoverSounds(HoverSampleSet.TabSelect)
                };
            }

            [BackgroundDependencyLoader]
            private void load(AudioManager audio)
            {
                selectSample = audio.Samples.Get(@"UI/tabselect-select");
            }

            protected override bool OnHover(HoverEvent e)
            {
                base.OnHover(e);

                if (!Active.Value)
                    HoverAction();

                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                base.OnHoverLost(e);

                if (!Active.Value)
                    UnhoverAction();
            }

            protected override void OnActivated()
            {
                HoverAction();
                Text.Font = Text.Font.With(weight: FontWeight.Bold);
                Text.FadeColour(Color4.White, 120, Easing.InQuad);
            }

            protected override void OnDeactivated()
            {
                UnhoverAction();
                Text.Font = Text.Font.With(weight: FontWeight.Medium);
            }

            protected override void OnActivatedByUser() => selectSample.Play();

            private void updateState()
            {
                if (Active.Value)
                    OnActivated();
                else
                    OnDeactivated();
            }

            protected virtual void HoverAction() => Bar.Expand();

            protected virtual void UnhoverAction()
            {
                Bar.Collapse();
                Text.FadeColour(AccentColour, 120, Easing.InQuad);
            }
        }
    }
}
