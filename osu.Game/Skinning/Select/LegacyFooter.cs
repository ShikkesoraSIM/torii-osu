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

        // torii: el footer de arriba engancha esto para que el chrome legacy
        // dispare las acciones reales del song-select. el PR de upstream las deja sueltas.
        public Action? BackAction { get; init; }
        public Action? ModsAction { get; init; }
        public Action? RandomAction { get; init; }
        public Action? OptionsAction { get; init; }

        [BackgroundDependencyLoader]
        private void load(ISkinSource skin, OsuConfigManager config, SkinManager skins)
        {
            RelativeSizeAxes = Axes.Both;

            const float mods_button_off = 57.6f * 1.6f;
            const float random_button_off = mods_button_off + 48 * 1.6f;
            const float options_button_off = random_button_off + 48 * 1.6f;
            const float user_pos_off = options_button_off + 48 * 2 * 1.6f;

            // torii: skin vs footer bundleado.
            // toggle de "skinear el footer del song-select". prendido: usa el songselect-bottom
            // del skin activo + los glyphs selection-* del skin. apagado: una barra limpia y pareja
            // con los botones classic bundleados.
            bool useSkin = config.Get<bool>(OsuSetting.ToriiLegacyFooterUseSkin);

            // null = agarra las texturas del skin actual asi se ven los footers skineados, si no el
            // classic bundleado. el fallback por textura al classic vive en LegacyFooterButton, y cada
            // boton tiene un hit area FIJO de 74x90 que no depende de la textura del skin. asi un skin
            // que no trae selection-* o lo trae con un tamaño raro igual te da un boton clickeable en
            // vez de colapsar o irse de pantalla.
            ISkin? buttonSource = useSkin ? null : skins.DefaultClassicSkin;
            // cuando skineamos siempre queremos un fondo de footer: el songselect-bottom del skin, si no
            // el classic bundleado (el default con borde azul). esta es la capa de ATRAS; la tarjeta de
            // stats del user va encima, y el cosmetic selection-mode del skin encima de eso.
            var bottomTexture = useSkin ? (skin.GetTexture(@"songselect-bottom") ?? skins.DefaultClassicSkin.GetTexture(@"songselect-bottom")) : null;

            // algunos skins meten el footer adentro de un "selection-mode" gigante (la misma textura que
            // la decoracion skinnable-top, donde la PARTE DE ABAJO es el diseño del footer). esa parte de
            // abajo la dibujamos como cosmetic ENCIMA de la tarjeta de stats (tapa performance/acc/lvl y
            // tiene un circulo transparente para el avatar). ojo: usar el lookup `skin` directo, porque
            // skins.CurrentSkin.Value.GetTexture devuelve null para texturas de skins legacy (no hay transformer).
            var modeTexture = useSkin ? skin.GetTexture(@"selection-mode") : null;
            bool skinFooterDecoration = modeTexture != null && Math.Max(modeTexture.DisplayWidth, modeTexture.DisplayHeight) > 90;

            bool showCleanBar = bottomTexture == null;

            InternalChildren = new Drawable[]
            {
                // barra de fallback limpia, se muestra cuando no hay footer de skin que dibujar
                // (ni songselect-bottom ni una decoracion selection-mode).
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = footer_bar_height,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.5f), Color4.Black.Opacity(0.85f)),
                    Alpha = showCleanBar ? 1 : 0,
                },
                // fondo del footer (capa de ATRAS): el songselect-bottom del skin, si no el classic bundleado.
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
                    RelativeSizeAxes = Axes.Both,
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
                                new LegacyRulesetFooterButton { TextureSource = buttonSource },
                                new LegacyFooterButton("mods") { X = mods_button_off, Action = ModsAction, TextureSource = buttonSource },
                                new LegacyFooterButton("random") { X = random_button_off, Action = RandomAction, TextureSource = buttonSource },
                                new LegacyFooterButton("options") { X = options_button_off, Action = OptionsAction, TextureSource = buttonSource },
                            }
                        },
                    }
                },
                // capa de ADELANTE: el cosmetic selection-mode del skin, encima de la tarjeta de stats
                // (tapa performance/acc/lvl, el circulo transparente deja ver el avatar). misma textura y
                // X que la decoracion de arriba asi queda alineado; recortado a la banda del footer para
                // que su diseño de arriba no se suba por encima del chrome del song-select. la Y lo acomoda
                // a la altura de los stats.
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Width = 1,
                    Height = 110,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Masking = true,
                    Alpha = skinFooterDecoration ? 1 : 0,
                    Child = new Sprite
                    {
                        Texture = modeTexture,
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Position = new Vector2(buttons_pos_16_9, -2),
                    },
                },
                (logoTrackingContainer = new LogoTrackingContainer
                {
                    RelativeSizeAxes = Axes.Both,
                }).WithChild(logoTrackingContainer.LogoFacade.With(f =>
                {
                    f.Anchor = Anchor.BottomRight;
                    f.Origin = Anchor.Centre;
                    // todo: lazer posiciona el logo distinto que stable, pero por estetica queda mejor el de lazer.
                    // que el logo se mueva al cambiar entre un skin lazer y uno legacy quedaria feo.
                    // de referencia, en stable el logo va cerca de Vector2(-70, -50).
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
