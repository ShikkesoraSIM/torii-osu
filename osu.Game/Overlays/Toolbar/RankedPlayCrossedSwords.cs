// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// Dos espaditas cruzadas. El icono de ranked play en el toolbar.
    /// </summary>
    /// <remarks>
    /// Es un ASSET (<c>Textures/Torii/ranked-sword</c>), no un dibujo armado con
    /// primitivas del framework. El primer intento fue dibujarlo con cajas y
    /// triangulos y no daba: a 19px las diagonales de las hojas quedan escalonadas
    /// y el conjunto se ve como una mancha. Un png con antialiasing de verdad,
    /// generado en alta y bajado, se lee bien desde 19px.
    ///
    /// Lo que se carga es UNA espada suelta y el componente pone dos rotadas. Podria
    /// ser un solo png con las dos ya cruzadas, pero entonces el choque solo podria
    /// mover el conjunto entero; teniendolas separadas cada hoja se anima sola, que
    /// es lo que hace que el golpe se sienta.
    ///
    /// El png viene sin recortar, en el mismo lienzo cuadrado con el que se genero:
    /// asi rotarlo sobre su centro reproduce exactamente el cruce del diseño. Si se
    /// recorta al contenido, el pivote se corre y las dos espadas dejan de cruzarse
    /// donde corresponde.
    ///
    /// Para regenerarlo esta el script en scratchpad (make_swords.py). Los colores
    /// (hoja plateada, mango dorado, gema verde) van horneados en el archivo.
    /// </remarks>
    public partial class RankedPlayCrossedSwords : CompositeDrawable
    {
        /// <summary>Angulo de cada hoja respecto de la vertical.</summary>
        private const float rest_angle = 42f;

        private Sprite leftSword = null!;
        private Sprite rightSword = null!;
        private Circle clashFlash = null!;

        private float dim = 1f;

        public RankedPlayCrossedSwords()
        {
            // Sin esto el destello con Alpha 0 no corre transforms y el choque no se ve.
            AlwaysPresent = true;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures)
        {
            var texture = textures.Get(@"Torii/ranked-sword");

            InternalChildren = new Drawable[]
            {
                clashFlash = new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.5f),
                    Colour = Color4.White,
                    Blending = BlendingParameters.Additive,
                    Alpha = 0,
                },
                leftSword = createSword(texture, -rest_angle),
                rightSword = createSword(texture, rest_angle),
            };
        }

        private static Sprite createSword(Texture? texture, float rotation) => new Sprite
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            FillMode = FillMode.Fit,
            Texture = texture,
            Rotation = rotation,
        };

        /// <summary>
        /// Los colores van horneados en el asset, asi que de lo que se pasa solo se
        /// usa el ALFA, para atenuar el icono cuando la cola esta vacia.
        /// </summary>
        public void SetTint(Color4 colour, double duration = 200)
        {
            dim = colour.A;
            this.FadeTo(dim, duration, Easing.OutQuint);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Alpha = dim;
            startAmbientSway();
        }

        /// <summary>
        /// El meneo de fondo. Lento y chico a proposito: esto vive en el toolbar y
        /// tiene que poder ignorarse, no pedir atencion todo el tiempo.
        /// </summary>
        private void startAmbientSway()
        {
            leftSword.ClearTransforms(targetMember: nameof(Rotation));
            rightSword.ClearTransforms(targetMember: nameof(Rotation));

            leftSword.RotateTo(-rest_angle - 2.5f, 1500, Easing.InOutSine)
                     .Then().RotateTo(-rest_angle + 2.5f, 1500, Easing.InOutSine)
                     .Loop();

            // La otra va en contrafase, asi el cruce respira en vez de girar entero.
            rightSword.RotateTo(rest_angle - 2.5f, 1500, Easing.InOutSine)
                      .Then().RotateTo(rest_angle + 2.5f, 1500, Easing.InOutSine)
                      .Loop();
        }

        /// <summary>
        /// Un choque: las dos hojas se cierran de golpe y destella el cruce.
        /// </summary>
        /// <param name="strength">0..1. Escala con cuanta gente entro de una.</param>
        public void Clash(float strength = 1f)
        {
            strength = Math.Clamp(strength, 0.25f, 1f);

            // El meneo se corta y se retoma al final, si no las dos animaciones
            // pelean por Rotation y el choque queda flojo.
            leftSword.ClearTransforms(targetMember: nameof(Rotation));
            rightSword.ClearTransforms(targetMember: nameof(Rotation));

            float swing = 12f * strength;

            leftSword.RotateTo(-rest_angle + swing, 70, Easing.OutQuint)
                     .Then().RotateTo(-rest_angle, 420, Easing.OutElasticHalf);

            rightSword.RotateTo(rest_angle - swing, 70, Easing.OutQuint)
                      .Then().RotateTo(rest_angle, 420, Easing.OutElasticHalf)
                      .Finally(_ => startAmbientSway());

            clashFlash.ClearTransforms();
            clashFlash.Alpha = 0;
            clashFlash.Scale = new Vector2(0.6f);
            clashFlash.FadeTo(0.5f * strength, 60, Easing.OutQuint)
                      .ScaleTo(1.5f, 340, Easing.OutQuint)
                      .FadeOut(340, Easing.OutQuint);
        }
    }
}
