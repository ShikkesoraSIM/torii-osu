// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// Dos espaditas cruzadas. El icono de ranked play en el toolbar.
    /// </summary>
    /// <remarks>
    /// Dibujado a mano y no un SpriteIcon porque FontAwesome free no tiene espadas
    /// cruzadas: lo mas cercano es Khanda, que es un simbolo religioso sij y no viene
    /// al caso. Dibujarlo ademas deja animar cada hoja por separado, que es lo que
    /// hace el "choque".
    ///
    /// Sigue el estilo de icono de juego: HOJA ANCHA que se afina hasta la punta y
    /// MANGO de otro color. Las dos cosas importan y por razones distintas. La hoja
    /// ancha es lo que hace que se lea como espada a 18px — con hojas finitas quedan
    /// dos palitos cruzados sin importar cuanto detalle les pongas. Y el mango de otro
    /// color es lo que separa "arriba" de "abajo": en un solo tono la silueta es una X
    /// simetrica y el ojo no sabe donde esta la punta.
    ///
    /// El naranja de la pildora se usa para el MANGO y la hoja va casi blanca. Sale
    /// gratis (es el color que ya teniamos) y de paso la hoja gana el contraste que
    /// necesita contra el fondo oscuro.
    ///
    /// OJO: las posiciones van con RelativePositionAxes. Con tamaños relativos y
    /// posiciones absolutas (que fue el primer intento) los Y se interpretan como
    /// PIXELES, todo se amontona en el centro y queda una manchita.
    /// </remarks>
    public partial class RankedPlayCrossedSwords : CompositeDrawable
    {
        private static readonly Color4 blade_colour = new Color4(236, 240, 246, 255);
        private static readonly Color4 blade_shade = new Color4(176, 184, 198, 255);

        /// <summary>Angulo de cada hoja respecto de la vertical.</summary>
        private const float rest_angle = 38f;

        private Container leftSword = null!;
        private Container rightSword = null!;
        private Circle clashFlash = null!;

        private readonly List<Drawable> hilts = new List<Drawable>();

        private Color4 tint = Color4.White;

        public RankedPlayCrossedSwords()
        {
            // Sin esto el destello con Alpha 0 no corre transforms y el choque no se ve.
            AlwaysPresent = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
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
                leftSword = createSword(-rest_angle),
                rightSword = createSword(rest_angle),
            };
        }

        /// <param name="rotation">Angulo de la hoja. Las dos se cruzan arriba del centro.</param>
        private Container createSword(float rotation)
        {
            // El mango se guarda aparte para poder pintarlo distinto de la hoja.
            var guard = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                RelativePositionAxes = Axes.Both,
                Size = new Vector2(0.52f, 0.14f),
                Y = 0.10f,
            };

            var grip = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                RelativeSizeAxes = Axes.Both,
                RelativePositionAxes = Axes.Both,
                Size = new Vector2(0.15f, 0.22f),
                Y = 0.14f,
            };

            var pommel = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                RelativePositionAxes = Axes.Both,
                Size = new Vector2(0.22f, 0.16f),
                Y = 0.40f,
            };

            hilts.Add(guard);
            hilts.Add(grip);
            hilts.Add(pommel);

            return new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Rotation = rotation,
                Children = new Drawable[]
                {
                    // La hoja entera es UN triangulo estirado: asi el afinado sale del
                    // shape y no de pegar un cuadrado con una punta encima, que a este
                    // tamaño se nota como un escaloncito.
                    new Triangle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.BottomCentre,
                        RelativeSizeAxes = Axes.Both,
                        RelativePositionAxes = Axes.Both,
                        Size = new Vector2(0.34f, 0.74f),
                        Y = 0.06f,
                        Colour = blade_colour,
                    },
                    // Filo: una franja mas oscura sobre la mitad izquierda de la hoja.
                    // Es el unico "sombreado" que entra a 18px y alcanza para que la
                    // hoja no se vea como una mancha plana.
                    new Triangle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.BottomCentre,
                        RelativeSizeAxes = Axes.Both,
                        RelativePositionAxes = Axes.Both,
                        Size = new Vector2(0.15f, 0.68f),
                        X = -0.05f,
                        Y = 0.06f,
                        Colour = blade_shade,
                    },
                    guard,
                    grip,
                    pommel,
                },
            };
        }

        /// <summary>
        /// Pinta el mango. La hoja se queda plateada; del color pasado se usa el alfa
        /// para atenuar el icono entero (asi el estado "cola vacia" se ve apagado).
        /// </summary>
        public void SetTint(Color4 colour, double duration = 200)
        {
            tint = colour;

            foreach (var hilt in hilts)
                hilt.FadeColour(new Color4(colour.R, colour.G, colour.B, 1f), duration, Easing.OutQuint);

            this.FadeTo(colour.A, duration, Easing.OutQuint);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            SetTint(tint, 0);
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

            leftSword.RotateTo(-rest_angle - 3f, 1500, Easing.InOutSine)
                     .Then().RotateTo(-rest_angle + 3f, 1500, Easing.InOutSine)
                     .Loop();

            // La otra va en contrafase, asi el cruce respira en vez de girar entero.
            rightSword.RotateTo(rest_angle - 3f, 1500, Easing.InOutSine)
                      .Then().RotateTo(rest_angle + 3f, 1500, Easing.InOutSine)
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

            float swing = 13f * strength;

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
