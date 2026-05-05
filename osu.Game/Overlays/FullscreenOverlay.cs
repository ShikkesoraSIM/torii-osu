// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics.CodeAnalysis;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.Containers;
using osu.Game.Online.API;
using osuTK.Graphics;

namespace osu.Game.Overlays
{
    public abstract partial class FullscreenOverlay<T> : WaveOverlayContainer, INamedOverlayComponent
        where T : OverlayHeader
    {
        public virtual IconUsage Icon => Header.Title.Icon;
        public virtual LocalisableString Title => Header.Title.Title;
        public virtual LocalisableString Description => Header.Title.Description;

        public T Header { get; private set; }

        protected virtual Color4 BackgroundColour => ColourProvider.Background5;

        [Resolved]
        protected IAPIProvider API { get; private set; } = null!;

        [Cached]
        protected readonly OverlayColourProvider ColourProvider;

        protected override Container<Drawable> Content => content;

        private readonly Box background;
        private readonly Container content;
        private readonly int defaultHue;
        private IDisposable? customUiHueBinding;

        protected FullscreenOverlay(OverlayColourScheme colourScheme)
        {
            RecreateHeader();

            defaultHue = colourScheme.GetHue();
            ColourProvider = new OverlayColourProvider(defaultHue);

            RelativeSizeAxes = Axes.Both;
            RelativePositionAxes = Axes.Both;
            Width = 0.85f;
            Anchor = Anchor.TopCentre;
            Origin = Anchor.TopCentre;

            Masking = true;

            EdgeEffect = new EdgeEffectParameters
            {
                Colour = Color4.Black.Opacity(0),
                Type = EdgeEffectType.Shadow,
                Hollow = true,
                Radius = 10
            };

            base.Content.AddRange(new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                },
                content = new Container
                {
                    RelativeSizeAxes = Axes.Both
                }
            });
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            // ORDER MATTERS: subscribe to ColoursChanged BEFORE BindFullScheme
            // so the initial apply() inside BindFullScheme's constructor
            // fires UpdateColours synchronously and the background +
            // wave colours are set on first paint. The previous order
            // (subscribe after bind) silently dropped the initial event,
            // leaving background.Colour at its default (white) which
            // caused the changelog / beatmap-listing "white content area
            // + heavy GPU lag" reported in the first dev-build review.
            ColourProvider.ColoursChanged += UpdateColours;
            // API passed so the donator-only accent is gated on the
            // currently-signed-in user, not on whoever last enabled it
            // on this machine. Re-fires automatically on login/logout.
            customUiHueBinding = CustomUiHueHelper.BindFullScheme(config, ColourProvider, defaultHue, CustomUiHueScope.Overlays, API);

            // Always paint the initial state, regardless of whether
            // BindFullScheme's update() landed on a hue identical to the
            // one the provider was constructed with. ChangeColourScheme
            // early-returns when the new hue matches the current one and
            // does NOT fire ColoursChanged — that's the case for every new
            // install of Torii, where the default config has
            // CustomUIHueEnabled=false and the resolver returns the
            // overlay's own scheme hue (which is also what the provider
            // started at). Without this explicit call, UpdateColours
            // never ran for those users, the `background` Box stayed at
            // its initialiser default (Color4.White), and rankings /
            // dashboard / beatmap-listing / changelog all rendered with a
            // glaring white panel underneath the dark content. Users with
            // a custom hue active never saw the bug because their
            // resolved hue differed from the constructor's default and
            // the event did fire. Calling UpdateColours unconditionally
            // here is idempotent — same code path BindFullScheme would
            // have triggered if the hue had changed.
            UpdateColours();
        }

        protected abstract T CreateHeader();

        [MemberNotNull(nameof(Header))]
        protected void RecreateHeader()
        {
            Header = CreateHeader();
        }

        public override void Show()
        {
            if (State.Value == Visibility.Visible)
            {
                // re-trigger the state changed so we can potentially surface to front
                State.TriggerChange();
            }
            else
            {
                base.Show();
            }
        }

        /// <summary>
        /// Updates the colours of the background and the top waves with the latest colour shades provided by <see cref="ColourProvider"/>.
        /// </summary>
        protected void UpdateColours()
        {
            Waves.FirstWaveColour = ColourProvider.Light4;
            Waves.SecondWaveColour = ColourProvider.Light3;
            Waves.ThirdWaveColour = ColourProvider.Dark4;
            Waves.FourthWaveColour = ColourProvider.Dark3;
            background.Colour = BackgroundColour;
        }

        protected override void PopIn()
        {
            base.PopIn();
            FadeEdgeEffectTo(WaveContainer.SHADOW_OPACITY, WaveContainer.APPEAR_DURATION, Easing.Out);
        }

        protected override void PopOut()
        {
            base.PopOut();
            FadeEdgeEffectTo(0, WaveContainer.DISAPPEAR_DURATION, Easing.In).OnComplete(_ => PopOutComplete());
        }

        protected virtual void PopOutComplete()
        {
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                customUiHueBinding?.Dispose();
                customUiHueBinding = null;
                if (ColourProvider != null)
                    ColourProvider.ColoursChanged -= UpdateColours;
            }

            base.Dispose(isDisposing);
        }
    }
}
