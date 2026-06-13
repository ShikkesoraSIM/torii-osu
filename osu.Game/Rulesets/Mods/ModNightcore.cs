// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Game.Audio;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Mods
{
    public abstract class ModNightcore : ModRateAdjust
    {
        public override string Name => "Nightcore";
        public override string Acronym => "NC";
        public override IconUsage? Icon => OsuIcon.ModNightcore;
        public override ModType Type => ModType.DifficultyIncrease;
        public override LocalisableString Description => "Uguuuuuuuu...";


        [SettingSource("Speed increase", "The actual increase to apply", SettingControlType = typeof(MultiplierSettingsSlider))]
        public override BindableNumber<double> SpeedChange { get; } = new BindableDouble(1.5)
        {
            MinValue = 1.01,
            MaxValue = 2,
            Precision = 0.01,
        };

        private readonly BindableNumber<double> tempoAdjust = new BindableDouble(1);
        private readonly BindableNumber<double> freqAdjust = new BindableDouble(1);

        private readonly RateAdjustModHelper rateAdjustHelper;

        protected ModNightcore()
        {
            rateAdjustHelper = new RateAdjustModHelper(SpeedChange);

            // intentionally not deferring the speed change handling to `RateAdjustModHelper`
            // as the expected result of operation is not the same (nightcore should preserve constant pitch).
            SpeedChange.BindValueChanged(val =>
            {
                freqAdjust.Value = SpeedChange.Default;
                tempoAdjust.Value = val.NewValue / SpeedChange.Default;
            }, true);
        }

        public override void ApplyToTrack(IAdjustableAudioComponent track)
        {
            track.AddAdjustment(AdjustableProperty.Frequency, freqAdjust);
            track.AddAdjustment(AdjustableProperty.Tempo, tempoAdjust);
        }

        public override double ScoreMultiplier => rateAdjustHelper.ScoreMultiplier;
    }

    public abstract partial class ModNightcore<TObject> : ModNightcore, IApplicableToDrawableRuleset<TObject>
        where TObject : HitObject
    {
        public void ApplyToDrawableRuleset(DrawableRuleset<TObject> drawableRuleset)
        {
            // Match upstream lazer: only play the off-beat hat samples when the
            // beatmap's tick rate is a multiple of two. On odd tick rates there
            // is no regular off-beat, so the hats just stand out as noise on the
            // red ticks - which is exactly the difference players reported.
            bool playHats = Precision.AlmostEquals(drawableRuleset.Beatmap.Difficulty.SliderTickRate % 2, 0);
            drawableRuleset.Overlays.Add(new NightcoreBeatContainer(playHats));
        }

        public partial class NightcoreBeatContainer : BeatSyncedContainer
        {
            private PausableSkinnableSound? hatSample;
            private PausableSkinnableSound? clapSample;
            private PausableSkinnableSound? kickSample;
            private PausableSkinnableSound? finishSample;

            private int? firstBeat;
            private int lastBeat = -1;

            private readonly bool playHats;

            public NightcoreBeatContainer(bool playHats = true)
            {
                this.playHats = playHats;
                Divisor = 2;
            }

            // Functional: plays the nightcore mod's added beat samples, must keep firing in Potato mode.
            protected override bool SuppressedByPotatoMode => false;

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    hatSample = new PausableSkinnableSound(new SampleInfo("Gameplay/nightcore-hat")),
                    clapSample = new PausableSkinnableSound(new SampleInfo("Gameplay/nightcore-clap")),
                    kickSample = new PausableSkinnableSound(new SampleInfo("Gameplay/nightcore-kick")),
                    finishSample = new PausableSkinnableSound(new SampleInfo("Gameplay/nightcore-finish")),
                };
            }

            private const int bars_per_segment = 4;

            protected override void OnNewBeat(int beatIndex, TimingControlPoint timingPoint, EffectControlPoint effectPoint, ChannelAmplitudes amplitudes)
            {
                base.OnNewBeat(beatIndex, timingPoint, effectPoint, amplitudes);

                int beatsPerBar = timingPoint.TimeSignature.Numerator;
                int segmentLength = beatsPerBar * Divisor * bars_per_segment;

                if (!IsBeatSyncedWithTrack)
                {
                    firstBeat = null;
                    return;
                }

                if (!firstBeat.HasValue || beatIndex < firstBeat)
                    // decide on a good starting beat index if once has not yet been decided.
                    firstBeat = beatIndex < 0 ? 0 : (beatIndex / segmentLength) * segmentLength;

                if (beatIndex >= firstBeat)
                    playBeatFor(beatIndex, segmentLength, timingPoint);
            }

            private void playBeatFor(int beatIndex, int segmentLength, TimingControlPoint timingPoint)
            {
                // https://github.com/peppy/osu-stable-reference/blob/6ab0cf1f9f7b3449f5c0d8defcd458aae72cdb88/osu!/Audio/NightcoreBeat.cs#L41
                if (lastBeat == beatIndex)
                    return;

                lastBeat = beatIndex;

                int beatInSegment = beatIndex % segmentLength;

                if (beatInSegment == 0)
                {
                    // https://github.com/peppy/osu-stable-reference/blob/6ab0cf1f9f7b3449f5c0d8defcd458aae72cdb88/osu!/Audio/NightcoreBeat.cs#L53
                    bool playFinish = beatIndex > 0 || !timingPoint.OmitFirstBarLine;

                    if (playFinish)
                        finishSample?.Play();
                }

                switch (timingPoint.TimeSignature.Numerator)
                {
                    case 3:
                        switch (beatInSegment % 6)
                        {
                            case 0:
                                kickSample?.Play();
                                break;

                            case 3:
                                clapSample?.Play();
                                break;

                            default:
                                if (playHats)
                                    hatSample?.Play();
                                break;
                        }

                        break;

                    case 4:
                        switch (beatInSegment % 4)
                        {
                            case 0:
                                kickSample?.Play();
                                break;

                            case 2:
                                clapSample?.Play();
                                break;

                            default:
                                // in stable, hat samples only play when the beatmap tick rate is even
                                // (https://github.com/peppy/osu-stable-reference/blob/6ab0cf1f9f7b3449f5c0d8defcd458aae72cdb88/osu!/Audio/NightcoreBeat.cs#L30-L32).
                                // reinstated to match lazer: on odd tick rates there is no regular off-beat,
                                // so playing a hat there just reads as noise on the red ticks.
                                if (playHats)
                                    hatSample?.Play();
                                break;
                        }

                        break;
                }
            }
        }
    }
}
