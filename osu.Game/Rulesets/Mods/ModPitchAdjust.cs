// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Online.API;
using osu.Game.Overlays.Settings;
using osu.Game.Utils;

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
    /// Can stack with fixed rate mods. Adaptive speed stays blocked because it
    /// changes rate continuously while PA is trying to keep one pitch offset.
    /// </para>
    /// </remarks>
    public class ModPitchAdjust : Mod, IApplicableToTrack, IToriiExclusiveMod
    {
        public override string Name => "Pitch Adjust";
        public override string Acronym => "PA";
        public override IconUsage? Icon => OsuIcon.ModPitchAdjust;
        public override ModType Type => ModType.Fun;
        public override LocalisableString Description =>
            "Shift the song's pitch up or down without changing playback speed.";

        public override double ScoreMultiplier => 1;

        public override Type[] IncompatibleMods => new[]
        {
            typeof(ModAdaptiveSpeed),
        };

        // Two operating ranges for the pitch slider, gated by the
        // ExtendedLimits toggle below. The "safe" range is what you actually
        // want to use — BASS_FX time-stretching beyond ±1 octave starts
        // gargling the audio into noise. The "extended" range is for
        // novelty / hush-hush territory: anything below ~0.3× sounds like
        // an eldritch warble, anything above ~2.5× is full chipmunk.
        private const double safe_min = 0.5;
        private const double safe_max = 2.0;
        private const double extended_min = 0.1;
        private const double extended_max = 3.0;

        [SettingSource(
            "Pitch shift",
            "Multiplier applied to pitch (1.0 = no change). 0.7 ≈ 5 semitones down, 1.4 ≈ 5 semitones up.",
            SettingControlType = typeof(MultiplierSettingsSlider))]
        public BindableNumber<double> PitchShift { get; } = new BindableDouble(1.0)
        {
            // Initialised to the SAFE range. The ExtendedLimits handler
            // below widens / narrows these on toggle. We don't initialise
            // straight to the extended range so users who never touch the
            // extended-limits checkbox can't accidentally land on a 0.1×
            // value just by dragging the slider all the way down.
            MinValue = safe_min,
            MaxValue = safe_max,
            Default = 1.0,
            Precision = 0.01,
        };

        [SettingSource(
            "Extended limits",
            "Allow extreme pitch shifts (0.1× to 3.0× instead of the safe 0.5× to 2.0× range). " +
            "Outside the safe band the BASS time-stretcher produces heavy artifacting — the audio " +
            "stops resembling music and starts to sound like a chipmunk choir or an eldritch warble.")]
        public BindableBool ExtendedLimits { get; } = new BindableBool();

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

            // Widen / narrow the slider range when the extended-limits
            // toggle flips. Order matters here:
            //
            //   - Widening (off → on): set MaxValue first (3.0), then
            //     MinValue (0.1). Either order works because we're only
            //     loosening the bounds, but we keep the convention so the
            //     narrowing branch stays readable next to it.
            //
            //   - Narrowing (on → off): set MaxValue first (2.0). If the
            //     current PitchShift value was above 2.0, the BindableNumber
            //     auto-clamps it down to 2.0. Then set MinValue (0.5) which
            //     will auto-clamp upward if the value was below 0.5.
            //     Setting MinValue first when MaxValue is still 3.0 would
            //     work too, but the framework throws if MinValue > MaxValue
            //     transiently — keeping Max-then-Min is the universally
            //     safe order.
            //
            // BindValueChanged with `true` fires immediately with the current
            // (default) value of false, which sets the safe range that
            // matches what we already initialised PitchShift with — so the
            // first call is a no-op effect-wise.
            ExtendedLimits.BindValueChanged(v =>
            {
                if (v.NewValue)
                {
                    PitchShift.MaxValue = extended_max;
                    PitchShift.MinValue = extended_min;
                }
                else
                {
                    PitchShift.MaxValue = safe_max;
                    PitchShift.MinValue = safe_min;
                }
            }, true);
        }

        public void ApplyToTrack(IAdjustableAudioComponent track)
        {
            track.AddAdjustment(AdjustableProperty.Frequency, freqAdjust);
            track.AddAdjustment(AdjustableProperty.Tempo, tempoAdjust);
        }

        /// <summary>
        /// Override the per-setting copy hook so the deserialisation order doesn't
        /// silently clamp extended-range pitch values back into the safe band.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="APIMod.ToMod"/> iterates settings in property-declaration
        /// order. <see cref="PitchShift"/> is declared above <see cref="ExtendedLimits"/>,
        /// so when a replay carries <c>{pitch_shift: 3.0, extended_limits: true}</c>
        /// the framework sets PitchShift first — and because the bindable still
        /// has its default <c>MaxValue</c> of 2.0 at that point, BindableDouble
        /// auto-clamps the incoming 3.0 down to 2.0. Then ExtendedLimits widens
        /// the range, but the value's already been corrupted: the leaderboard
        /// shows 2× and the replay plays at 2× even though the player heard 3×
        /// at submission time.
        /// </para>
        /// <para>
        /// Fix: when an out-of-safe-range value is being copied into PitchShift,
        /// pre-flip ExtendedLimits BEFORE delegating to the base. That widens
        /// the bindable's bounds first so the subsequent assignment doesn't clamp.
        /// Mirrors the explicit guard <see cref="DifficultyBindable.Value"/>
        /// applies for the Difficulty Adjust mod, which solved the same class
        /// of bug in upstream lazer.
        /// </para>
        /// </remarks>
        internal override void CopyAdjustedSetting(IBindable target, object source)
        {
            if (ReferenceEquals(target, PitchShift))
            {
                double? incoming = tryExtractDoubleValue(source);
                if (incoming.HasValue && (incoming.Value > safe_max || incoming.Value < safe_min))
                    ExtendedLimits.Value = true;
            }

            base.CopyAdjustedSetting(target, source);
        }

        /// <summary>
        /// Best-effort coercion of a CopyAdjustedSetting source (which can be a
        /// raw boxed numeric, a string from JSON parsing, or another IBindable
        /// when the framework is binding two settings together) into a double we
        /// can compare against the safe / extended thresholds.
        /// </summary>
        private static double? tryExtractDoubleValue(object source)
        {
            if (source is IBindable b)
            {
                object? underlying = BindableValueAccessor.GetValue(b);
                source = underlying ?? source;
            }

            return source switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                decimal m => (double)m,
                string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) => parsed,
                _ => null,
            };
        }
    }
}
