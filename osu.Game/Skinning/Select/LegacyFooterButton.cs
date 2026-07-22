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

        /// <summary>
        /// cuando el glyph base de este boton es en realidad la decoracion skinnable-top gigante del skin
        /// (tipico de selection-mode), no lo dibujamos como glyph del boton: clampeado al slot saldria una
        /// mini version rara de toda la decoracion. la decoracion la dibuja aparte LegacyFooter.
        /// </summary>
        public bool SuppressBaseGlyph { get; init; }

        // el slot del boton de stable. el hit area clickeable es COMO MINIMO esto, asi una textura que
        // falta / es 0x0 nunca deja el boton sin click; y crece hasta cubrir el glyph de hover cuando el
        // skin trae botones mas grandes (stable toma el input del sprite -over, o sea que su hit area
        // tambien escala con la textura).
        private const float slot_width = 74;
        private const float slot_height = 90;

        // mas alla de esto la textura es arte de decoracion del footer, no un glyph de boton.
        private const float max_glyph_dimension = 250;

        public LegacyFooterButton(string kind)
        {
            this.kind = kind;

            Enabled.Value = true;
        }

        [BackgroundDependencyLoader]
        private void load(ISkinSource skin, SkinManager skins)
        {
            // los botones pueden quedar de alturas distintas cuando el skin trae glyphs oversized;
            // anclados abajo quedan alineados por la base dentro de la fila del footer.
            Anchor = Anchor.BottomLeft;
            Origin = Anchor.BottomLeft;

            ISkin source = TextureSource ?? skin;
            const Anchor sprite_anchor = Anchor.BottomLeft;

            Texture? texture(string name)
            {
                var tex = source.GetTexture(name) ?? skins.DefaultClassicSkin.GetTexture(name);

                // una textura tamaño-decoracion no sirve como glyph de boton (hay skins que meten
                // el arte entero del footer en estos slots); caemos al glyph classic bundleado en
                // vez de dibujarla gigante encima de todo.
                if (tex != null && Math.Max(tex.DisplayWidth, tex.DisplayHeight) > max_glyph_dimension)
                    tex = skins.DefaultClassicSkin.GetTexture(name);

                return tex;
            }

            // las dos texturas: el glyph base (siempre visible) y el "-over" (glow aditivo de hover, como
            // en stable). si el skin no trae el "-over" caemos al base asi igual hay feedback en hover.
            var baseTex = texture($"selection-{kind}");
            var overTex = texture($"selection-{kind}-over") ?? baseTex;

            // si este boton lleva en realidad la decoracion gigante (ej: selection-mode usado como
            // skinnable-top), no la dibujamos como glyph ni como hover: la dibuja aparte LegacyFooter.
            if (SuppressBaseGlyph)
            {
                baseTex = null;
                overTex = null;
            }

            var overSize = overTex != null ? new Vector2(overTex.DisplayWidth, overTex.DisplayHeight) : Vector2.Zero;
            Size = Vector2.ComponentMax(new Vector2(slot_width, slot_height), overSize);

            Children = new Drawable[]
            {
                new Sprite
                {
                    Anchor = sprite_anchor,
                    Origin = sprite_anchor,
                    Texture = baseTex,
                    BypassAutoSizeAxes = Axes.Both,
                    Size = sizeFor(baseTex),
                },
                // stable dibuja el -over EXACTAMENTE en la misma posicion que el base (mismo
                // field/origin/position), solo fadeado por alpha en hover. nada de nudges.
                hoverSprite = new Sprite
                {
                    Anchor = sprite_anchor,
                    Origin = sprite_anchor,
                    Texture = overTex,
                    BypassAutoSizeAxes = Axes.Both,
                    Size = sizeFor(overTex),
                    Alpha = 0,
                    AlwaysPresent = true,
                    Blending = BlendingParameters.Additive,
                },
                hoverSound = new SkinnableSound(new SampleInfo("click-short")),
                clickSound = new SkinnableSound(new SampleInfo("click-short-confirm")),
            };

            // tamaño natural, como stable: un boton grande simplemente sobresale de la barra del footer.
            Vector2 sizeFor(Texture? tex) => tex == null ? Vector2.Zero : new Vector2(tex.DisplayWidth, tex.DisplayHeight);
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
