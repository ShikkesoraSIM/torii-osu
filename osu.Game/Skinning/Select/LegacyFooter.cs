// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Utils;
using osu.Game.Graphics.Containers;
using osu.Game.Screens.Menu;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Skinning.Select
{
    public partial class LegacyFooter : CompositeDrawable
    {
        private Container components = null!;
        private LogoTrackingContainer logoTrackingContainer = null!;
        private IDisposable? logoTracking;

        private const float buttons_pos_4_3 = 120 * 1.6f;
        private const float buttons_pos_16_9 = 140 * 1.6f;
        private const float footer_bar_height = 96;

        // Torii: wired by the host footer so the legacy chrome drives the real
        // song-select actions. The upstream housing PR leaves these unhooked.
        public Action? BackAction { get; init; }
        public Action? ModsAction { get; init; }
        public Action? RandomAction { get; init; }
        public Action? OptionsAction { get; init; }

        [BackgroundDependencyLoader]
        private void load(ISkinSource skin)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            const float mods_button_off = 57.6f * 1.6f;
            const float random_button_off = mods_button_off + 48 * 1.6f;
            const float options_button_off = random_button_off + 48 * 1.6f;
            const float user_pos_off = options_button_off + 48 * 2 * 1.6f;

            // Torii: a consistent clean footer bar pinned to the bottom. We intentionally
            // do NOT render the skin's "songselect-bottom" texture: that asset isn't bundled
            // with lazer's classic skin, and skins that DO ship it commonly bake a full
            // stable-era footer mockup (buttons + panels) into a tall texture which, drawn
            // at native height, floats up into the middle of the screen. A solid bar gives a
            // predictable, aligned footer with every skin (the skin's buttons + the user
            // panel still render on top for skin flavour).
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = footer_bar_height,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.5f), Color4.Black.Opacity(0.85f)),
                },
                new LegacyBackButton
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Action = BackAction,
                },
                components = new Container
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    AutoSizeAxes = Axes.Both,
                    X = buttons_pos_16_9,
                    Children = new Drawable[]
                    {
                        new LegacyFooterUser
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            X = user_pos_off + 3 * 1.6f,
                            Y = 2 * 1.6f,
                        },
                        new Container
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            AutoSizeAxes = Axes.Both,
                            Children = new[]
                            {
                                new LegacyRulesetFooterButton(),
                                new LegacyFooterButton("mods") { X = mods_button_off, Action = ModsAction },
                                new LegacyFooterButton("random") { X = random_button_off, Action = RandomAction },
                                new LegacyFooterButton("options") { X = options_button_off, Action = OptionsAction },
                            }
                        },
                    }
                },
                (logoTrackingContainer = new LogoTrackingContainer
                {
                    RelativeSizeAxes = Axes.Both,
                }).WithChild(logoTrackingContainer.LogoFacade.With(f =>
                {
                    f.Anchor = Anchor.BottomRight;
                    f.Origin = Anchor.Centre;
                    // todo: lazer's positioning of the logo differs from stable, but for aesthetic purposes it's better to use lazer's.
                    // having the logo shift position when switching between a lazer and a legacy skin would look awkward.
                    // for reference, stable's positioning of the logo is close to Vector2(-70, -50).
                    f.Position = new Vector2(-76, -36);
                })),
            };
        }

        protected override void Update()
        {
            base.Update();

            bool isWidescreen = Precision.DefinitelyBigger(DrawWidth, 1024);
            components.X = isWidescreen ? buttons_pos_16_9 : buttons_pos_4_3;
        }

        public void StartTrackingLogo(OsuLogo logo, float duration = 0, Easing easing = Easing.None)
        {
            logoTracking = logoTrackingContainer.StartTracking(logo, duration, easing);
        }

        public void StopTrackingLogo()
        {
            logoTracking?.Dispose();
            logoTracking = null;
        }
    }
}
