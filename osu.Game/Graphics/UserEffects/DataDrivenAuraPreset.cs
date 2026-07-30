// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Cosmetics.Definitions;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects
{
    /// <summary>
    /// torii: un <see cref="AuraPreset"/> manejado 100% por DATOS (<see cref="DataDrivenAura"/>) en vez
    /// de C# a mano. Es el puente entre el runtime data-driven (lo que exporta la Cosmetic Creator) y el
    /// pipeline de auras existente: el <see cref="AuraRegistry"/> y el <see cref="ParticleAuraEmitter"/>
    /// lo tratan igual que cualquier preset hardcodeado, así los dos conviven sin distinción.
    ///
    /// Un aura de la comunidad se POSEE como cosmético (no se gana por grupo), así que
    /// <see cref="OwningGroupIdentifiers"/> es vacío: el entitlement lo maneja el server por posesión.
    /// </summary>
    public partial class DataDrivenAuraPreset : AuraPreset
    {
        private readonly string auraId;
        private readonly DataDrivenAura data;
        private readonly Random random = new Random();

        public DataDrivenAuraPreset(string auraId, DataDrivenAura data)
        {
            this.auraId = auraId;
            this.data = data ?? new DataDrivenAura();
        }

        public override string AuraId => auraId;

        // Auras data-driven se desbloquean como cosmético (regalo/compra), no por rol.
        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = Array.Empty<string>();

        public override double SpawnIntervalMs => data.SpawnIntervalMs;
        public override double SpawnJitterMs => data.SpawnJitterMs;
        public override int MaxAlive => Math.Clamp(data.MaxAlive, 1, 40);

        public override Color4? GlowColour => parseGlowColour();

        /// <summary>torii: parámetros completos del glow (blur/pulso/intensidad) para el control total.</summary>
        public override AuraGlowSettings? GlowSettings => buildGlowSettings();

        public override void EmitParticle(Container parent, Vector2 parentSize, Random rng)
        {
            if (data.Particles == null || data.Particles.Count == 0)
                return;

            var spec = pickWeighted(data.Particles, p => p.Weight, rng);
            if (spec == null)
                return;

            float scale = ParticleScale(parentSize);
            parent.Add(AuraParticleBuilder.Build(spec, parentSize, scale, rng));
        }

        public override Drawable? CreateBackground() => buildOrnament(data.Background);
        public override Drawable? CreateLeadingOrnament() => buildOrnament(data.LeadingOrnament);
        public override Drawable? CreateTrailingOrnament() => buildOrnament(data.TrailingOrnament);

        private Color4? parseGlowColour()
        {
            if (string.IsNullOrEmpty(data.Glow?.Colour))
                return null;
            return CosmeticSettingsBinder.ParseColour(data.Glow.Colour);
        }

        private AuraGlowSettings? buildGlowSettings()
        {
            if (data.Glow == null || string.IsNullOrEmpty(data.Glow.Colour))
                return null;

            return new AuraGlowSettings
            {
                Colour = CosmeticSettingsBinder.ParseColour(data.Glow.Colour),
                MinAlpha = Math.Clamp(data.Glow.MinAlpha, 0f, 1f),
                MaxAlpha = Math.Clamp(data.Glow.MaxAlpha, 0f, 1f),
                BlurSigma = Math.Clamp(data.Glow.BlurSigma, 0f, 12f),
                PulseMs = Math.Clamp(data.Glow.PulseMs, 100f, 6000f),
                Pulsate = data.Glow.Pulsate,
            };
        }

        private static T? pickWeighted<T>(IReadOnlyList<T> items, Func<T, float> weight, Random rng) where T : class
        {
            if (items == null || items.Count == 0)
                return null;
            float total = items.Sum(weight);
            if (total <= 0)
                return items[rng.Next(items.Count)];
            float roll = (float)rng.NextDouble() * total;
            foreach (var it in items)
            {
                roll -= weight(it);
                if (roll <= 0)
                    return it;
            }
            return items[items.Count - 1];
        }

        private Drawable? buildOrnament(OrnamentSpec ornament)
        {
            if (ornament?.Layers == null || ornament.Layers.Count == 0)
                return null;

            return new OrnamentDrawable(ornament);
        }

        /// <summary>Un sello/ornamento: stack de capas concéntricas + respiración opcional.</summary>
        private partial class OrnamentDrawable : CompositeDrawable
        {
            private readonly OrnamentSpec spec;

            public OrnamentDrawable(OrnamentSpec spec)
            {
                this.spec = spec;
                Origin = Anchor.Centre;
                Anchor = Anchor.Centre;
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var container = new Container { Origin = Anchor.Centre, Anchor = Anchor.Centre, AutoSizeAxes = Axes.Both };

                foreach (var layer in spec.Layers)
                {
                    float ls = spec.BaseSizePx * layer.SizeRatio;
                    Color4 lc = CosmeticSettingsBinder.ParseColour(layer.Colour);
                    var blend = AuraParticleBuilder.ParseBlend(layer.Blend);

                    Drawable d;
                    switch ((layer.Kind ?? "fillCircle").ToLowerInvariant())
                    {
                        case "ringglyph":
                        case "iconglyph":
                            var icon = AuraParticleShapes.ResolveGlyph(layer.Icon);
                            d = icon != null
                                ? new SpriteIcon { Anchor = Anchor.Centre, Origin = Anchor.Centre, Icon = icon.Value, Size = new Vector2(ls), Colour = lc }
                                : new Circle { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(ls), Colour = lc };
                            break;

                        default: // fillCircle
                            d = new Circle { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(ls), Colour = lc };
                            break;
                    }

                    d.Alpha = layer.Alpha;
                    d.Blending = blend;
                    container.Add(d);
                }

                InternalChild = container;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                if (spec.Breath != null)
                {
                    this.FadeTo(spec.Breath.MaxAlpha)
                        .FadeTo(spec.Breath.MinAlpha, spec.Breath.HalfPeriodMs, AuraParticleBuilder.ParseEasing(spec.Breath.Easing))
                        .Then().FadeTo(spec.Breath.MaxAlpha, spec.Breath.HalfPeriodMs, AuraParticleBuilder.ParseEasing(spec.Breath.Easing))
                        .Loop();
                }
            }
        }
    }

    /// <summary>
    /// torii: parámetros completos del text-shape glow que un preset puede exponer para control total
    /// (color + pulso + intensidad + blur), en vez del solo <see cref="AuraPreset.GlowColour"/>.
    /// <see cref="UserAuraContainer"/> los usa cuando el preset los provee; sino usa sus defaults.
    /// </summary>
    public class AuraGlowSettings
    {
        public Color4 Colour { get; set; } = Color4.White;
        public float MinAlpha { get; set; } = 0.5f;
        public float MaxAlpha { get; set; } = 0.9f;
        public float BlurSigma { get; set; } = 4f;
        public float PulseMs { get; set; } = 1500f;
        public bool Pulsate { get; set; } = true;
    }
}
