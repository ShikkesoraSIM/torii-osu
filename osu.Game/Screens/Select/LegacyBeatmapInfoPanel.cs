// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// torii: el bloque de info del beatmap estilo osu!stable (arriba a la izquierda del song select legacy).
    /// replica el layout exacto del stable (SongSelection.cs:261-289): el icono de ranked-status en la
    /// esquina, el titulo pegado arriba de todo, despues mapper / length-bpm-objects / cantidad de circulos /
    /// stats de dificultad. las coords son las del stable (espacio de 480) x1.6 para que peguen con el espacio
    /// legacy de 1366x768.
    /// </summary>
    public partial class LegacyBeatmapInfoPanel : CompositeDrawable
    {
        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        private Sprite statusIcon = null!;
        private OsuSpriteText titleText = null!;
        private OsuSpriteText mapperText = null!;
        private OsuSpriteText lengthText = null!;
        private OsuSpriteText countsText = null!;
        private OsuSpriteText statsText = null!;

        private ISkinSource skin = null!;

        [BackgroundDependencyLoader]
        private void load(ISkinSource skinSource)
        {
            skin = skinSource;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                statusIcon = new Sprite
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = new Vector2(19, 19),
                    Size = new Vector2(28),
                },
                // el titulo va pegado arriba de todo, corrido para pasar el icono de status (stable 21,-3).
                // el stable solo pone en bold la linea de Length/BPM/Objects (details3.TextBold), todo lo
                // demas va con Aller light.
                titleText = line(new Vector2(34, -3), 28, FontWeight.Light),
                mapperText = line(new Vector2(37, 19), 18, FontWeight.Light),
                lengthText = line(new Vector2(2, 38), 18, FontWeight.Bold),
                countsText = line(new Vector2(2, 58), 18, FontWeight.Light),
                statsText = line(new Vector2(2, 78), 13, FontWeight.Light),
            };

            static OsuSpriteText line(Vector2 position, float size, FontWeight weight) => new OsuSpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = position,
                Font = LegacyFonts.Get(size, weight),
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
                // no esta el beatmap jugable, los conteos quedan en cero.
            }

            int lengthSeconds = (int)(info.Length / 1000);
            lengthText.Text = $"Length: {lengthSeconds / 60:00}:{lengthSeconds % 60:00}   BPM: {info.BPM:0}   Objects: {total}";
            countsText.Text = $"Circles: {circles}   Sliders: {sliders}   Spinners: {spinners}";

            var diff = info.Difficulty;
            statsText.Text = $"CS:{diff.CircleSize:0.##} AR:{diff.ApproachRate:0.##} OD:{diff.OverallDifficulty:0.##} HP:{diff.DrainRate:0.##}   Star Rating: {info.StarRating:0.0}★";

            statusIcon.Texture = skin.GetTexture(statusTextureName(info.BeatmapSet?.Status ?? info.Status));
        }

        private static string statusTextureName(BeatmapOnlineStatus status)
        {
            switch (status)
            {
                case BeatmapOnlineStatus.Ranked:
                case BeatmapOnlineStatus.Qualified:
                case BeatmapOnlineStatus.Loved:
                    return @"selection-ranked";

                case BeatmapOnlineStatus.Approved:
                    return @"selection-approved";

                default:
                    return @"selection-question";
            }
        }
    }
}
