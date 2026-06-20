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

            // las dos texturas: el glyph base (siempre visible) y el "-over" (glow aditivo de hover, como
            // en stable). si el skin no trae el "-over" caemos al base asi igual hay feedback en hover.
            var baseTex = texture($"selection-{kind}");
            var overTex = texture($"selection-{kind}-over") ?? baseTex;

            // tamaño final del glyph: si la textura viene ENORME (algunos skins traen un selection-mode
            // gigante como decoracion "skinnable top", que dibuja aparte el LegacyTopDecoration) la achicamos
            // al slot; si no, su tamaño natural. el hit area del boton es fijo (74x90) aparte.
            // CLAVE PARA EL ALINEADO: el base y el -over comparten el MISMO tamaño + anchor, asi el glow
            // overlaya 100% sin offset aunque el -over del skin venga con un tamaño/padding apenas distinto
            // (sino cada uno se escalaba por su cuenta y el -over quedaba corrido un par de px).
            var sizingTex = baseTex ?? overTex;
            Vector2 glyphSize = Vector2.Zero;

            if (sizingTex != null)
            {
                glyphSize = new Vector2(sizingTex.DisplayWidth, sizingTex.DisplayHeight);

                float maxDim = Math.Max(sizingTex.DisplayWidth, sizingTex.DisplayHeight);
                if (maxDim > slot_height)
                    glyphSize *= slot_height / maxDim;
            }

            Children = new Drawable[]
            {
                new Sprite
                {
                    Anchor = spriteAnchor,
                    Origin = spriteAnchor,
                    Texture = baseTex,
                    BypassAutoSizeAxes = Axes.Both,
                    Size = glyphSize,
                },
                hoverSprite = new Sprite
                {
                    Anchor = spriteAnchor,
                    Origin = spriteAnchor,
                    Texture = overTex,
                    BypassAutoSizeAxes = Axes.Both,
                    Size = glyphSize,
                    Alpha = 0,
                    AlwaysPresent = true,
                    Blending = BlendingParameters.Additive,
                },
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
