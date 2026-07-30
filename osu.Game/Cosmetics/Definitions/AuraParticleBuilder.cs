// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Cosmetics.Definitions
{
    /// <summary>
    /// torii: el INTÉRPRETE de auras data-driven. Convierte un <see cref="ParticleSpec"/> (datos) en
    /// una partícula viva con toda su animación, reemplazando los 20 <c>EmitParticle</c> escritos a mano.
    /// Es lo que un <see cref="Graphics.UserEffects.DataDrivenAuraPreset"/> llama por cada spawn.
    ///
    /// Samplea los rangos (tamaño, color, spawn, drift, lifetime, rotación) con el <see cref="Random"/>
    /// del emisor, construye la forma (procedural / glyph / texto / compuesta / PNG propio) y engancha
    /// la cadena de transforms según el modo de movimiento + la gramática de animación. Anchors bakeados
    /// y <c>.Expire()</c> garantizado por acá (nunca dependen del autor de la data).
    /// </summary>
    public static class AuraParticleBuilder
    {
        /// <summary>
        /// Construye una partícula lista para agregar al emisor: posicionada en su spawn, con las
        /// animaciones ya enganchadas y su auto-expiración. <paramref name="parentSize"/> es el bounding
        /// box del nombre; <paramref name="scale"/> el factor proporcional (ParticleScale) del preset.
        /// </summary>
        public static Drawable Build(ParticleSpec spec, Vector2 parentSize, float scale, Random random)
        {
            spec ??= new ParticleSpec();

            // ---- sampleo de valores ----
            float size = sampleRange(spec.SizePx, 6f, 10f, random) * scale;
            Vector2 spawn = sampleSpawn(spec, parentSize, random);
            Vector2 drift = sampleDrift(spec, parentSize, random);
            double lifetime = sampleRange(spec.LifetimeMs, 1500f, 2000f, random);
            float initialRotation = sampleRange(spec.InitialRotation, 0f, 0f, random);
            float peakAlpha = Math.Clamp(sampleRange(spec.Anim?.PeakAlpha, 1f, 1f, random), 0.05f, 1f);

            var palette = resolvePalette(spec.Palette);
            Color4 baseColour = pickColour(spec, palette, random);

            float orbitRadius = sampleRange(spec.OrbitRadius, 0.28f, 0.28f, random) * parentSize.Y;
            float orbitPhase = (float)(random.NextDouble() * Math.PI * 2);

            return new AuraParticle(spec, size, spawn, drift, lifetime, initialRotation, peakAlpha, baseColour, palette, random, orbitRadius, spec.OrbitTurns, orbitPhase);
        }

        // ---- sampleo helpers ----

        private static float sampleRange(float[] range, float defMin, float defMax, Random random)
        {
            float min = range != null && range.Length > 0 ? range[0] : defMin;
            float max = range != null && range.Length > 1 ? range[1] : min;
            if (max < min) (min, max) = (max, min);
            return min + (float)random.NextDouble() * (max - min);
        }

        private static Vector2 sampleSpawn(ParticleSpec spec, Vector2 parentSize, Random random)
        {
            float[] xr = spec.SpawnX;
            float[] yr = spec.SpawnY;

            // selector ponderado de zona (Founder gate). si hay zonas, pisan spawnX/Y.
            if (spec.Zones != null && spec.Zones.Count > 0)
            {
                var zone = pickWeighted(spec.Zones, z => z.Weight, random);
                if (zone != null)
                {
                    xr = zone.SpawnX;
                    yr = zone.SpawnY;
                }
            }

            float fx = sampleRange(xr, 0f, 1f, random);
            float fy = sampleRange(yr, 0.5f, 0.95f, random);
            return new Vector2(fx * parentSize.X, fy * parentSize.Y);
        }

        private static Vector2 sampleDrift(ParticleSpec spec, Vector2 parentSize, Random random)
        {
            float dx = sampleRange(spec.DriftX, -0.1f, 0.1f, random) * parentSize.X;
            float dy = sampleRange(spec.DriftY, -0.9f, -0.4f, random) * parentSize.Y;
            return new Vector2(dx, dy);
        }

        private static Color4[] resolvePalette(string[] hex)
        {
            if (hex == null || hex.Length == 0)
                return new[] { Color4.White };
            return hex.Select(h => CosmeticSettingsBinder.ParseColour(h)).ToArray();
        }

        private static Color4 pickColour(ParticleSpec spec, Color4[] palette, Random random)
        {
            if (palette.Length == 0)
                return Color4.White;

            switch ((spec.ColourPick ?? "random").ToLowerInvariant())
            {
                case "fixed":
                    return palette[0];

                case "twotone":
                    return palette[random.Next(2) % palette.Length];

                case "weighted":
                    if (spec.ColourWeights != null && spec.ColourWeights.Length > 0)
                    {
                        int idx = pickWeightedIndex(spec.ColourWeights, random);
                        return palette[Math.Clamp(idx, 0, palette.Length - 1)];
                    }
                    return palette[random.Next(palette.Length)];

                case "bylayer":
                default:
                    return palette[random.Next(palette.Length)];
            }
        }

        private static T pickWeighted<T>(IReadOnlyList<T> items, Func<T, float> weight, Random random) where T : class
        {
            if (items == null || items.Count == 0)
                return null;
            float total = items.Sum(weight);
            if (total <= 0)
                return items[random.Next(items.Count)];
            float roll = (float)random.NextDouble() * total;
            foreach (var it in items)
            {
                roll -= weight(it);
                if (roll <= 0)
                    return it;
            }
            return items[items.Count - 1];
        }

        private static int pickWeightedIndex(float[] weights, Random random)
        {
            float total = weights.Sum();
            if (total <= 0)
                return random.Next(weights.Length);
            float roll = (float)random.NextDouble() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= weights[i];
                if (roll <= 0)
                    return i;
            }
            return weights.Length - 1;
        }

        internal static Easing ParseEasing(string name)
            => !string.IsNullOrEmpty(name) && Enum.TryParse<Easing>(name, true, out var e) ? e : Easing.None;

        /// <summary>
        /// Una partícula de aura viva. La POSICIÓN exterior hace el drift; los transforms goofy (scale,
        /// rotación, loops, keyframes) van sobre el contenedor interior <see cref="visual"/> para no
        /// pelear con el movimiento. Construye su forma en el BDL (resuelve IRenderer para el PNG propio)
        /// y arma las animaciones en LoadComplete (los loops requieren Time, nunca inline).
        /// </summary>
        internal partial class AuraParticle : CompositeDrawable
        {
            private readonly ParticleSpec spec;
            private readonly float size;
            private readonly Vector2 drift;
            private readonly double lifetime;
            private readonly float initialRotation;
            private readonly float peakAlpha;
            private readonly Color4 baseColour;
            private readonly Color4[] palette;
            private readonly Random random;
            private readonly Vector2 spawnPos;
            private readonly float orbitRadius;
            private readonly float orbitTurns;
            private readonly float orbitPhase;

            private string motion = "drift";
            private bool proceduralMotion;
            private double birthTime;

            private Container visual = null!;

            public AuraParticle(ParticleSpec spec, float size, Vector2 spawn, Vector2 drift, double lifetime,
                                float initialRotation, float peakAlpha, Color4 baseColour, Color4[] palette, Random random,
                                float orbitRadius, float orbitTurns, float orbitPhase)
            {
                this.spec = spec;
                this.size = size;
                this.drift = drift;
                this.lifetime = lifetime;
                this.initialRotation = initialRotation;
                this.peakAlpha = peakAlpha;
                this.baseColour = baseColour;
                this.palette = palette;
                this.random = random;
                this.orbitRadius = orbitRadius;
                this.orbitTurns = orbitTurns;
                this.orbitPhase = orbitPhase;
                spawnPos = spawn;

                // la Position es medida desde el top-left del nombre (igual que los presets a mano);
                // Origin Centre para escalar/rotar alrededor del punto.
                Anchor = Anchor.TopLeft;
                Origin = Anchor.Centre;
                Position = spawn;
                Alpha = 0;
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load(IRenderer renderer)
            {
                Drawable inner = buildInner(renderer);
                inner.Rotation += initialRotation;

                InternalChild = visual = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Child = inner,
                };
            }

            private Drawable buildInner(IRenderer renderer)
            {
                // 1. PNG propio (base64) — pisa todo lo demás.
                if (!string.IsNullOrEmpty(spec.CustomImage))
                {
                    var factory = CosmeticCustomImage.Resolve(renderer, spec.CustomImage, size);
                    if (factory != null)
                        return factory(0);
                }

                // 2. compuesta multi-capa.
                if (spec.Layers != null && spec.Layers.Count > 0)
                    return buildLayers();

                // 3. texto (dev bits).
                if (spec.Text != null && spec.Text.Length > 0)
                {
                    string glyph = spec.Text[random.Next(spec.Text.Length)];
                    var weight = Enum.TryParse<FontWeight>(spec.FontWeight, true, out var w) ? w : FontWeight.Bold;
                    return new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = glyph,
                        Font = OsuFont.GetFont(size: size * 1.4f, weight: weight),
                        Colour = baseColour,
                    };
                }

                // 4. glyph FontAwesome.
                var icon = AuraParticleShapes.ResolveGlyph(spec.Icon);
                Drawable shape;
                if (icon != null)
                {
                    shape = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = icon.Value,
                        Size = new Vector2(size),
                        Colour = baseColour,
                    };
                }
                else
                {
                    // 5. forma procedural.
                    Vector2 aspect = spec.Aspect != null && spec.Aspect.Length >= 2 ? new Vector2(spec.Aspect[0], spec.Aspect[1]) : Vector2.One;
                    shape = AuraParticleShapes.Build(spec.Shape, size, baseColour, aspect);
                }

                shape.Blending = ParseBlend(spec.Blend);

                // halo opcional: copia agrandada y tenue detrás (fake glow).
                if (spec.Halo != null)
                {
                    var halo = cloneForHalo(icon, spec, size * spec.Halo.Scale, baseColour);
                    if (halo != null)
                    {
                        halo.Alpha = spec.Halo.Alpha;
                        halo.Blending = ParseBlend(spec.Blend);
                        return new Container
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            AutoSizeAxes = Axes.Both,
                            Children = new[] { halo, shape },
                        };
                    }
                }

                return shape;
            }

            private Drawable buildLayers()
            {
                var container = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                };

                foreach (var layer in spec.Layers)
                {
                    Color4 lc = layer.ColourRef >= 0 && layer.ColourRef < palette.Length ? palette[layer.ColourRef] : Color4.White;
                    float ls = size * layer.SizeRatio;

                    var iconU = AuraParticleShapes.ResolveGlyph(layer.Icon);
                    Drawable d;
                    if (iconU != null)
                        d = new SpriteIcon { Anchor = Anchor.Centre, Origin = Anchor.Centre, Icon = iconU.Value, Size = new Vector2(ls), Colour = lc };
                    else
                    {
                        Vector2 aspect = layer.Aspect != null && layer.Aspect.Length >= 2 ? new Vector2(layer.Aspect[0], layer.Aspect[1]) : Vector2.One;
                        d = AuraParticleShapes.Build(layer.Shape, ls, lc, aspect);
                        d.Anchor = Anchor.Centre;
                        d.Origin = Anchor.Centre;
                    }

                    d.Alpha = layer.Alpha;
                    d.Blending = ParseBlend(layer.Blend);
                    if (layer.Offset != null && layer.Offset.Length >= 2)
                        d.Position = new Vector2(layer.Offset[0] * size, layer.Offset[1] * size);

                    container.Add(d);
                }

                return container;
            }

            private static Drawable cloneForHalo(IconUsage? icon, ParticleSpec spec, float haloSize, Color4 colour)
            {
                if (icon != null)
                    return new SpriteIcon { Anchor = Anchor.Centre, Origin = Anchor.Centre, Icon = icon.Value, Size = new Vector2(haloSize), Colour = colour };

                Vector2 aspect = spec.Aspect != null && spec.Aspect.Length >= 2 ? new Vector2(spec.Aspect[0], spec.Aspect[1]) : Vector2.One;
                var d = AuraParticleShapes.Build(spec.Shape, haloSize, colour, aspect);
                d.Anchor = Anchor.Centre;
                d.Origin = Anchor.Centre;
                return d;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                birthTime = Time.Current;
                animate();
            }

            protected override void Update()
            {
                base.Update();

                // movimiento procedural (orbit/spiral/zigzag/pendulum): la posición es una función del
                // tiempo de vida, no un tween. el resto (fade/escala/rotación) sigue por transforms.
                if (!proceduralMotion || lifetime <= 0)
                    return;

                float t = (float)Math.Clamp((Time.Current - birthTime) / lifetime, 0, 1);
                float angle = orbitPhase + orbitTurns * MathF.PI * 2 * t;
                Vector2 pos = spawnPos + drift * t;

                switch (motion)
                {
                    case "orbit":
                        pos += new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * orbitRadius;
                        break;

                    case "spiral":
                        pos += new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * orbitRadius * t;
                        break;

                    case "zigzag":
                        // oscilación perpendicular a la dirección de la deriva.
                        Vector2 dir = drift.LengthSquared > 0.0001f ? drift.Normalized() : new Vector2(0, -1);
                        Vector2 perp = new Vector2(-dir.Y, dir.X);
                        pos += perp * orbitRadius * MathF.Sin(angle);
                        break;

                    case "pendulum":
                        pos.X += orbitRadius * MathF.Sin(angle);
                        break;
                }

                Position = pos;
            }

            private void animate()
            {
                var anim = spec.Anim ?? new AnimSpec();

                // ---- movimiento exterior ----
                motion = (spec.Motion ?? "drift").ToLowerInvariant();
                Vector2 driftTarget = spawnPos + drift;

                switch (motion)
                {
                    case "rise":
                        this.MoveTo(driftTarget, lifetime, Easing.OutQuad);
                        break;

                    case "fall":
                        // cae acelerando.
                        this.MoveTo(driftTarget, lifetime, Easing.InQuad);
                        break;

                    case "burst":
                        // sale disparada y frena.
                        this.MoveTo(driftTarget, lifetime, Easing.OutExpo);
                        break;

                    case "orbit":
                    case "spiral":
                    case "zigzag":
                    case "pendulum":
                        // la Position la maneja Update() con una función periódica del tiempo de vida.
                        proceduralMotion = true;
                        break;

                    case "ripple":
                        // onda que CRECE en el lugar (siempre escala; default si no hay Resize).
                        visual.ScaleTo(anim.Resize?.Factor ?? 2.6f, anim.Resize?.Ms ?? (float)(lifetime * 0.8), ParseEasing(anim.Resize?.Easing ?? "OutQuint"));
                        break;

                    case "popinplace":
                        // pop de escala garantizado si el autor no puso uno (así se diferencia del beam).
                        if (anim.ScaleIn == null && (anim.ScaleKeyframes == null || anim.ScaleKeyframes.Count == 0))
                        {
                            visual.Scale = new Vector2(0.2f);
                            visual.ScaleTo(1f, 300, Easing.OutBack);
                        }
                        break;

                    case "beam":
                        // quieta y a tamaño fijo; el fade con hold la hace ver como un haz.
                        break;

                    default: // drift
                        this.MoveTo(driftTarget, lifetime, Easing.OutSine);
                        break;
                }

                // ---- fade (timeline de keyframes o simple) ----
                if (anim.FadeKeyframes != null && anim.FadeKeyframes.Count > 0)
                {
                    var seq = this.FadeTo(peakAlpha * anim.FadeKeyframes[0].Mul, anim.FadeInMs, ParseEasing(anim.FadeInEasing));
                    foreach (var kf in anim.FadeKeyframes.Skip(1))
                        seq = seq.Then().FadeTo(peakAlpha * kf.Mul, kf.Ms, ParseEasing(kf.Easing));
                    seq.Then().FadeOut(anim.FadeOutMs, Easing.InQuad);
                }
                else
                {
                    this.FadeTo(peakAlpha, anim.FadeInMs, ParseEasing(anim.FadeInEasing))
                        .Delay(Math.Max(0, lifetime - anim.FadeInMs - anim.FadeOutMs))
                        .FadeOut(anim.FadeOutMs, Easing.InQuad);
                }

                // ---- scale (pop de entrada / keyframes) ----
                if (anim.ScaleIn != null)
                {
                    visual.Scale = new Vector2(anim.ScaleIn.From);
                    visual.ScaleTo(anim.ScaleIn.To, anim.ScaleIn.Ms, ParseEasing(anim.ScaleIn.Easing));
                }

                if (anim.ScaleKeyframes != null && anim.ScaleKeyframes.Count > 0)
                {
                    var seq = visual.ScaleTo(anim.ScaleKeyframes[0].Mul, anim.FadeInMs, ParseEasing(anim.FadeInEasing));
                    foreach (var kf in anim.ScaleKeyframes.Skip(1))
                        seq = seq.Then().ScaleTo(kf.Mul, kf.Ms, ParseEasing(kf.Easing));
                }

                // ---- rotación ----
                if (anim.RotateToAbsolute.HasValue)
                    visual.RotateTo(anim.RotateToAbsolute.Value, lifetime, Easing.OutQuint);
                else if (anim.RotateOverLife != null && anim.RotateOverLife.Length > 0)
                {
                    float deg = sampleRange(anim.RotateOverLife, 0f, 0f, random);
                    visual.RotateTo(visual.Rotation + deg, lifetime, ParseEasing(anim.RotateOverLifeEasing));
                }

                // ---- loops concurrentes (bob / pulse / breathing) ----
                if (anim.Loops != null)
                {
                    foreach (var loop in anim.Loops)
                        applyLoop(loop);
                }

                // auto-expiración garantizada por el builder, nunca por la data.
                this.Delay(lifetime).Expire();
            }

            private void applyLoop(LoopSpec loop)
            {
                Drawable target = (loop.Target ?? "inner").ToLowerInvariant() == "whole" ? (Drawable)visual : visual.Child;
                if (target == null)
                    return;

                var easing = ParseEasing(loop.Easing);

                if ((loop.Channel ?? "scale").ToLowerInvariant() == "moveoffset")
                {
                    target.MoveToOffset(new Vector2(0, -loop.Amount), loop.Ms, easing)
                          .Then().MoveToOffset(new Vector2(0, loop.Amount), loop.Ms, easing)
                          .Loop();
                }
                else
                {
                    float baseScale = target.Scale.X <= 0 ? 1f : target.Scale.X;
                    target.ScaleTo(baseScale * (1 + loop.Amount), loop.Ms, easing)
                          .Then().ScaleTo(baseScale, loop.Ms, easing)
                          .Loop();
                }
            }
        }

        internal static BlendingParameters ParseBlend(string blend)
            => (blend ?? "additive").ToLowerInvariant() switch
            {
                "inherit" => BlendingParameters.Inherit,
                "mixture" => BlendingParameters.Mixture,
                "none" => BlendingParameters.None,
                _ => BlendingParameters.Additive,
            };
    }
}
