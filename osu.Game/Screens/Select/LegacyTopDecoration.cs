// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Rulesets;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// torii: el truco de skinning del "mode button" de osu!stable. los skins traen un selection-mode
    /// grande + mode-*-small asi la parte de ARRIBA del song select se vuelve skinneable. stable dibuja
    /// ese grafico mode-button DETRAS del texto/controles del song select. el footer de torii dibuja las
    /// mismas texturas, pero el footer queda arriba de la pantalla, asi que tapaban la info del beatmap /
    /// Group / Sort / Rankings. aca dibujamos el mismo grafico mode-button, como capa ADELANTE del carousel
    /// pero DETRAS del chrome legacy, asi los labels/cajas se leen por encima (el mode button propio del
    /// footer queda clampeado a tamano normal asi no vuelve a tapar). posicionado para coincidir con el del footer.
    /// </summary>
    public partial class LegacyTopDecoration : CompositeDrawable
    {
        // coincide con LegacyFooter.buttons_pos_16_9 (140 * 1.6), la X del cluster de botones del footer.
        private const float components_x = 140 * 1.6f;

        [Resolved]
        private ISkinSource skin { get; set; } = null!;

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        private Sprite background = null!;
        private Sprite icon = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                // bg selection-mode: va en el bottom-left del mode button (como LegacyFooterButton).
                background = new Sprite
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Position = new Vector2(components_x, 0),
                },
                // icono mode-*-small: con offset desde el bottom-left del boton (como LegacyRulesetFooterButton).
                icon = new Sprite
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.Centre,
                    Position = new Vector2(components_x + 57.6f / 2 * 1.6f, -35 * 1.6f),
                    // stable dibuja el iconito de modo aditivo (brilla sobre la decoracion).
                    Blending = BlendingParameters.Additive,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            skin.SourceChanged += updateTextures;
            ruleset.BindValueChanged(_ => updateTextures(), true);
        }

        private void updateTextures()
        {
            background.Texture = skin.GetTexture(@"selection-mode");
            icon.Texture = skin.GetTexture($@"mode-{ruleset.Value.ShortName}-small");
        }

        protected override void Dispose(bool isDisposing)
        {
            if (skin.IsNotNull())
                skin.SourceChanged -= updateTextures;

            base.Dispose(isDisposing);
        }
    }
}
