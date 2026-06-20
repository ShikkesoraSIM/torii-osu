// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Overlays.Settings.Sections.Torii;
using osu.Game.Rulesets;

namespace osu.Game.Overlays.Settings.Sections
{
    /// <summary>
    /// Settings section grouping Torii-specific options. Subsections are added as
    /// features are ported over.
    /// </summary>
    public partial class ToriiSection : SettingsSection
    {
        public override LocalisableString Header => "Torii";

        // Same torii-gate glyph the client badge uses, rendered like every other
        // settings-section icon (plain monochrome, tinted by the settings panel).
        public override Drawable CreateIcon() => new SpriteIcon
        {
            Icon = FontAwesome.Solid.ToriiGate
        };

        [BackgroundDependencyLoader]
        private void load(RulesetStore rulesets)
        {
            Add(new ToriiSongSelectSettings());
            Add(new ToriiMenusSettings());
            Add(new ToriiGraphicsSettings());
            Add(new ToriiGameplaySettings());
            // key debounce anti-chatter. mismo binding que la copia en Settings -> Input (tocar una
            // actualiza la otra en vivo), espejada aca al lado de los tweaks de gameplay.
            Add(new Input.KeyDebounceSettings());
            Add(new ToriiAuraSettings());
            Add(new ToriiServerSettings());
            Add(new ToriiBriefingSettings());
            Add(new ToriiStorageSettings());

            // subsecciones torii por-ruleset: cada ruleset puede pedir mirrorear prefs relevantes aca
            // via Ruleset.CreateToriiSettingsSubsection(). los mismos toggles viven tambien en su
            // Settings -> Rulesets -> X nativo, con binding compartido asi quedan en sync.
            foreach (var rulesetInfo in rulesets.AvailableRulesets)
            {
                var sub = rulesetInfo.CreateInstance().CreateToriiSettingsSubsection();
                if (sub != null)
                    Add(sub);
            }

            // Android-only subsection (low-latency Oboe audio). Skipped entirely on
            // Desktop / iOS so the section header doesn't render there at all.
            if (RuntimeInfo.OS == RuntimeInfo.Platform.Android)
                Add(new ToriiAndroidSettings());
        }
    }
}
