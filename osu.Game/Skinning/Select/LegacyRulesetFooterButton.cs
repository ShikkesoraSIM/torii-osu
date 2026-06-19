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
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ISkin source = TextureSource ?? skin;

            ruleset.BindValueChanged(r =>
            {
                string name = $@"mode-{r.NewValue.ShortName}-small";
                var tex = source.GetTexture(name) ?? skins.DefaultClassicSkin.GetTexture(name);
                modeIcon.Texture = tex;

                if (tex != null)
                {
                    // mantener el icono de mode del footer en tamaño boton normal. algunos skins traen un
                    // mode-*-small gigante como decoracion "skinnable top" (lo dibuja aparte y atras del
                    // chrome el LegacyTopDecoration); sin este clamp el footer volveria a dibujar esa
                    // textura enorme encima de todo. a los iconos normales les dejamos el tamaño nativo,
                    // solo achicamos los que vienen pasados.
                    const float max_icon = 70f;
                    float maxDim = Math.Max(tex.DisplayWidth, tex.DisplayHeight);
                    float scale = maxDim > max_icon ? max_icon / maxDim : 1f;
                    modeIcon.Size = new Vector2(tex.DisplayWidth * scale, tex.DisplayHeight * scale);
                }
            }, true);
        }
    }
}
