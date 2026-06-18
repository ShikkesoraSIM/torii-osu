// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osuTK;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// Torii: the osu!stable-style beatmap information block shown in the top-left of the
    /// legacy song-select UI (replacing lazer's title/details wedges). Plain shadowed text:
    /// title, mapper, length/BPM/object count, circle/slider/spinner counts, and difficulty stats.
    /// </summary>
    public partial class LegacyBeatmapInfoPanel : CompositeDrawable
    {
        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        private OsuSpriteText titleText = null!;
        private OsuSpriteText mapperText = null!;
        private OsuSpriteText lengthText = null!;
        private OsuSpriteText countsText = null!;
        private OsuSpriteText statsText = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            AutoSizeAxes = Axes.Both;

            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    titleText = shadowed(OsuFont.GetFont(size: 28, weight: FontWeight.Bold)),
                    mapperText = shadowed(OsuFont.GetFont(size: 16)),
                    lengthText = shadowed(OsuFont.GetFont(size: 15)),
                    countsText = shadowed(OsuFont.GetFont(size: 15)),
                    statsText = shadowed(OsuFont.GetFont(size: 15)),
                },
            };

            static OsuSpriteText shadowed(FontUsage font) => new OsuSpriteText
            {
                Font = font,
                Shadow = true,
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            beatmap.BindValueChanged(_ => updateDisplay(), true);
        }

        private void updateDisplay()
        {
            var working = beatmap.Value;
            var info = working.BeatmapInfo;
            var metadata = info.Metadata;

            titleText.Text = $"{metadata.Artist} - {metadata.Title} [{info.DifficultyName}]";
            mapperText.Text = $"Mapped by {metadata.Author.Username}";

            int circles = 0, sliders = 0, spinners = 0, total = 0;

            try
            {
                var objects = working.Beatmap?.HitObjects;

                if (objects != null)
                {
                    foreach (var h in objects)
                    {
                        total++;
                        string name = h.GetType().Name;

                        if (name.Contains("Spinner") || name.Contains("Swell"))
                            spinners++;
                        else if (name.Contains("Slider") || name.Contains("Hold") || name.Contains("Drum") || name.Contains("Juice"))
                            sliders++;
                        else
                            circles++;
                    }
                }
            }
            catch
            {
                // playable beatmap not available; counts stay at zero.
            }

            int lengthSeconds = (int)(info.Length / 1000);
            lengthText.Text = $"Length: {lengthSeconds / 60:00}:{lengthSeconds % 60:00}   BPM: {info.BPM:0}   Objects: {total}";
            countsText.Text = $"Circles: {circles}   Sliders: {sliders}   Spinners: {spinners}";

            var diff = info.Difficulty;
            statsText.Text = $"CS:{diff.CircleSize:0.##} AR:{diff.ApproachRate:0.##} OD:{diff.OverallDifficulty:0.##} HP:{diff.DrainRate:0.##}   Star Rating: {info.StarRating:0.0}★";
        }
    }
}
