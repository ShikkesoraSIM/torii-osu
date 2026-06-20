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

            // nudge fino del glow -over para que calce con el grafico del boton (el -over del skin cae un
            // toque abajo-izquierda respecto al "+", asi que lo corremos un poco arriba-derecha).
            // +x = derecha, -y = arriba. AJUSTABLE: si queda corrido, mover estos dos numeros.
            var hoverNudge = new Vector2(1, -6);

            Children = new Drawable[]
            {
                // glyph base, anclado abajo-izquierda como stable. tamaño natural (clampeado al slot si la
                // textura del skin viene enorme). el hit area del boton es fijo (74x90) aparte.
                new Sprite
                {
                    Anchor = spriteAnchor,
                    Origin = spriteAnchor,
                    Texture = baseTex,
                    BypassAutoSizeAxes = Axes.Both,
                    Size = sizeFor(baseTex),
                },
                // glow "-over" del hover: mismo anclaje + tamaño natural que el base, + el nudge fino.
                hoverSprite = new Sprite
                {
                    Anchor = spriteAnchor,
                    Origin = spriteAnchor,
                    Position = hoverNudge,
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

            Vector2 sizeFor(Texture? tex)
            {
                if (tex == null)
                    return Vector2.Zero;

                var size = new Vector2(tex.DisplayWidth, tex.DisplayHeight);
                float maxDim = Math.Max(tex.DisplayWidth, tex.DisplayHeight);
                if (maxDim > slot_height)
                    size *= slot_height / maxDim;
                return size;
            }
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
