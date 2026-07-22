// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Overlays;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.Queue
{
    /// <summary>
    /// torii: fila compacta de una partida reciente en la cola. Rediseñada: se fue el cover-art
    /// grande y ruidoso; ahora es una fila limpia con los dos jugadores, el marcador de rondas al
    /// medio, el ganador resaltado y el perdedor apagado, mas una franja de resultado abajo.
    /// </summary>
    public partial class RankedPlayMatchPanel : CompositeDrawable
    {
        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private UserLookupCache userLookupCache { get; set; } = null!;

        private readonly RankedPlayRoomState state;

        public RankedPlayMatchPanel(RankedPlayRoomState state)
        {
            this.state = state;

            Height = 60;
            AutoSizeAxes = Axes.None;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 10;

            (int UserId, RankedPlayUserInfo Info)[] users = state.Users.Select(kvp => (kvp.Key, kvp.Value)).ToArray();
            Task<APIUser?> leftLookup = userLookupCache.GetUserAsync(users[0].UserId);
            Task<APIUser?> rightLookup = userLookupCache.GetUserAsync(users[1].UserId);
            Task.WhenAll(leftLookup, rightLookup).WaitSafely();

            APIUser left = leftLookup.GetResultSafely() ?? new APIUser { Username = "Unknown" };
            APIUser right = rightLookup.GetResultSafely() ?? new APIUser { Username = "Unknown" };

            RankedPlayUserInfo leftInfo = users[0].Info;
            RankedPlayUserInfo rightInfo = users[1].Info;

            bool leftWin = leftInfo.Life > rightInfo.Life;
            bool rightWin = rightInfo.Life > leftInfo.Life;

            Color4 leftResult = leftWin ? colours.Green : rightWin ? colours.Red : colours.Yellow;
            Color4 rightResult = rightWin ? colours.Green : leftWin ? colours.Red : colours.Yellow;

            Color4 winnerName = Color4.White;
            Color4 loserName = new Color4(0.62f, 0.62f, 0.68f, 1f);

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background4,
                },
                // tinte suave del lado de cada jugador segun su resultado.
                new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.5f,
                    Colour = ColourInfo.GradientHorizontal(leftResult.Opacity(0.16f), leftResult.Opacity(0f)),
                },
                new Box
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.5f,
                    Colour = ColourInfo.GradientHorizontal(rightResult.Opacity(0f), rightResult.Opacity(0.16f)),
                },
                // avatares.
                avatar(left, Anchor.CentreLeft, new MarginPadding { Left = 10 }),
                avatar(right, Anchor.CentreRight, new MarginPadding { Right = 10 }),
                // nombre + vida de cada lado.
                sideInfo(left.Username, leftInfo, leftWin, Anchor.CentreLeft, new MarginPadding { Left = 54 }, winnerName, loserName),
                sideInfo(right.Username, rightInfo, rightWin, Anchor.CentreRight, new MarginPadding { Right = 54 }, winnerName, loserName),
                // marcador de rondas al medio.
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6, 0),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = leftInfo.RoundsWon.ToString(),
                            Font = OsuFont.Torus.With(size: 24, weight: FontWeight.Bold),
                            Colour = leftWin ? colours.Green : Color4.White,
                            UseFullGlyphHeight = false,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = "-",
                            Font = OsuFont.Torus.With(size: 20, weight: FontWeight.Regular),
                            Colour = new Color4(0.5f, 0.5f, 0.56f, 1f),
                            UseFullGlyphHeight = false,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = rightInfo.RoundsWon.ToString(),
                            Font = OsuFont.Torus.With(size: 24, weight: FontWeight.Bold),
                            Colour = rightWin ? colours.Green : Color4.White,
                            UseFullGlyphHeight = false,
                        },
                    }
                },
                // franja de resultado abajo (mitad y mitad).
                new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Width = 0.5f,
                    Height = 3,
                    Colour = leftResult,
                },
                new Box
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    RelativeSizeAxes = Axes.X,
                    Width = 0.5f,
                    Height = 3,
                    Colour = rightResult,
                },
            };
        }

        private Drawable avatar(APIUser user, Anchor anchor, MarginPadding margin) => new CircularContainer
        {
            Anchor = anchor,
            Origin = anchor,
            Size = new Vector2(36),
            Masking = true,
            Margin = margin,
            Child = new UpdateableAvatar(user)
            {
                RelativeSizeAxes = Axes.Both,
            }
        };

        private Drawable sideInfo(string username, RankedPlayUserInfo info, bool win, Anchor anchor, MarginPadding margin, Color4 winnerName, Color4 loserName)
        {
            bool right = (anchor & Anchor.x2) != 0;
            Anchor textAnchor = right ? Anchor.TopRight : Anchor.TopLeft;

            return new FillFlowContainer
            {
                Anchor = anchor,
                Origin = anchor,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Margin = margin,
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Anchor = textAnchor,
                        Origin = textAnchor,
                        Text = username,
                        Font = OsuFont.GetFont(size: 15, weight: FontWeight.SemiBold),
                        Colour = win ? winnerName : loserName,
                        UseFullGlyphHeight = false,
                    },
                    new FillFlowContainer
                    {
                        Anchor = textAnchor,
                        Origin = textAnchor,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(3, 0),
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = FontAwesome.Solid.Heart,
                                Size = new Vector2(9),
                                Colour = colours.Red.Opacity(win ? 1f : 0.6f),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = info.Life.ToString("N0"),
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                Colour = win ? new Color4(0.8f, 0.8f, 0.85f, 1f) : loserName,
                                UseFullGlyphHeight = false,
                            },
                        }
                    }
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            this.FadeInFromZero(500, Easing.OutQuint);
        }
    }
}
