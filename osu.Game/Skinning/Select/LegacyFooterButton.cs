// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Game.Audio;
using osuTK;

namespace osu.Game.Skinning.Select
{
    public partial class LegacyFooterButton : ClickableContainer
    {
        private readonly string kind;

        private Sprite hoverSprite = null!;
        private SkinnableSound hoverSound = null!;
        private SkinnableSound clickSound = null!;

        /// <summary>de donde salen las texturas del boton. null = el skin actual.</summary>
        public ISkin? TextureSource { get; init; }

        // el slot del boton de stable. el hit area clickeable queda fijado a esto asi nunca depende del
        // tamaño de la textura del skin (un selection-* que falta / es 0 / es enorme no puede dejar el
        // boton sin click ni comerse a los de al lado). los glyphs se dibujan solo visual encima.
        private const float slot_width = 74;
        private const float slot_height = 90;

        public LegacyFooterButton(string kind)
        {
            this.kind = kind;

            Enabled.Value = true;
        }

        [BackgroundDependencyLoader]
        private void load(ISkinSource skin, SkinManager skins)
        {
            Width = slot_width;
            Height = slot_height;

            ISkin source = TextureSource ?? skin;
            const Anchor spriteAnchor = Anchor.BottomLeft;

            // si el skin activo no trae estas texturas, caemos al classic bundleado. si no, un
            // "selection-{kind}-over" que falta colapsa el boton (y su hit area, que sale del sprite de
            // hover) a 0x0, o sea invisible y sin click en algunos skins.
            Texture? texture(string name) => source.GetTexture(name) ?? skins.DefaultClassicSkin.GetTexture(name);

            // arma el sprite del glyph del boton, achicando las texturas de skin que vengan ENORMES al
            // tamaño del slot. algunos skins traen un selection-mode (y mode-*-small) gigante como
            // decoracion "skinnable top" del song-select, eso lo dibuja aparte y atras del chrome el
            // LegacyTopDecoration. el boton del footer tiene que quedar tamaño boton asi no te tapa toda la pantalla.
            Sprite glyph(string name, bool hover)
            {
                var tex = texture(name);

                var sprite = new Sprite
                {
                    Anchor = spriteAnchor,
                    Origin = spriteAnchor,
                    Texture = tex,
                    BypassAutoSizeAxes = Axes.Both,
                    Alpha = hover ? 0f : 1f,
                    AlwaysPresent = hover,
                };

                if (tex != null)
                {
                    float maxDim = Math.Max(tex.DisplayWidth, tex.DisplayHeight);
                    if (maxDim > slot_height)
                        sprite.Size = new Vector2(tex.DisplayWidth, tex.DisplayHeight) * (slot_height / maxDim);
                }

                return sprite;
            }

            Children = new Drawable[]
            {
                glyph($"selection-{kind}", hover: false),
                hoverSprite = glyph($"selection-{kind}-over", hover: true),
                hoverSound = new SkinnableSound(new SampleInfo("click-short")),
                clickSound = new SkinnableSound(new SampleInfo("click-short-confirm")),
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            hoverSprite.FadeIn(100);
            hoverSound.Play();
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hoverSprite.FadeOut(100);
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            clickSound.Play();
            return base.OnClick(e);
        }
    }
}
