// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Rulesets;
using osuTK;

namespace osu.Game.Skinning.Select
{
    public partial class LegacyRulesetFooterButton : LegacyFooterButton
    {
        private Sprite modeIcon = null!;

        [Resolved]
        private ISkinSource skin { get; set; } = null!;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        public LegacyRulesetFooterButton()
            : base("mode")
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AddInternal(modeIcon = new Sprite
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.Centre,
                BypassAutoSizeAxes = Axes.Both,
                X = 57.6f / 2 * 1.6f,
                Y = -35 * 1.6f,
                // stable lo dibuja aditivo (s_modeSprite.Additive = true); los skins con iconos de
                // mode sobre fondo negro dependen de que el negro quede invisible.
                Blending = BlendingParameters.Additive,
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ISkin source = TextureSource ?? skin;

            ruleset.BindValueChanged(r =>
            {
                // si el skin trae los iconos de mode como decoracion gigante skinnable-top (igual que
                // selection-mode), no los dibujamos como icono del boton: clampeados al slot quedan como una
                // mini-decoracion rara al lado del footer. la decoracion la dibuja aparte LegacyFooter.
                if (SuppressBaseGlyph)
                {
                    modeIcon.Texture = null;
                    return;
                }

                string name = $@"mode-{r.NewValue.ShortName}-small";
                var tex = source.GetTexture(name) ?? skins.DefaultClassicSkin.GetTexture(name);

                // algunos skins traen un mode-*-small gigante como decoracion "skinnable top" (lo
                // dibuja aparte y atras del chrome el LegacyTopDecoration); para eso caemos al icono
                // classic bundleado en vez de redibujar la decoracion sobre el footer. los iconos
                // normales van a tamaño natural como stable, aunque sobresalgan un poco del slot.
                if (tex != null && Math.Max(tex.DisplayWidth, tex.DisplayHeight) > 250)
                    tex = skins.DefaultClassicSkin.GetTexture(name);

                modeIcon.Texture = tex;

                if (tex != null)
                    modeIcon.Size = new Vector2(tex.DisplayWidth, tex.DisplayHeight);
            }, true);
        }
    }
}
