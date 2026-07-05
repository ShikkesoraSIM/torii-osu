// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Online.ScoreNotes
{
    /// <summary>
    /// iconito de "este score tiene una nota" para las tarjetas de leaderboard.
    /// arranca invisible; cuando el <see cref="ScoreNoteStore"/> confirma que el score
    /// tiene nota, aparece con un fade. hoverearlo muestra un mini briefing con el
    /// texto de la nota + la imagen (cargada async, nunca bloquea el juego).
    /// inerte si no hay store cacheado (test scenes) o si el score no es online.
    /// </summary>
    public partial class ScoreNoteIcon : CompositeDrawable, IHasCustomTooltip<APIScoreNote>
    {
        private readonly long scoreId;
        private APIScoreNote note;

        [Resolved(canBeNull: true)]
        private ScoreNoteStore store { get; set; }

        public ScoreNoteIcon(long scoreId)
        {
            this.scoreId = scoreId;
            Size = new Vector2(16);
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(20, 40, 70, 220),
                },
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = FontAwesome.Solid.CommentDots,
                    Size = new Vector2(9),
                    Colour = new Color4(140, 195, 255, 255),
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            store?.Lookup(scoreId, n =>
            {
                if (IsDisposed) return;

                note = n;
                this.FadeIn(250, Easing.OutQuint);
            });
        }

        public ITooltip<APIScoreNote> GetCustomTooltip() => new ScoreNoteTooltip();

        public APIScoreNote TooltipContent => note;

        /// <summary>mini briefing del tooltip: texto de la nota + imagen (async).</summary>
        public partial class ScoreNoteTooltip : VisibilityContainer, ITooltip<APIScoreNote>
        {
            [Resolved]
            private LargeTextureStore textures { get; set; }

            [Resolved]
            private IAPIProvider api { get; set; }

            private OsuSpriteText author;
            private OsuTextFlowContainer textFlow;
            private Container imageContainer;
            private Sprite image;
            private APIScoreNote lastNote;
            private CancellationTokenSource imageCancel;

            [BackgroundDependencyLoader]
            private void load()
            {
                Width = 240;
                AutoSizeAxes = Axes.Y;
                Masking = true;
                CornerRadius = BriefingTheme.CornerMd;
                BorderThickness = 1;
                BorderColour = Color4.White.Opacity(0.12f);

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(18, 20, 34, 246),
                    },
                    // flow vertical: TODOS los hijos Top-Y (regla fillflow).
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                        Padding = new MarginPadding(BriefingTheme.SpacingMd),
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(BriefingTheme.SpacingXs + 2, 0),
                                Children = new Drawable[]
                                {
                                    new SpriteIcon
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Icon = FontAwesome.Solid.CommentDots,
                                        Size = new Vector2(BriefingTheme.TypeCaption),
                                        Colour = new Color4(140, 195, 255, 255),
                                    },
                                    author = new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                                        Colour = new Color4(140, 195, 255, 255),
                                    },
                                },
                            },
                            textFlow = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody))
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                            },
                            imageContainer = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 120,
                                Masking = true,
                                CornerRadius = BriefingTheme.CornerSm,
                                Alpha = 0,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = Color4.Black.Opacity(0.3f),
                                    },
                                    image = new Sprite
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        FillMode = FillMode.Fit,
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Alpha = 0,
                                    },
                                },
                            },
                        },
                    },
                };
            }

            public void SetContent(APIScoreNote content)
            {
                if (content == null || (lastNote != null && lastNote.ScoreId == content.ScoreId))
                    return;

                lastNote = content;
                author.Text = $"NOTE · {content.Username}";
                textFlow.Text = content.Text;

                imageCancel?.Cancel();
                imageCancel = new CancellationTokenSource();

                if (!content.HasImage)
                {
                    imageContainer.Alpha = 0;
                    image.Texture = null;
                    return;
                }

                imageContainer.Alpha = 1;
                image.Alpha = 0;

                long targetScore = content.ScoreId;
                var token = imageCancel.Token;

                // carga ASYNC (nunca en el update thread) via el proxy del server; si el
                // tooltip cambio de nota mientras bajaba, el guard la descarta.
                textures.GetAsync(content.GetImageUrl(api), token).ContinueWith(t => Schedule(() =>
                {
                    if (lastNote?.ScoreId != targetScore || token.IsCancellationRequested)
                        return;

                    var tex = t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? t.GetResultSafely() : null;
                    image.Texture = tex;
                    image.FadeTo(tex != null ? 1 : 0, 150);
                    if (tex == null)
                        imageContainer.Alpha = 0;
                }));
            }

            protected override void PopIn() => this.FadeIn(200, Easing.OutQuint);
            protected override void PopOut() => this.FadeOut(150, Easing.OutQuint);
            public void Move(Vector2 pos) => Position = pos;
        }
    }
}
