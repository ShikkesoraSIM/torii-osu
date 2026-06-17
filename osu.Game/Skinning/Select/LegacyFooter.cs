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
using osu.Game.Configuration;
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
        private void load(ISkinSource skin, OsuConfigManager config, SkinManager skins)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            const float mods_button_off = 57.6f * 1.6f;
            const float random_button_off = mods_button_off + 48 * 1.6f;
            const float options_button_off = random_button_off + 48 * 1.6f;
            const float user_pos_off = options_button_off + 48 * 2 * 1.6f;

            // ─── Torii: skin vs bundled footer chrome ───────────────────────
            // "Skin the song-select footer" toggle. When ON, render the active skin's own
            // footer textures using stable's positioning; when OFF, a clean consistent
            // bundled footer regardless of skin.
            bool useSkin = config.Get<bool>(OsuSetting.ToriiLegacyFooterUseSkin);

            // Stable splits the footer into two layouts (SongSelection.cs:311): default-skin
            // buttons are bottom-anchored ("new"), but a skin shipping its OWN selection-*
            // textures uses the "old" layout — TopLeft origin at y=426 in stable's 480-tall
            // space — so its baked footer design lands correctly near the bottom. frenzibyte's
            // PR only implements the new layout, which is why custom skins float to the middle.
            bool customButtons = useSkin
                                 && skins.CurrentSkin.Value != skins.DefaultClassicSkin
                                 && skins.CurrentSkin.Value.GetTexture(@"selection-mods") != null;

            // Old layout: button sprites grow downward from y≈426 (480-space) → 86.4 px above
            // the footer bottom once scaled by 1.6 into the 768-space the footer is laid out in.
            const float old_layout_buttons_y = -(480 - 426) * 1.6f;

            // null texture source = the ambient (current) skin; bundled = the classic skin.
            ISkin? buttonSource = useSkin ? null : skins.DefaultClassicSkin;
            var bottomTexture = useSkin ? skin.GetTexture(@"songselect-bottom") : null;

            InternalChildren = new Drawable[]
            {
                // Clean fallback bar, shown whenever there's no skin songselect-bottom to draw.
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = footer_bar_height,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.5f), Color4.Black.Opacity(0.85f)),
                    Alpha = bottomTexture != null ? 0 : 1,
                },
                // Skin's songselect-bottom backing, bottom-anchored exactly like stable
                // (SongSelection.cs:733 — Origins.BottomLeft at (0,480)).
                new Sprite
                {
                    Texture = bottomTexture,
                    RelativeSizeAxes = Axes.X,
                    Width = 1,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Alpha = bottomTexture != null ? 1 : 0,
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
                            Origin = customButtons ? Anchor.TopLeft : Anchor.BottomLeft,
                            Y = customButtons ? old_layout_buttons_y : 0,
                            AutoSizeAxes = Axes.Both,
                            Children = new[]
                            {
                                new LegacyRulesetFooterButton { TextureSource = buttonSource, LegacyTopLeftLayout = customButtons },
                                new LegacyFooterButton("mods") { X = mods_button_off, Action = ModsAction, TextureSource = buttonSource, LegacyTopLeftLayout = customButtons },
                                new LegacyFooterButton("random") { X = random_button_off, Action = RandomAction, TextureSource = buttonSource, LegacyTopLeftLayout = customButtons },
                                new LegacyFooterButton("options") { X = options_button_off, Action = OptionsAction, TextureSource = buttonSource, LegacyTopLeftLayout = customButtons },
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
