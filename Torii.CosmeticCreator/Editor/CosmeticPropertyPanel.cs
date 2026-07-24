// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Game.Cosmetics;
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
    /// torii: el panel de propiedades. arma controles (sliders/color/dropdowns/toggles) segun la
    /// FAMILIA del trail y el modo de color activo, cada uno bindeado a la <see cref="CosmeticDefinition"/>
    /// que se esta editando. cualquier cambio escribe en Definition.Settings y dispara <see cref="Changed"/>
    /// -> el preview se rearma. o sea el estado del panel ES el JSON exportable: data-first de punta a
    /// punta. la idea es que se sienta "arma tu propio cosmetico" (paletas libres, tint, etc.), no
    /// "toquetea un preset".
    /// </summary>
    public partial class CosmeticPropertyPanel : CompositeDrawable
    {
        public CosmeticDefinition Definition { get; }

        /// <summary>se dispara cada vez que un parametro cambia (para rearmar el preview).</summary>
        public event Action? Changed;

        [Resolved]
        private Storage storage { get; set; } = null!;

        private FillFlowContainer flow = null!;

        // carpeta portable donde el usuario dropea sus PNGs para partículas custom.
        private const string custom_particles_dir = "custom-particles";

        public CosmeticPropertyPanel(CosmeticDefinition definition)
        {
            Definition = definition;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colours)
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

            Rebuild();
        }

        /// <summary>rearma todos los controles (al cambiar de familia, de modo de color, o de definicion).</summary>
        public void Rebuild()
        {
            if (flow == null)
                return;

            flow.Clear();

            header("Cosmetic");
            name();
            tier();

            switch (Definition.Family ?? CosmeticTrailFamily.Dot)
            {
                case CosmeticTrailFamily.Dot:
                    buildDot();
                    break;

                case CosmeticTrailFamily.Particle:
                    buildParticle();
                    break;

                case CosmeticTrailFamily.Ribbon:
                    buildRibbon();
                    break;
            }

            // los helpers ya poblaron Settings con los defaults de cada param; avisamos para que el
            // preview arranque mostrando el cosmetico completo.
            Changed?.Invoke();
        }

        // reconstruir el panel DESDE un callback de un control (cambio de modo/toggle) tiene que ser
        // diferido: si limpiamos el flow en pleno evento del propio control lo disponemos mientras se
        // ejecuta. lo agendamos para el proximo frame.
        private void scheduleRebuild() => Scheduler.AddOnce(Rebuild);

        // ─────────────────────────── familias ───────────────────────────

        private void buildDot()
        {
            header("Colour");
            colourModeDot();

            switch (dotMode())
            {
                case ToriiCosmeticTrail.TrailColourMode.Solid:
                    colour("Colour", "PrimaryColour", "#FF66AA");
                    break;

                case ToriiCosmeticTrail.TrailColourMode.Gradient:
                    colour("Head colour", "PrimaryColour", "#FF66AA");
                    colour("Tail colour", "SecondaryColour", "#66CCFF");
                    break;

                case ToriiCosmeticTrail.TrailColourMode.Rainbow:
                    slider("Rainbow start (hue)", "HueBase", 0f, 1f, 0.01f, 0f);
                    slider("Rainbow spread", "HueSpread", 0f, 1f, 0.01f, 1f);
                    slider("Rainbow flow speed", "HueCycleSpeed", 0f, 1.5f, 0.01f, 0.25f);
                    break;

                case ToriiCosmeticTrail.TrailColourMode.Palette:
                    paletteEditor();
                    break;
            }

            blending();

            header("Shape & length");
            slider("Thickness", "Thickness", 4f, 48f, 0.5f, 22f);
            slider("Length (fade ms)", "FadeDurationOverride", 150f, 2000f, 10f, 450f);
            slider("Density", "IntervalMultiplierOverride", 0.1f, 1f, 0.01f, 0.5f);
            slider("Fade curve", "FadeExponentOverride", 0.5f, 4f, 0.1f, 1.7f);
        }

        private void buildParticle()
        {
            header("Particle");
            particleShape();
            toggleRebuild("Recolour particles", "UseParticleTint", false);
            if (boolSetting("UseParticleTint", false))
                colour("Particle colour", "ParticleTint", "#FFFFFF");
            blending();

            header("Motion & length");
            driftComponent("Drift X", 0, -60f, 60f, 1f, 0f);
            driftComponent("Drift Y", 1, -60f, 60f, 1f, -12f);
            slider("Drift jitter", "DriftJitter", 0f, 40f, 1f, 10f);
            slider("Spin (deg)", "SpinDegrees", 0f, 360f, 5f, 0f);
            slider("Spawn interval", "SpawnInterval", 6f, 60f, 1f, 16f);
            slider("Lifetime (ms)", "ParticleLifetime", 200f, 1600f, 10f, 700f);
            slider("Start scale", "StartScale", 0.1f, 3f, 0.05f, 1f);
            slider("End scale", "EndScale", 0.1f, 3f, 0.05f, 0.5f);
            slider("Max alive", "MaxAlive", 20f, 300f, 5f, 160f);
        }

        private void buildRibbon()
        {
            header("Colour");
            colourModeRibbon();

            switch (ribbonMode())
            {
                case CosmeticRibbonTrail.RibbonColourMode.Solid:
                    colour("Colour", "PrimaryColour", "#FF66AA");
                    break;

                case CosmeticRibbonTrail.RibbonColourMode.Gradient:
                    colour("Head colour", "PrimaryColour", "#FF66AA");
                    colour("Tail colour", "SecondaryColour", "#66CCFF");
                    break;

                case CosmeticRibbonTrail.RibbonColourMode.Rainbow:
                    slider("Rainbow spread", "HueSpread", 0f, 1f, 0.01f, 1f);
                    slider("Rainbow flow speed", "HueCycleSpeed", 0f, 1.5f, 0.01f, 0.35f);
                    break;

                case CosmeticRibbonTrail.RibbonColourMode.Palette:
                    paletteEditor();
                    break;
            }

            header("Glow & head");
            toggleRebuild("Glow", "Glow", true);
            if (boolSetting("Glow", true))
                colour("Glow colour", "GlowColour", "#FFFFFF");
            toggleRebuild("Head dot", "HeadDot", false);
            if (boolSetting("HeadDot", false))
                slider("Head dot scale", "HeadDotScale", 1f, 5f, 0.1f, 1.4f);

            header("Shape & length");
            toggleRebuild("Segmented (taper)", "Segmented", false);
            if (boolSetting("Segmented", false))
            {
                slider("Head width", "HeadWidth", 3f, 30f, 0.5f, 12f);
                slider("Tail width", "TailWidth", 1f, 30f, 0.5f, 4f);
                toggle("Fade tail", "FadeTail", true);
            }
            else
                slider("Width", "Width", 3f, 30f, 0.5f, 10f);

            slider("Length (ms)", "RibbonLifetime", 200f, 1600f, 10f, 550f);

            header("Effects");
            toggleRebuild("RGB split (glitch)", "RgbSplit", false);
            if (boolSetting("RgbSplit", false))
                slider("RGB split offset", "RgbSplitOffset", 0f, 10f, 0.5f, 3f);
            slider("Pulse amount", "PulseAmount", 0f, 1f, 0.05f, 0f);
            slider("Pulse speed", "PulseSpeed", 0.2f, 4f, 0.1f, 1.6f);
        }

        // ─────────────────────────── colour mode ───────────────────────────

        private ToriiCosmeticTrail.TrailColourMode dotMode()
            => Enum.TryParse((string?)Definition.Settings["ColourMode"], true, out ToriiCosmeticTrail.TrailColourMode m) ? m : ToriiCosmeticTrail.TrailColourMode.Solid;

        private CosmeticRibbonTrail.RibbonColourMode ribbonMode()
            => Enum.TryParse((string?)Definition.Settings["ColourMode"], true, out CosmeticRibbonTrail.RibbonColourMode m) ? m : CosmeticRibbonTrail.RibbonColourMode.Solid;

        private void colourModeDot()
        {
            var start = dotMode();
            var current = new Bindable<ToriiCosmeticTrail.TrailColourMode>(start);
            Definition.Settings["ColourMode"] = start.ToString();
            current.BindValueChanged(v =>
            {
                Definition.Settings["ColourMode"] = v.NewValue.ToString();
                Changed?.Invoke();  // rearma el preview
                scheduleRebuild();  // rearma el panel (mostrar el control de color correcto)
            });
            flow.Add(new SettingsEnumDropdown<ToriiCosmeticTrail.TrailColourMode> { LabelText = "Colour mode", Current = current });
        }

        private void colourModeRibbon()
        {
            var start = ribbonMode();
            var current = new Bindable<CosmeticRibbonTrail.RibbonColourMode>(start);
            Definition.Settings["ColourMode"] = start.ToString();
            current.BindValueChanged(v =>
            {
                Definition.Settings["ColourMode"] = v.NewValue.ToString();
                Changed?.Invoke();
                scheduleRebuild();
            });
            flow.Add(new SettingsEnumDropdown<CosmeticRibbonTrail.RibbonColourMode> { LabelText = "Colour mode", Current = current });
        }

        // ─────────────────────────── palette editor ───────────────────────────

        // presets de arranque: el usuario carga uno y despues edita/agrega/saca colores libremente.
        private static readonly (string name, string[] hexes)[] palette_presets =
        {
            ("Aurora", new[] { "#6EE7B7", "#3B82F6", "#9333EA" }),
            ("Sunset", new[] { "#FFD166", "#EF476F", "#6A0572" }),
            ("Sakura", new[] { "#FFC2D1", "#FF8FAB", "#FB6F92" }),
            ("Ocean", new[] { "#48CAE4", "#0096C7", "#023E8A" }),
            ("Neon", new[] { "#39FF14", "#FF00FF", "#00FFFF" }),
        };

        private void paletteEditor()
        {
            var arr = Definition.Settings["Palette"] as JArray;
            if (arr == null || arr.Count == 0)
            {
                arr = new JArray("#FF6699", "#66CCFF", "#B266FF");
                Definition.Settings["Palette"] = arr;
            }

            header("Palette");
            flow.Add(new OsuSpriteText
            {
                Text = "Colours blend head → tail. Add as many as you want.",
                Font = OsuFont.Torus.With(size: 11),
                Alpha = 0.6f,
                Margin = new MarginPadding { Bottom = 4, Left = 2 },
            });

            flow.Add(palettePresetRow());

            for (int i = 0; i < arr.Count; i++)
                paletteColour(arr, i);

            flow.Add(new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Margin = new MarginPadding { Top = 6 },
                Children = new Drawable[]
                {
                    new RoundedButton
                    {
                        Width = 130,
                        Height = 30,
                        Text = "+ Add colour",
                        Action = () => { addPaletteColour(); scheduleRebuild(); },
                    },
                    new RoundedButton
                    {
                        Width = 130,
                        Height = 30,
                        Text = "− Remove last",
                        Action = () => { removeLastPaletteColour(); scheduleRebuild(); },
                    },
                },
            });
        }

        private Drawable palettePresetRow()
        {
            var row = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new Vector2(6, 6),
                Margin = new MarginPadding { Bottom = 6 },
            };

            foreach (var preset in palette_presets)
            {
                var p = preset;
                row.Add(new RoundedButton
                {
                    Width = 84,
                    Height = 26,
                    Text = p.name,
                    Action = () => { loadPalettePreset(p.hexes); scheduleRebuild(); },
                });
            }

            return row;
        }

        private void paletteColour(JArray arr, int index)
        {
            string hex = (string?)arr[index] ?? "#FFFFFF";
            var current = new Bindable<Colour4>(Colour4.FromHex(hex));
            current.BindValueChanged(v =>
            {
                var a = Definition.Settings["Palette"] as JArray;
                if (a != null && index < a.Count)
                {
                    a[index] = v.NewValue.ToHex();
                    Changed?.Invoke();
                }
            });
            flow.Add(new SettingsColour { LabelText = $"Colour {index + 1}", Current = current });
        }

        private void addPaletteColour()
        {
            var arr = Definition.Settings["Palette"] as JArray ?? new JArray();
            arr.Add("#FFFFFF");
            Definition.Settings["Palette"] = arr;
        }

        private void removeLastPaletteColour()
        {
            var arr = Definition.Settings["Palette"] as JArray;
            if (arr != null && arr.Count > 1)
                arr.RemoveAt(arr.Count - 1);
        }

        private void loadPalettePreset(string[] hexes)
        {
            Definition.Settings["Palette"] = new JArray(hexes.Cast<object>().ToArray());
            Changed?.Invoke();
        }

        // ─────────────────────────── controles base ───────────────────────────

        private void header(string title) => flow.Add(new OsuSpriteText
        {
            Text = title.ToUpperInvariant(),
            Font = OsuFont.Torus.With(size: 12, weight: FontWeight.Bold),
            Margin = new MarginPadding { Top = 12, Bottom = 2, Left = 2 },
            Alpha = 0.7f,
        });

        private void name()
        {
            var current = new Bindable<string>(Definition.Name ?? string.Empty);
            current.BindValueChanged(v =>
            {
                Definition.Name = v.NewValue;
                Definition.Id = slugify(v.NewValue);
                Changed?.Invoke();
            });
            flow.Add(new SettingsTextBox { LabelText = "Name", Current = current });
        }

        private void tier()
        {
            var current = new Bindable<CosmeticTier>(Definition.Tier);
            current.BindValueChanged(v => { Definition.Tier = v.NewValue; Changed?.Invoke(); });
            flow.Add(new SettingsEnumDropdown<CosmeticTier> { LabelText = "Tier", Current = current });
        }

        private void blending()
        {
            var start = (string?)Definition.Settings["Blending"] ?? "Inherit";
            var current = new Bindable<string>(start);
            Definition.Settings["Blending"] = start;
            current.BindValueChanged(v => set("Blending", v.NewValue));
            flow.Add(new SettingsDropdown<string> { LabelText = "Blending", Items = new[] { "Inherit", "Additive", "Mixture" }, Current = current });
        }

        // dropdown de forma de particula: formas built-in + las imagenes PNG que el usuario dropeo en la
        // carpeta portable (entradas "img:archivo.png"). elegir una imagen la embebe (base64) en la
        // definicion, asi el cosmetico queda portable/compartible para contests.
        private void particleShape()
        {
            var cust = storage.GetStorageForDirectory(custom_particles_dir);

            string[] customFiles;
            try
            {
                customFiles = cust.GetFiles(string.Empty, "*.png")
                                  .Select(f => Path.GetFileName(f) ?? f)
                                  .OrderBy(n => n)
                                  .ToArray();
            }
            catch
            {
                customFiles = Array.Empty<string>();
            }

            string[] builtins = CosmeticParticleShapes.Names.OrderBy(n => n).ToArray();
            string[] items = builtins.Concat(customFiles.Select(f => "img:" + f)).ToArray();

            string start = (string?)Definition.Settings["ParticleShape"] ?? "bubble";
            if (!items.Contains(start))
                start = "bubble";
            Definition.Settings["ParticleShape"] = start;
            syncCustomImage(start, cust);

            var current = new Bindable<string>(start);
            current.BindValueChanged(v =>
            {
                Definition.Settings["ParticleShape"] = v.NewValue;
                syncCustomImage(v.NewValue, cust);
                Changed?.Invoke();
            });
            flow.Add(new SettingsDropdown<string> { LabelText = "Shape", Items = items, Current = current });

            flow.Add(new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Margin = new MarginPadding { Top = 4 },
                Children = new Drawable[]
                {
                    new RoundedButton
                    {
                        Width = 152,
                        Height = 28,
                        Text = "Open images folder",
                        Action = () => { try { cust.PresentExternally(); } catch { } },
                    },
                    new RoundedButton
                    {
                        Width = 98,
                        Height = 28,
                        Text = "Refresh",
                        Action = scheduleRebuild,
                    },
                },
            });

            flow.Add(new OsuSpriteText
            {
                Text = "Drop PNGs (≤256px) in that folder, Refresh, then pick \"img:…\".",
                Font = OsuFont.Torus.With(size: 11),
                Alpha = 0.6f,
                Margin = new MarginPadding { Top = 2, Bottom = 4, Left = 2 },
            });
        }

        // mantiene Settings["CustomImage"] (base64) en sync con la seleccion del dropdown: si es una
        // entrada "img:archivo", lee el PNG y lo embebe (con tope de tamano); si no, lo saca.
        private void syncCustomImage(string selection, Storage cust)
        {
            if (selection != null && selection.StartsWith("img:", StringComparison.Ordinal))
            {
                string file = selection.Substring(4);
                try
                {
                    if (cust.Exists(file))
                    {
                        using var s = cust.GetStream(file);
                        using var mem = new MemoryStream();
                        s.CopyTo(mem);
                        if (mem.Length > 0 && mem.Length <= 500_000)
                        {
                            Definition.Settings["CustomImage"] = Convert.ToBase64String(mem.ToArray());
                            return;
                        }
                    }
                }
                catch
                {
                }
            }

            Definition.Settings.Remove("CustomImage");
        }

        // un componente (X o Y) del array Drift=[x,y], que es como el runtime espera el Vector2.
        private void driftComponent(string label, int index, float min, float max, float step, float defaultVal)
        {
            var arr = Definition.Settings["Drift"] as JArray;
            if (arr == null || arr.Count < 2)
            {
                arr = new JArray(0f, -12f);
                Definition.Settings["Drift"] = arr;
            }

            float start = arr[index]?.Value<float>() ?? defaultVal;
            var current = new BindableFloat(start) { MinValue = min, MaxValue = max, Precision = step };
            current.BindValueChanged(v =>
            {
                var a = Definition.Settings["Drift"] as JArray ?? new JArray(0f, 0f);
                while (a.Count < 2) a.Add(0f);
                a[index] = v.NewValue;
                Definition.Settings["Drift"] = a;
                Changed?.Invoke();
            });
            flow.Add(new SettingsSlider<float> { LabelText = label, Current = current, KeyboardStep = step });
        }

        private void slider(string label, string key, float min, float max, float step, float defaultVal)
        {
            float start = Definition.Settings[key]?.Value<float>() ?? defaultVal;
            var current = new BindableFloat(start) { MinValue = min, MaxValue = max, Precision = step };
            Definition.Settings[key] = start;
            current.BindValueChanged(v => set(key, v.NewValue));
            flow.Add(new SettingsSlider<float> { LabelText = label, Current = current, KeyboardStep = step });
        }

        private void toggle(string label, string key, bool defaultVal)
        {
            bool start = boolSetting(key, defaultVal);
            var current = new BindableBool(start);
            Definition.Settings[key] = start;
            current.BindValueChanged(v => set(key, v.NewValue));
            flow.Add(new SettingsCheckbox { LabelText = label, Current = current });
        }

        // igual que toggle pero rearma el panel al cambiar (para mostrar/ocultar controles dependientes).
        private void toggleRebuild(string label, string key, bool defaultVal)
        {
            bool start = boolSetting(key, defaultVal);
            var current = new BindableBool(start);
            Definition.Settings[key] = start;
            current.BindValueChanged(v =>
            {
                Definition.Settings[key] = v.NewValue;
                Changed?.Invoke();
                scheduleRebuild();
            });
            flow.Add(new SettingsCheckbox { LabelText = label, Current = current });
        }

        private void colour(string label, string key, string defaultHex)
        {
            string startHex = (string?)Definition.Settings[key] ?? defaultHex;
            var start = Colour4.FromHex(startHex);
            var current = new Bindable<Colour4>(start);
            Definition.Settings[key] = startHex;
            current.BindValueChanged(v => set(key, v.NewValue.ToHex()));
            flow.Add(new SettingsColour { LabelText = label, Current = current });
        }

        // ─────────────────────────── helpers ───────────────────────────

        private bool boolSetting(string key, bool defaultVal) => Definition.Settings[key]?.Value<bool>() ?? defaultVal;

        private void set(string key, JToken value)
        {
            Definition.Settings[key] = value;
            Changed?.Invoke();
        }

        private static string slugify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "cosmetic-untitled";

            string slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            while (slug.Contains("--"))
                slug = slug.Replace("--", "-");
            return "trail-" + slug.Trim('-');
        }
    }
}
