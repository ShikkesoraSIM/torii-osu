// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Cosmetics.Definitions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osuTK;

namespace Torii.CosmeticCreator.Editor
{
    /// <summary>
    /// torii: panel de propiedades de un AURA. A diferencia del de trails (que escribe en un dict plano),
    /// el aura es un modelo anidado, así que mantenemos un <see cref="DataDrivenAura"/> POCO, lo editamos
    /// con los controles, y en cada cambio lo re-serializamos a Definition.Settings + disparamos
    /// <see cref="Changed"/> para rearmar el preview. Expone lo de más impacto: emisión, glow (control
    /// total), y la primera partícula (forma/tamaño/paleta/movimiento/vida). El estado del panel ES el
    /// JSON exportable.
    /// </summary>
    public partial class AuraPropertyPanel : CompositeDrawable
    {
        public CosmeticDefinition Definition { get; }

        public event Action? Changed;

        private readonly DataDrivenAura data;
        private FillFlowContainer flow = null!;

        public AuraPropertyPanel(CosmeticDefinition definition)
        {
            Definition = definition;
            data = definition.Settings?.ToObject<DataDrivenAura>() ?? new DataDrivenAura();
            if (data.Particles == null || data.Particles.Count == 0)
                data.Particles = new List<ParticleSpec> { defaultParticle() };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = new OsuScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                ScrollbarOverlapsContent = false,
                Child = flow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Padding = new MarginPadding { Horizontal = 12, Vertical = 16 },
                },
            };

            rebuild();
        }

        private static ParticleSpec defaultParticle() => new ParticleSpec
        {
            Shape = "circle",
            SizePx = new[] { 5f, 9f },
            Palette = new[] { "#FFE9A8", "#8FD4FF" },
            SpawnY = new[] { 0.4f, 0.95f },
            DriftY = new[] { -0.8f, -0.35f },
        };

        private void commit()
        {
            Definition.Settings = JObject.FromObject(data);
            Changed?.Invoke();
        }

        private void scheduleRebuild() => Scheduler.AddOnce(rebuild);

        private void rebuild()
        {
            flow.Clear();

            header("Aura");
            nameBox();

            header("Emission");
            sliderD("Spawn interval (ms)", data.SpawnIntervalMs, 80, 800, 10, v => data.SpawnIntervalMs = v);
            sliderD("Spawn jitter (ms)", data.SpawnJitterMs, 0, 500, 10, v => data.SpawnJitterMs = v);
            sliderI("Max particles", data.MaxAlive, 1, 30, v => data.MaxAlive = v);

            header("Glow (behind the name)");
            glowControls();

            var p = data.Particles[0];
            header("Particle");
            shapeControl(p);
            rangeControl("Size (px)", p.SizePx ?? new[] { 5f, 9f }, 1, 40, 0.5f, r => p.SizePx = r);
            paletteControls(p);
            header("Movement");
            enumDropdown("Motion", p.Motion ?? "drift", new[] { "drift", "stationary", "expandInPlace", "holdBeam" }, v => { p.Motion = v; });
            rangeControl("Drift X (× width)", p.DriftX ?? new[] { -0.1f, 0.1f }, -1f, 1f, 0.01f, r => p.DriftX = r);
            rangeControl("Drift Y (× height)", p.DriftY ?? new[] { -0.9f, -0.4f }, -1.2f, 1.2f, 0.01f, r => p.DriftY = r);
            rangeControl("Lifetime (ms)", p.LifetimeMs ?? new[] { 1500f, 2000f }, 300, 4000, 50, r => p.LifetimeMs = r);

            commit();
        }

        // ─────────────────────────── secciones ───────────────────────────

        private void nameBox()
        {
            var current = new Bindable<string>(Definition.Name ?? "Untitled");
            current.BindValueChanged(v =>
            {
                Definition.Name = v.NewValue;
                Definition.Id = slugify(v.NewValue);
                commit();
            });
            flow.Add(new SettingsTextBox { LabelText = "Name", Current = current });
        }

        private void glowControls()
        {
            data.Glow ??= new GlowSpec { Colour = "#A5CDFF" };

            var enabled = new BindableBool(!string.IsNullOrEmpty(data.Glow.Colour));
            enabled.BindValueChanged(v =>
            {
                if (!v.NewValue)
                    data.Glow.Colour = null;
                else if (string.IsNullOrEmpty(data.Glow.Colour))
                    data.Glow.Colour = "#A5CDFF";
                scheduleRebuild();
            });
            flow.Add(new SettingsCheckbox { LabelText = "Glow enabled", Current = enabled });

            if (string.IsNullOrEmpty(data.Glow.Colour))
                return;

            var colour = new Bindable<Colour4>(parseHex(data.Glow.Colour, Colour4.White));
            colour.BindValueChanged(v => { data.Glow.Colour = v.NewValue.ToHex(); commit(); });
            flow.Add(new SettingsColour { LabelText = "Glow colour", Current = colour });

            sliderF("Glow brightness", data.Glow.MaxAlpha, 0.2f, 1f, 0.05f, v => data.Glow.MaxAlpha = v);
            sliderF("Glow blur", data.Glow.BlurSigma, 1f, 10f, 0.5f, v => data.Glow.BlurSigma = v);
            sliderF("Glow pulse (ms)", data.Glow.PulseMs, 300f, 4000f, 100f, v => data.Glow.PulseMs = v);

            var pulsate = new BindableBool(data.Glow.Pulsate);
            pulsate.BindValueChanged(v => { data.Glow.Pulsate = v.NewValue; commit(); });
            flow.Add(new SettingsCheckbox { LabelText = "Glow pulsates", Current = pulsate });
        }

        private void shapeControl(ParticleSpec p)
        {
            // formas procedurales + glyphs, en un solo dropdown. los glyphs llevan prefijo "icon:".
            var shapes = AuraParticleShapes.Names.ToArray();
            var glyphs = AuraParticleShapes.GlyphNames.Select(g => "icon:" + g).OrderBy(s => s).ToArray();
            var items = shapes.Concat(glyphs).ToArray();

            string start = !string.IsNullOrEmpty(p.Icon) ? "icon:" + p.Icon : (p.Shape ?? "circle");
            if (!items.Contains(start))
                start = "circle";

            var current = new Bindable<string>(start);
            current.BindValueChanged(v =>
            {
                if (v.NewValue.StartsWith("icon:", StringComparison.Ordinal))
                {
                    p.Icon = v.NewValue.Substring(5);
                    p.Shape = null;
                }
                else
                {
                    p.Shape = v.NewValue;
                    p.Icon = null;
                }

                commit();
            });
            flow.Add(new SettingsDropdown<string> { LabelText = "Shape", Items = items, Current = current });
        }

        private void paletteControls(ParticleSpec p)
        {
            p.Palette ??= new[] { "#FFFFFFFF" };

            for (int i = 0; i < p.Palette.Length; i++)
            {
                int index = i;
                var colour = new Bindable<Colour4>(parseHex(p.Palette[index], Colour4.White));
                colour.BindValueChanged(v =>
                {
                    p.Palette[index] = v.NewValue.ToHex();
                    commit();
                });
                flow.Add(new SettingsColour { LabelText = $"Colour {index + 1}", Current = colour });
            }

            flow.Add(new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Margin = new MarginPadding { Top = 4, Bottom = 4 },
                Children = new Drawable[]
                {
                    new RoundedButton
                    {
                        Width = 120,
                        Height = 28,
                        Text = "Add colour",
                        Action = () =>
                        {
                            p.Palette = p.Palette.Append("#FFFFFFFF").ToArray();
                            scheduleRebuild();
                        },
                    },
                    new RoundedButton
                    {
                        Width = 120,
                        Height = 28,
                        Text = "Remove last",
                        Action = () =>
                        {
                            if (p.Palette.Length > 1)
                                p.Palette = p.Palette.Take(p.Palette.Length - 1).ToArray();
                            scheduleRebuild();
                        },
                    },
                },
            });
        }

        // ─────────────────────────── controles base ───────────────────────────

        private void header(string title) => flow.Add(new OsuSpriteText
        {
            Text = title.ToUpperInvariant(),
            Font = OsuFont.Torus.With(size: 12, weight: FontWeight.Bold),
            Margin = new MarginPadding { Top = 12, Bottom = 2, Left = 2 },
            Alpha = 0.7f,
        });

        private void sliderF(string label, float value, float min, float max, float step, Action<float> set)
        {
            var current = new BindableFloat(Math.Clamp(value, min, max)) { MinValue = min, MaxValue = max, Precision = step };
            current.BindValueChanged(v => { set(v.NewValue); commit(); });
            flow.Add(new SettingsSlider<float> { LabelText = label, Current = current });
        }

        private void sliderD(string label, double value, float min, float max, float step, Action<double> set)
            => sliderF(label, (float)value, min, max, step, v => set(v));

        private void sliderI(string label, int value, int min, int max, Action<int> set)
        {
            var current = new BindableInt(Math.Clamp(value, min, max)) { MinValue = min, MaxValue = max };
            current.BindValueChanged(v => { set(v.NewValue); commit(); });
            flow.Add(new SettingsSlider<int> { LabelText = label, Current = current });
        }

        // un rango [min,max] editado con dos sliders.
        private void rangeControl(string label, float[] range, float min, float max, float step, Action<float[]> set)
        {
            float lo = range.Length > 0 ? range[0] : min;
            float hi = range.Length > 1 ? range[1] : lo;

            var loB = new BindableFloat(Math.Clamp(lo, min, max)) { MinValue = min, MaxValue = max, Precision = step };
            var hiB = new BindableFloat(Math.Clamp(hi, min, max)) { MinValue = min, MaxValue = max, Precision = step };

            loB.BindValueChanged(v => { set(new[] { v.NewValue, hiB.Value }); commit(); });
            hiB.BindValueChanged(v => { set(new[] { loB.Value, v.NewValue }); commit(); });

            flow.Add(new SettingsSlider<float> { LabelText = $"{label} — min", Current = loB });
            flow.Add(new SettingsSlider<float> { LabelText = $"{label} — max", Current = hiB });
        }

        private void enumDropdown(string label, string value, string[] items, Action<string> set)
        {
            var current = new Bindable<string>(items.Contains(value) ? value : items[0]);
            current.BindValueChanged(v => { set(v.NewValue); commit(); });
            flow.Add(new SettingsDropdown<string> { LabelText = label, Items = items, Current = current });
        }

        private static Colour4 parseHex(string hex, Colour4 fallback)
        {
            try
            {
                return Colour4.FromHex(hex);
            }
            catch
            {
                return fallback;
            }
        }

        private static string slugify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "aura";
            string slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            while (slug.Contains("--"))
                slug = slug.Replace("--", "-");
            return "aura-" + slug.Trim('-');
        }
    }
}
