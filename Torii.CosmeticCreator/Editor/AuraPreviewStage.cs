// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Cosmetics.Definitions;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osuTK;
using osuTK.Graphics;

namespace Torii.CosmeticCreator.Editor
{
    /// <summary>
    /// torii: preview en vivo de un AURA. Muestra un nombre de ejemplo con el aura data-driven
    /// alrededor — el emisor de partículas detrás del texto + el text-shape glow — replicando cómo se
    /// vería en el juego (chat, leaderboard, perfil). Al cambiar la definición reconstruye el preset y
    /// rearma las capas. Un fondo claro/oscuro toggle ayuda a ver cómo lee el aura en ambos.
    /// </summary>
    public partial class AuraPreviewStage : CompositeDrawable
    {
        [Resolved]
        private OsuColour osuColours { get; set; } = null!;

        private Container backgroundBox = null!;
        private Container auraHost = null!;
        private OsuSpriteText nameText = null!;
        private ParticleAuraEmitter? emitter;
        private TextShapeGlow? glow;

        private CosmeticDefinition? pending;
        private bool darkBackground = true;
        private string sampleName = "Torii";

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = 8;

            InternalChildren = new Drawable[]
            {
                backgroundBox = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new Box { RelativeSizeAxes = Axes.Both },
                },
                auraHost = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Child = nameText = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = sampleName,
                        Font = OsuFont.TorusAlternate.With(size: 40, weight: FontWeight.SemiBold),
                        Colour = Color4.White,
                    },
                },
            };

            applyBackground();
        }

        /// <summary>reconstruye el aura desde una definición (Type=Aura). null limpia el preview.</summary>
        public void SetDefinition(CosmeticDefinition definition)
        {
            pending = definition;
            if (IsLoaded)
                rebuild();
        }

        public void SetSampleName(string name)
        {
            sampleName = string.IsNullOrWhiteSpace(name) ? "Torii" : name;
            if (nameText != null)
                nameText.Text = sampleName;
        }

        public void ToggleBackground()
        {
            darkBackground = !darkBackground;
            applyBackground();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            if (pending != null)
                rebuild();
        }

        private void applyBackground()
        {
            if (backgroundBox?.Child is Box box)
                box.Colour = darkBackground ? new Color4(28, 26, 34, 255) : new Color4(232, 232, 238, 255);
            if (nameText != null)
                nameText.Colour = darkBackground ? Color4.White : new Color4(40, 38, 46, 255);
        }

        private void rebuild()
        {
            // tirar las capas viejas.
            if (emitter != null)
            {
                auraHost.Remove(emitter, disposeImmediately: true);
                emitter = null;
            }

            if (glow != null)
            {
                auraHost.Remove(glow, disposeImmediately: true);
                glow = null;
            }

            if (pending == null || !CosmeticAuraFactory.CanBuild(pending))
                return;

            AuraPreset preset;
            try
            {
                preset = CosmeticAuraFactory.Create(pending);
            }
            catch
            {
                // definición a medio editar / inválida: dejamos el nombre pelado.
                return;
            }

            // glow (abajo del todo).
            if (preset.GlowSettings is AuraGlowSettings gs)
            {
                auraHost.Add(glow = new TextShapeGlow(nameText.Text, nameText.Font, gs.Colour)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    BypassAutoSizeAxes = Axes.Both,
                    Depth = 2,
                    BlurSigma = new Vector2(gs.BlurSigma),
                    MaxAlpha = gs.MaxAlpha,
                    MinAlpha = gs.MinAlpha,
                    DurationMs = gs.PulseMs,
                    Pulsate = gs.Pulsate,
                });
            }
            else if (preset.GlowColour is Color4 gc)
            {
                auraHost.Add(glow = new TextShapeGlow(nameText.Text, nameText.Font, gc)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    BypassAutoSizeAxes = Axes.Both,
                    Depth = 2,
                });
            }

            // emisor de partículas (detrás del nombre, arriba del glow).
            auraHost.Add(emitter = new ParticleAuraEmitter(preset)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                BypassAutoSizeAxes = Axes.Both,
                Depth = 1,
            });
        }

        protected override void Update()
        {
            base.Update();

            // el emisor y el glow spawnean/dibujan dentro del bounding box del nombre.
            Vector2 textSize = nameText.DrawSize;
            if (textSize.X <= 0 || textSize.Y <= 0)
                return;

            if (emitter != null && (Math.Abs(emitter.DrawWidth - textSize.X) > 0.5f || Math.Abs(emitter.DrawHeight - textSize.Y) > 0.5f))
                emitter.Size = textSize;
        }
    }
}
