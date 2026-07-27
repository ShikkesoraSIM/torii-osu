// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Osu.Mods
{
    /// <summary>
    /// torii: hitsounds driven by your KEYS instead of by the notes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Normally a hitsound only fires when a note is judged as hit
    /// (<see cref="DrawableHitObject"/> plays its samples on entering
    /// <c>ArmedState.Hit</c>). This mod moves the trigger to the input: every
    /// press makes a sound, hit or not, exactly like a taiko drum.
    /// </para>
    /// <para>
    /// Which sample plays is decided by WHERE you are in the map, never by
    /// whether you actually hit anything:
    /// <list type="bullet">
    /// <item>press lands close to a note in time, you get that note's samples,
    /// so the mapper's whistles and claps still come through.</item>
    /// <item>nothing nearby (a break, or mashing between patterns) you get the
    /// plain hitnormal of the sample set active at that point in the timeline.
    /// A drum section mashes like a drum, a soft section mashes soft.</item>
    /// </list>
    /// That second rule is the taiko part: don and kat are skin sounds that
    /// follow the section, they are not per-note. Falling back to the nearest
    /// note's samples instead would fire lone claps in the middle of a break,
    /// which sounds wrong for exactly that reason.
    /// </para>
    /// </remarks>
    public partial class OsuModHitSounder : Mod, IApplicableToDrawableRuleset<OsuHitObject>, IApplicableToBeatmap, IToriiExclusiveMod
    {
        public override string Name => "Hit Sounder";
        public override string Acronym => "HS";
        public override LocalisableString Description => "Hitsounds follow your fingers, not the notes. Mash freely.";
        public override ModType Type => ModType.Fun;
        public override IconUsage? Icon => OsuIcon.Metronome;

        public override Type[] IncompatibleMods => new[] { typeof(ModAutoplay), typeof(OsuModCinema) };

        [SettingSource("Sample set", "Which bank to mash with when there is no note nearby.")]
        public Bindable<HitSounderBank> Bank { get; } = new Bindable<HitSounderBank>(HitSounderBank.Auto);

        [SettingSource("Keep note hitsounds", "Off makes every press sound the same, ignoring what the mapper hitsounded.")]
        public BindableBool UseNoteSamples { get; } = new BindableBool(true);

        /// <summary>
        /// How close a press has to land to a note to borrow its samples. Wider
        /// than a hit window on purpose: this is about the press feeling like it
        /// belongs to that note, not about judging it.
        /// </summary>
        private const double borrow_window_ms = 200;

        private IBeatmap beatmap = null!;

        /// <summary>Object start times, sorted, so a press can binary-search the closest one.</summary>
        private double[] objectTimes = Array.Empty<double>();
        private IList<HitSampleInfo>[] objectSamples = Array.Empty<IList<HitSampleInfo>>();

        public void ApplyToBeatmap(IBeatmap b)
        {
            beatmap = b;

            // Nested objects (slider ticks, tails, spinner bonus) keep firing on
            // their own, so only top-level starts are candidates for borrowing.
            var ordered = b.HitObjects.OrderBy(h => h.StartTime).ToArray();
            objectTimes = ordered.Select(h => h.StartTime).ToArray();
            objectSamples = ordered.Select(h => h.Samples.ToArray()).Cast<IList<HitSampleInfo>>().ToArray();

            // Take the samples off the objects now that we hold a copy. Without
            // this a note you actually hit sounds twice, once from your key and
            // once from the note. Doing it here rather than on the drawable is
            // both simpler and the only option: DrawableHitObject.Samples is
            // protected. Nested samples are untouched, so slider ticks and tails
            // still play as the mapper wrote them.
            foreach (var hitObject in ordered)
                hitObject.Samples = Array.Empty<HitSampleInfo>();
        }

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            var osuRuleset = (DrawableOsuRuleset)drawableRuleset;
            osuRuleset.KeyBindingInputManager.Add(new HitSoundPlayer(this, drawableRuleset.FrameStableClock));
        }

        /// <summary>
        /// Sample selection is the whole mod, so it is worth asserting on
        /// directly rather than through a running playfield.
        /// </summary>
        internal ISampleInfo[] SamplesForTesting(double time) => SamplesFor(time) ?? Array.Empty<ISampleInfo>();

        /// <summary>
        /// Samples for a press at <paramref name="time"/>, or null if nothing should play.
        /// </summary>
        private ISampleInfo[]? SamplesFor(double time)
        {
            if (UseNoteSamples.Value)
            {
                var borrowed = nearestNoteSamples(time);
                if (borrowed != null)
                    return borrowed.Cast<ISampleInfo>().ToArray();
            }

            return new ISampleInfo[] { fallbackSample(time) };
        }

        private IList<HitSampleInfo>? nearestNoteSamples(double time)
        {
            if (objectTimes.Length == 0)
                return null;

            int i = Array.BinarySearch(objectTimes, time);
            if (i < 0)
                i = ~i;

            // ~i is the first object at or after `time`, so the closest is that
            // one or the one before it.
            int best = -1;
            double bestDistance = double.MaxValue;

            foreach (int candidate in new[] { i - 1, i })
            {
                if (candidate < 0 || candidate >= objectTimes.Length)
                    continue;

                double distance = Math.Abs(objectTimes[candidate] - time);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            if (best < 0 || bestDistance > borrow_window_ms)
                return null;

            var samples = objectSamples[best];
            return samples.Count > 0 ? samples : null;
        }

        /// <summary>
        /// The plain hitnormal of the bank in force at this point, which is what
        /// makes mashing track the section instead of being one flat tick.
        /// </summary>
        private HitSampleInfo fallbackSample(double time)
        {
            string bank = Bank.Value switch
            {
                HitSounderBank.Normal => HitSampleInfo.BANK_NORMAL,
                HitSounderBank.Soft => HitSampleInfo.BANK_SOFT,
                HitSounderBank.Drum => HitSampleInfo.BANK_DRUM,
                // SamplePointAt only exists on the legacy control point info, which is
                // what every .osu-sourced beatmap actually carries. Anything else has no
                // per-section banks to follow, so the plain default is correct there.
                _ => ((beatmap.ControlPointInfo as LegacyControlPointInfo)?.SamplePointAt(time)
                      ?? SampleControlPoint.DEFAULT).SampleBank,
            };

            return new HitSampleInfo(HitSampleInfo.HIT_NORMAL, bank);
        }

        /// <summary>
        /// Lives in the key binding input manager so it sees presses before
        /// gameplay does, and never blocks them: this mod adds sound, it does not
        /// change what reaches the playfield.
        /// </summary>
        private partial class HitSoundPlayer : CompositeDrawable, IKeyBindingHandler<OsuAction>
        {
            private readonly OsuModHitSounder mod;
            private readonly IFrameStableClock clock;

            /// <summary>
            /// Pool of one-shot players. A single SkinnableSound cannot overlap
            /// with itself, and streams absolutely will, so presses round-robin
            /// through several.
            /// </summary>
            private const int voices = 8;

            private readonly PausableSkinnableSound[] pool = new PausableSkinnableSound[voices];
            private int nextVoice;

            public HitSoundPlayer(OsuModHitSounder mod, IFrameStableClock clock)
            {
                this.mod = mod;
                this.clock = clock;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                for (int i = 0; i < voices; i++)
                    AddInternal(pool[i] = new PausableSkinnableSound());
            }

            public bool OnPressed(KeyBindingPressEvent<OsuAction> e)
            {
                switch (e.Action)
                {
                    case OsuAction.LeftButton:
                    case OsuAction.RightButton:
                        play();
                        break;
                }

                // never swallow the press.
                return false;
            }

            public void OnReleased(KeyBindingReleaseEvent<OsuAction> e)
            {
            }

            private void play()
            {
                // Rewinding replays would otherwise fire a burst of stale presses.
                if (clock.IsRewinding)
                    return;

                var samples = mod.SamplesFor(clock.CurrentTime);
                if (samples == null || samples.Length == 0)
                    return;

                var voice = pool[nextVoice];
                nextVoice = (nextVoice + 1) % voices;

                voice.Samples = samples;
                voice.Play();
            }
        }
    }

    public enum HitSounderBank
    {
        [System.ComponentModel.Description("Follow the map")]
        Auto,

        Normal,
        Soft,
        Drum,
    }
}
