// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Diagnostics;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics.Backgrounds;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osuTK.Graphics;

// Note: this file underpins SettingsButton/SettingsButtonV2 (and most
// "round button" controls in the settings panel + form popovers). The
// hue-driven background is sourced from the resolved OverlayColourProvider
// at load-time; we now also keep listening for ColoursChanged so the
// button re-tints in place when CustomUIHue changes — previously the
// button's "default background colour" was a frozen snapshot, which is
// the actual root cause behind the user-reported "settings buttons work
// but require a client restart for the new hue to apply".

namespace osu.Game.Graphics.UserInterfaceV2
{
    public partial class RoundedButton : OsuButton, IFilterable, IHasTooltip
    {
        protected TrianglesV2? Triangles { get; private set; }

        protected override float HoverLayerFinalAlpha => 0;

        private Color4? triangleGradientSecondColour;

        // Whether the consumer set BackgroundColour explicitly. When they
        // do (e.g. PurpleRoundedButton, DangerousRoundedButton, ad-hoc
        // assignments at construction) we stop tracking the overlay theme
        // — the explicit value wins.
        private bool backgroundColourOverridden;
        private OverlayColourProvider? overlayColourProvider;

        public override Color4 BackgroundColour
        {
            get => base.BackgroundColour;
            set
            {
                backgroundColourOverridden = true;
                base.BackgroundColour = value;
                triangleGradientSecondColour = BackgroundColour.Lighten(0.2f);
                updateColours();
            }
        }

        [BackgroundDependencyLoader(true)]
        private void load(OverlayColourProvider? overlayColourProvider, OsuColour colours)
        {
            this.overlayColourProvider = overlayColourProvider;

            // Many buttons have local colours, but this provides a sane default for all other cases.
            DefaultBackgroundColour = overlayColourProvider?.Colour3 ?? colours.Blue3;
            triangleGradientSecondColour ??= DefaultBackgroundColour.Lighten(0.2f);

            // Live re-tint when CustomUIHue changes. Skipped if no overlay
            // theme is in scope (some root-level usages) or if the consumer
            // already pinned a custom BackgroundColour.
            if (overlayColourProvider != null)
                overlayColourProvider.ColoursChanged += retintFromOverlayTheme;
        }

        private void retintFromOverlayTheme()
        {
            if (overlayColourProvider == null || backgroundColourOverridden)
                return;

            var newDefault = overlayColourProvider.Colour3;
            DefaultBackgroundColour = newDefault;

            // Mirror the BackgroundColour setter side-effects without
            // tripping backgroundColourOverridden — we want this colour
            // to keep tracking the live theme.
            base.BackgroundColour = newDefault;
            triangleGradientSecondColour = newDefault.Lighten(0.2f);
            updateColours();

            if (!IsHovered)
                Background.FadeColour(newDefault, 200, Easing.OutQuint);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // This doesn't match the latest design spec (should be 5) but is an in-between that feels right to the eye
            // until we move everything over to Form controls.
            Content.CornerRadius = 10;
            Content.CornerExponent = 2.5f;

            Add(Triangles = new TrianglesV2
            {
                Thickness = 0.02f,
                SpawnRatio = 0.6f,
                RelativeSizeAxes = Axes.Both,
                Depth = float.MaxValue,
            });

            updateColours();
        }

        private void updateColours()
        {
            if (Triangles == null)
                return;

            Debug.Assert(triangleGradientSecondColour != null);

            Triangles.Colour = ColourInfo.GradientVertical(triangleGradientSecondColour.Value, BackgroundColour);
        }

        protected override bool OnHover(HoverEvent e)
        {
            Debug.Assert(triangleGradientSecondColour != null);

            Background.FadeColour(triangleGradientSecondColour.Value, 300, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            Background.FadeColour(BackgroundColour, 300, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        public virtual IEnumerable<LocalisableString> FilterTerms => new[] { Text };

        public bool MatchingFilter
        {
            set => this.FadeTo(value ? 1 : 0);
        }

        public bool FilteringActive { get; set; }

        public virtual LocalisableString TooltipText { get; set; }

        protected override void Dispose(bool isDisposing)
        {
            if (overlayColourProvider != null)
                overlayColourProvider.ColoursChanged -= retintFromOverlayTheme;

            base.Dispose(isDisposing);
        }
    }
}
