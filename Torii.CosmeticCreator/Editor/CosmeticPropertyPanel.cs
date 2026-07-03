// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Game.Cosmetics;
using osu.Game.Cosmetics.Definitions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osuTK;

namespace Torii.CosmeticCreator.Editor
{
    /// <summary>
    /// torii: el panel de propiedades. arma controles (sliders/color/dropdowns/toggles) segun la
    /// FAMILIA del trail, cada uno bindeado a la <see cref="CosmeticDefinition"/> que se esta editando.
    /// cualquier cambio escribe en Definition.Settings y dispara <see cref="Changed"/> -> el preview se
    /// rearma. o sea el estado del panel ES el JSON exportable: data-first de punta a punta.
    /// </summary>
    public partial class CosmeticPropertyPanel : CompositeDrawable
    {
        public CosmeticDefinition Definition { get; }

        /// <summary>se dispara cada vez que un parametro cambia (para rearmar el preview).</summary>
        public event Action? Changed;

        private FillFlowContainer flow = null!;

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

        /// <summary>rearma todos los controles (al cambiar de familia o cargar otra definicion).</summary>
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

        // ─────────────────────────── familias ───────────────────────────

        private void buildDot()
        {
            header("Colour");
            colourModeDot();
            colour("Primary colour", "PrimaryColour", "#FFFFFF");
            colour("Secondary colour (gradient)", "SecondaryColour", "#FFFFFF");
            slider("Hue cycle speed (rainbow)", "HueCycleSpeed", 0f, 1.5f, 0.01f, 0.25f);
            slider("Hue spread (rainbow)", "HueSpread", 0f, 1f, 0.01f, 1f);
            blending();

            header("Shape & length");
            slider("Thickness", "Thickness", 4f, 48f, 0.5f, 22f);
            slider("Length (fade ms)", "FadeDurationOverride", 150f, 2000f, 10f, 450f);
            slider("Density", "IntervalMultiplierOverride", 0.1f, 1f, 0.01f, 0.5f);
        }

        private void buildParticle()
        {
            header("Particle");
            shape("Shape", "ParticleShape", "bubble");
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
            colour("Primary colour", "PrimaryColour", "#FFFFFF");
            colour("Secondary colour (gradient)", "SecondaryColour", "#FFFFFF");
            slider("Hue cycle speed (rainbow)", "HueCycleSpeed", 0f, 1.5f, 0.01f, 0.35f);

            header("Glow & head");
            toggle("Glow", "Glow", true);
            colour("Glow colour", "GlowColour", "#FFFFFF");
            toggle("Head dot", "HeadDot", false);
            slider("Head dot scale", "HeadDotScale", 1f, 5f, 0.1f, 1.4f);

            header("Shape & length");
            toggle("Segmented (taper)", "Segmented", false);
            slider("Width", "Width", 3f, 30f, 0.5f, 10f);
            slider("Head width (segmented)", "HeadWidth", 3f, 30f, 0.5f, 12f);
            slider("Tail width (segmented)", "TailWidth", 1f, 30f, 0.5f, 4f);
            toggle("Fade tail", "FadeTail", true);
            slider("Length (ms)", "RibbonLifetime", 200f, 1600f, 10f, 550f);

            header("Effects");
            toggle("RGB split (glitch)", "RgbSplit", false);
            slider("RGB split offset", "RgbSplitOffset", 0f, 10f, 0.5f, 3f);
            slider("Pulse amount", "PulseAmount", 0f, 1f, 0.05f, 0f);
            slider("Pulse speed", "PulseSpeed", 0.2f, 4f, 0.1f, 1.6f);
        }

        // ─────────────────────────── controles ───────────────────────────

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

        private void colourModeDot()
        {
            var start = Enum.TryParse((string?)Definition.Settings["ColourMode"], true, out ToriiCosmeticTrail.TrailColourMode m) ? m : ToriiCosmeticTrail.TrailColourMode.Solid;
            var current = new Bindable<ToriiCosmeticTrail.TrailColourMode>(start);
            Definition.Settings["ColourMode"] = start.ToString();
            current.BindValueChanged(v => { set("ColourMode", v.NewValue.ToString()); });
            flow.Add(new SettingsEnumDropdown<ToriiCosmeticTrail.TrailColourMode> { LabelText = "Colour mode", Current = current });
        }

        private void colourModeRibbon()
        {
            var start = Enum.TryParse((string?)Definition.Settings["ColourMode"], true, out CosmeticRibbonTrail.RibbonColourMode m) ? m : CosmeticRibbonTrail.RibbonColourMode.Solid;
            var current = new Bindable<CosmeticRibbonTrail.RibbonColourMode>(start);
            Definition.Settings["ColourMode"] = start.ToString();
            current.BindValueChanged(v => { set("ColourMode", v.NewValue.ToString()); });
            flow.Add(new SettingsEnumDropdown<CosmeticRibbonTrail.RibbonColourMode> { LabelText = "Colour mode", Current = current });
        }

        private void blending()
        {
            var start = (string?)Definition.Settings["Blending"] ?? "Inherit";
            var current = new Bindable<string>(start);
            Definition.Settings["Blending"] = start;
            current.BindValueChanged(v => set("Blending", v.NewValue));
            flow.Add(new SettingsDropdown<string> { LabelText = "Blending", Items = new[] { "Inherit", "Additive", "Mixture" }, Current = current });
        }

        private void shape(string label, string key, string defaultVal)
        {
            var start = (string?)Definition.Settings[key] ?? defaultVal;
            var current = new Bindable<string>(start);
            Definition.Settings[key] = start;
            current.BindValueChanged(v => set(key, v.NewValue));
            flow.Add(new SettingsDropdown<string> { LabelText = label, Items = CosmeticParticleShapes.Names.OrderBy(n => n).ToArray(), Current = current });
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
            bool start = Definition.Settings[key]?.Value<bool>() ?? defaultVal;
            var current = new BindableBool(start);
            Definition.Settings[key] = start;
            current.BindValueChanged(v => set(key, v.NewValue));
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
