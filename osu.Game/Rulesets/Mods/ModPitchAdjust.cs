// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;

namespace osu.Game.Rulesets.Mods
{
    /// <summary>
    /// A Torii-original Fun mod that shifts the song's pitch up or down without
    /// changing playback speed. Pure audio effect — does not touch hit-object
    /// timing, judgement windows, or anything that affects difficulty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trick: <see cref="AdjustableProperty.Frequency"/> changes the playback
    /// rate, which couples pitch + speed (this is what stable DT does — chipmunks).
    /// <see cref="AdjustableProperty.Tempo"/> uses BASS_FX time-stretching to
    /// change speed while preserving pitch. Setting Frequency = N AND Tempo = 1/N
    /// composes to "pitch shifted by N, speed unchanged".
    /// </para>
    /// <para>
    /// This mod is intentionally listed as ranked (<see cref="Mod.Ranked"/> defaults
    /// to true and we don't override) and carries a 1.0× score multiplier — pitch
    /// shift in isolation is not a difficulty modifier. The server-side
    /// <c>mods_can_get_pp</c> check in g0v0-server permits any pitch_shift value,
    /// matching this client-side stance.
    /// </para>
    /// <para>
    /// Conflicts with every other mod that mutates Frequency or Tempo
    /// (<see cref="ModRateAdjust"/> family + <see cref="ModAdaptiveSpeed"/>).
    /// Stacking would have BASS_FX compose ambiguous adjustments.
    /// </para>
    /// </remarks>
    public class ModPitchAdjust : Mod, IApplicableToTrack
    {
        public override string Name => "Pitch Adjust";
        public override string Acronym => "PA";
        public override IconUsage? Icon => OsuIcon.ModPitchAdjust;
        public override ModType Type => ModType.Fun;
        public override LocalisableString Description =>
            "Shift the song's pitch up or down without changing playback speed.";

        public override double ScoreMultiplier => 1;

        // Stacking with anything else that touches Frequency / Tempo would have
        // BASS compose adjustments multiplicatively in ways the user-facing UI
        // can't represent honestly. Mark mutually exclusive with the rate-adjust
        // family (mirrored on the server in static/mods.json).
        public override Type[] IncompatibleMods => new[]
        {
            typeof(ModRateAdjust),
            typeof(ModAdaptiveSpeed),
        };

        [SettingSource(
            "Pitch shift",
            "Multiplier applied to pitch (1.0 = no change). 0.7 ≈ 5 semitones down, 1.4 ≈ 5 semitones up.",
            SettingControlType = typeof(MultiplierSettingsSlider))]
        public BindableNumber<double> PitchShift { get; } = new BindableDouble(1.0)
        {
            // Hard clamp so users can't push BASS_FX into ranges where the
            // time-stretch artifacts dominate the signal (anything outside
            // ~[0.5, 2.0] sounds like noise rather than music). The
            // server-side hush-hush threshold (1.8) sits comfortably inside
            // this range.
            MinValue = 0.5,
            MaxValue = 2.0,
            Default = 1.0,
            Precision = 0.01,
        };

        // Track-side adjustment bindables. Recomputed on every PitchShift
        // change so the live audio reacts mid-song without restarting the
        // track.
        private readonly BindableDouble freqAdjust = new BindableDouble(1);
        private readonly BindableDouble tempoAdjust = new BindableDouble(1);

        public ModPitchAdjust()
        {
            // Frequency = N shifts both pitch AND speed by N (sample-rate scaling).
            // Tempo = 1/N undoes the speed change via BASS_FX time-stretch while
            // preserving the pitch shift. Net: pitch ×N, speed unchanged.
            PitchShift.BindValueChanged(v =>
            {
                freqAdjust.Value = v.NewValue;
                tempoAdjust.Value = 1.0 / v.NewValue;
            }, true);
        }

        public void ApplyToTrack(IAdjustableAudioComponent track)
        {
            track.AddAdjustment(AdjustableProperty.Frequency, freqAdjust);
            track.AddAdjustment(AdjustableProperty.Tempo, tempoAdjust);
        }
    }
}
