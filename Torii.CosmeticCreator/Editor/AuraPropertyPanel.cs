// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Game.Cosmetics.Definitions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osuTK;

namespace Torii.CosmeticCreator.Editor
{
    /// <summary>
    /// torii: panel de propiedades de un AURA. Un aura es una MEZCLA de tipos de partícula, así que el
    /// panel muestra una lista de partículas: agregás las que quieras y clic en una para editarla (forma
    /// —procedural, glyph o TU PNG—, peso en la mezcla, paleta, tamaño, movimiento, vida). Mantenemos un
    /// <see cref="DataDrivenAura"/> POCO, lo editamos con los controles y en cada cambio lo re-serializamos
    /// a Definition.Settings + disparamos <see cref="Changed"/> → el preview se rearma. El estado del panel
    /// ES el JSON exportable.
    /// </summary>
    public partial class AuraPropertyPanel : CompositeDrawable
    {
        public CosmeticDefinition Definition { get; }

        public event Action? Changed;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colours { get; set; } = null!;

        private readonly DataDrivenAura data;
        private FillFlowContainer flow = null!;

        private int selected;

        // carpeta portable donde el usuario dropea sus PNGs para partículas custom (igual que trails).
        private const string custom_particles_dir = "custom-particles";

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
            Weight = 1,
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

            selected = Math.Clamp(selected, 0, data.Particles.Count - 1);

            header("Aura");
            nameBox();

            header("Emission");
            sliderD("Spawn interval (ms)", data.SpawnIntervalMs, 80, 800, 10, v => data.SpawnIntervalMs = v);
            sliderD("Spawn jitter (ms)", data.SpawnJitterMs, 0, 500, 10, v => data.SpawnJitterMs = v);
            sliderI("Max particles", data.MaxAlive, 1, 30, v => data.MaxAlive = v);

            header("Glow (behind the name)");
            glowControls();

            header($"Particles ({data.Particles.Count})");
            particleTabs();

            var p = data.Particles[selected];
            header($"Particle {selected + 1}");
            sliderF("Mix weight", p.Weight, 0.1f, 10f, 0.1f, v => p.Weight = v);
            shapeControl(p);
            rangeControl("Size (px)", p.SizePx ?? new[] { 5f, 9f }, 1, 40, 0.5f, r => p.SizePx = r);
            paletteControls(p);

            header("Movement");
            motionDropdown(p);
            if (isCircular(p.Motion))
            {
                rangeControl("Orbit radius (× height)", p.OrbitRadius ?? new[] { 0.28f, 0.28f }, 0.05f, 0.8f, 0.01f, r => p.OrbitRadius = r);
                sliderF("Orbit turns / cycles", p.OrbitTurns, 0.25f, 6f, 0.25f, v => p.OrbitTurns = v);
            }
            rangeControl("Spawn X (× width)", p.SpawnX ?? new[] { 0f, 1f }, -0.3f, 1.3f, 0.02f, r => p.SpawnX = r);
            rangeControl("Spawn Y (× height)", p.SpawnY ?? new[] { 0.4f, 0.95f }, -0.3f, 1.3f, 0.02f, r => p.SpawnY = r);
            rangeControl("Drift X (× width)", p.DriftX ?? new[] { -0.1f, 0.1f }, -1f, 1f, 0.01f, r => p.DriftX = r);
            rangeControl("Drift Y (× height)", p.DriftY ?? new[] { -0.9f, -0.4f }, -1.2f, 1.2f, 0.01f, r => p.DriftY = r);
            rangeControl("Lifetime (ms)", p.LifetimeMs ?? new[] { 1500f, 2000f }, 300, 4000, 50, r => p.LifetimeMs = r);
            rangeControl("Spin over life (deg)", p.Anim?.RotateOverLife ?? new[] { 0f, 0f }, -360, 360, 5, r => ensureAnim(p).RotateOverLife = allZero(r) ? null : r);

            commit();
        }

        // ─────────────────────────── partículas (lista) ───────────────────────────

        private void particleTabs()
        {
            var row = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new Vector2(6, 6),
                Margin = new MarginPadding { Bottom = 4 },
            };

            for (int i = 0; i < data.Particles.Count; i++)
            {
                int index = i;
                row.Add(new RoundedButton
                {
                    Width = 52,
                    Height = 28,
                    Text = $"#{i + 1}",
                    BackgroundColour = index == selected ? colours.Colour1 : colours.Background3,
                    Action = () => { selected = index; scheduleRebuild(); },
                });
            }

            row.Add(new RoundedButton
            {
                Width = 60,
                Height = 28,
                Text = "+ Add",
                BackgroundColour = colours.Colour2,
                Action = () =>
                {
                    data.Particles.Add(defaultParticle());
                    selected = data.Particles.Count - 1;
                    scheduleRebuild();
                },
            });

            if (data.Particles.Count > 1)
            {
                row.Add(new RoundedButton
                {
                    Width = 74,
                    Height = 28,
                    Text = "− Remove",
                    Action = () =>
                    {
                        data.Particles.RemoveAt(selected);
                        selected = Math.Max(0, selected - 1);
                        scheduleRebuild();
                    },
                });
            }

            flow.Add(row);
        }

        // ─────────────────────────── forma (con PNG propio) ───────────────────────────

        private void shapeControl(ParticleSpec p)
        {
            var cust = storage.GetStorageForDirectory(custom_particles_dir);

            string[] customFiles;
            try
            {
                customFiles = cust.GetFiles(string.Empty, "*.png").Select(f => Path.GetFileName(f) ?? f).OrderBy(n => n).ToArray();
            }
            catch
            {
                customFiles = Array.Empty<string>();
            }

            var items = new List<string>();
            items.AddRange(AuraParticleShapes.Names);
            items.AddRange(AuraParticleShapes.GlyphNames.Select(g => "icon:" + g).OrderBy(s => s));
            items.AddRange(customFiles.Select(f => "img:" + f));

            // si ya tiene una imagen embebida (ej de un import), la representamos con una entrada propia.
            const string uploaded = "(uploaded image)";
            bool hasEmbedded = !string.IsNullOrEmpty(p.CustomImage);
            if (hasEmbedded)
                items.Insert(0, uploaded);

            string start = hasEmbedded ? uploaded
                : !string.IsNullOrEmpty(p.Icon) ? "icon:" + p.Icon
                : (p.Shape ?? "circle");
            if (!items.Contains(start))
                start = "circle";

            var current = new Bindable<string>(start);
            current.BindValueChanged(v =>
            {
                applyShapeSelection(p, v.NewValue, cust);
                commit();
            });
            flow.Add(new SettingsDropdown<string> { LabelText = "Shape / image", Items = items.ToArray(), Current = current });

            flow.Add(new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Margin = new MarginPadding { Top = 4 },
                Children = new Drawable[]
                {
                    new RoundedButton { Width = 152, Height = 28, Text = "Open images folder", Action = () => { try { cust.PresentExternally(); } catch { } } },
                    new RoundedButton { Width = 88, Height = 28, Text = "Refresh", Action = scheduleRebuild },
                },
            });

            flow.Add(new OsuSpriteText
            {
                Text = "Drop a PNG (≤256px) in that folder, Refresh, then pick \"img:…\" for THIS particle.",
                Font = OsuFont.Torus.With(size: 11),
                Alpha = 0.6f,
                Margin = new MarginPadding { Top = 2, Bottom = 4, Left = 2 },
            });
        }

        private void applyShapeSelection(ParticleSpec p, string sel, Storage cust)
        {
            if (sel == "(uploaded image)")
                return; // ya está embebida, no tocar.

            if (sel.StartsWith("img:", StringComparison.Ordinal))
            {
                string file = sel.Substring(4);
                string? b64 = readBase64(cust, file);
                if (b64 != null)
                {
                    p.CustomImage = b64;
                    p.Shape = null;
                    p.Icon = null;
                }
                return;
            }

            p.CustomImage = null;

            if (sel.StartsWith("icon:", StringComparison.Ordinal))
            {
                p.Icon = sel.Substring(5);
                p.Shape = null;
            }
            else
            {
                p.Shape = sel;
                p.Icon = null;
            }
        }

        private static string? readBase64(Storage cust, string file)
        {
            try
            {
                if (!cust.Exists(file))
                    return null;
                using var s = cust.GetStream(file);
                using var mem = new MemoryStream();
                s.CopyTo(mem);
                if (mem.Length > 0 && mem.Length <= 500_000)
                    return Convert.ToBase64String(mem.ToArray());
            }
            catch
            {
            }

            return null;
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
                        Action = () => { p.Palette = p.Palette.Append("#FFFFFFFF").ToArray(); scheduleRebuild(); },
                    },
                    new RoundedButton
                    {
                        Width = 120,
                        Height = 28,
                        Text = "Remove last",
                        Action = () => { if (p.Palette.Length > 1) p.Palette = p.Palette.Take(p.Palette.Length - 1).ToArray(); scheduleRebuild(); },
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

        // los modos de movimiento. cambiar el modo rearma el panel para mostrar/ocultar los controles
        // de órbita (radio/vueltas) que solo aplican a los circulares.
        private static readonly string[] motion_modes =
        {
            "drift", "rise", "fall", "burst", "orbit", "spiral", "zigzag", "pendulum", "popInPlace", "ripple", "beam",
        };

        private void motionDropdown(ParticleSpec p)
        {
            var current = new Bindable<string>(motion_modes.Contains(p.Motion) ? p.Motion : "drift");
            current.BindValueChanged(v => { p.Motion = v.NewValue; scheduleRebuild(); });
            flow.Add(new SettingsDropdown<string> { LabelText = "Motion", Items = motion_modes, Current = current });
        }

        private static bool isCircular(string? motion) => motion is "orbit" or "spiral" or "zigzag" or "pendulum";

        private static AnimSpec ensureAnim(ParticleSpec p) => p.Anim ??= new AnimSpec();

        private static bool allZero(float[] r) => r.All(v => Math.Abs(v) < 0.01f);

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
