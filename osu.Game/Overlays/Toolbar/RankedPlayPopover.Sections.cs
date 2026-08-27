// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Utils;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.Matchmaking;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    public partial class RankedPlayPopover
    {
        /// <summary>
        /// La fila de "cargando". Compacta a proposito: ocupa una linea, no un bloque.
        /// </summary>
        /// <remarks>
        /// Va DENTRO del flow de contenido y no como spinner encima porque el panel
        /// mide por auto-size: un spinner grande lo hace abrir alto y desinflarse de
        /// golpe cuando no encuentra nada, que se lee como si algo hubiera fallado. Con
        /// una linea, el panel abre chiquito y solo crece si hay algo que mostrar.
        /// </remarks>
        private partial class LoadingRow : CompositeDrawable
        {
            public LoadingRow()
            {
                RelativeSizeAxes = Axes.X;
                Height = 22;

                InternalChild = new LoadingSpinner
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(14),
                    State = { Value = Visibility.Visible },
                };
            }
        }

        /// <summary>
        /// Acepta clics SOLO fuera del panel, para cerrarlo.
        /// </summary>
        /// <remarks>
        /// Vive como hermano del panel y se dibuja detras. El truco esta en
        /// ReceivePositionalInputAt: devuelve true unicamente si el punto cae afuera,
        /// asi los clics de adentro lo atraviesan y llegan al panel normal.
        ///
        /// OnMouseDown devuelve false a proposito: el clic sigue viaje a lo que haya
        /// debajo, asi cerrar el panel y apretar otra cosa es un solo gesto. Es lo que
        /// hace el resto de lazer y lo que la gente espera.
        /// </remarks>
        private partial class OutsideClickCatcher : Drawable
        {
            private readonly RankedPlayPopover popover;
            private readonly Action onOutsideClick;

            public OutsideClickCatcher(RankedPlayPopover popover, Action onOutsideClick)
            {
                this.popover = popover;
                this.onOutsideClick = onOutsideClick;

                // Su propia caja mide cero; el hit-test se hace contra coordenadas de
                // pantalla, asi que hace falta estar presente igual.
                AlwaysPresent = true;
            }

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
            {
                if (popover.State.Value != Visibility.Visible)
                    return false;

                // Los clics de adentro caen al panel normal.
                if (popover.ScreenSpaceDrawQuad.Contains(screenSpacePos))
                    return false;

                // CLAVE: tampoco cazar el clic en la pildora que abrio el panel. Si no,
                // apretarla estando abierto hacia: el cazador cierra en mouse-down, el
                // clic sigue hasta la pildora, y esta ve el panel cerrado y lo abre de
                // nuevo. O sea que clickear el boton nunca lo cerraba, siempre parecia
                // reabrirse. Excluyendola, el clic llega con el panel todavia visible y
                // el toggle lo cierra como corresponde.
                if (popover.AnchoredAt?.ScreenSpaceDrawQuad.Contains(screenSpacePos) == true)
                    return false;

                return true;
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                onOutsideClick();
                return false;
            }
        }

        /// <summary>Titulito de seccion, en mayusculas chiquitas.</summary>
        private partial class SectionHeader : OsuSpriteText
        {
            public SectionHeader(string text)
            {
                Text = text.ToUpperInvariant();
                Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold);
                Colour = Color4.White.Opacity(0.5f);
                Margin = new MarginPadding { Bottom = 4 };
            }
        }

        private partial class EmptyNote : OsuSpriteText
        {
            public EmptyNote(string text)
            {
                Text = text;
                Font = OsuFont.GetFont(size: 12);
                Colour = Color4.White.Opacity(0.45f);
            }
        }

        /// <summary>Quien esta esperando.</summary>
        private partial class QueueSection : FillFlowContainer
        {
            private readonly APIUser[] users;
            private readonly GetComfortPicksBulkResponse? picks;

            public QueueSection(APIUser[] users, GetComfortPicksBulkResponse? picks)
            {
                this.users = users;
                this.picks = picks;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Direction = FillDirection.Vertical;
                Spacing = new Vector2(0, 4);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Add(new SectionHeader(users.Length == 1 ? @"1 in queue" : $@"{users.Length} in queue"));

                if (users.Length == 0)
                {
                    Add(new EmptyNote(@"Nobody waiting yet."));
                    return;
                }

                Add(new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Full,
                    Spacing = new Vector2(6, 4),
                    ChildrenEnumerable = users.Select(u => new NameChip(u.Username, u.Id, picks?.For(u.Id))),
                });
            }
        }

        /// <summary>Un nombre en una capsulita. Se puede clickear para ver el perfil.</summary>
        private partial class NameChip : OsuClickableContainer
        {
            [Resolved]
            private OsuGame? game { get; set; }

            private readonly int userId;

            public NameChip(string username, int userId, float? starRating)
            {
                this.userId = userId;

                AutoSizeAxes = Axes.Both;

                // Ver quien esta esperando sin poder mirar quien es sirve a medias:
                // el nombre solo no dice si le podes ganar.
                Action = () => game?.ShowUser(new APIUser { Id = userId });

                var contenido = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(5, 0),
                    Margin = new MarginPadding { Horizontal = 8, Vertical = 4 },
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                            Text = username,
                            Colour = Color4.White.Opacity(0.9f),
                        },
                    },
                };

                // La dificultad que eligio, en la misma capsula que el nombre. Es el dato
                // que decide si entrar: "7.5" se lee de un vistazo y ocupa menos que un
                // numero de mmr. Quien todavia no eligio va sin nada, en vez de con un
                // cero que se leeria como una eleccion.
                if (starRating is float sr)
                {
                    contenido.Add(new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(9),
                        Icon = FontAwesome.Solid.Star,
                        Colour = new Color4(255, 204, 34, 255),
                    });

                    contenido.Add(new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                        Text = sr.ToString(@"0.0"),
                        Colour = new Color4(255, 204, 34, 255),
                    });
                }

                Child = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 7,
                    CornerExponent = 2.4f,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.White.Opacity(0.08f),
                        },
                        contenido,
                    },
                };
            }
        }

        /// <summary>Las partidas que estan pasando.</summary>
        private partial class LiveMatchesSection : FillFlowContainer
        {
            private readonly RankedPlayLiveMatch[] matches;

            public LiveMatchesSection(RankedPlayLiveMatch[] matches)
            {
                this.matches = matches;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Direction = FillDirection.Vertical;
                Spacing = new Vector2(0, 8);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Add(new SectionHeader(matches.Length == 1 ? @"1 match ongoing" : $@"{matches.Length} matches ongoing"));

                // Tres como mucho: el panel cuelga del toolbar, si crece mas tapa media
                // pantalla. Lo que no entra se ve entrando a ranked play.
                foreach (var m in matches.Take(3))
                    Add(new MatchRow(m));

                if (matches.Length > 3)
                    Add(new EmptyNote($@"+{matches.Length - 3} more"));
            }
        }

        /// <summary>Una partida: los dos jugadores, su vida, y que estan jugando.</summary>
        private partial class MatchRow : CompositeDrawable
        {
            private readonly RankedPlayLiveMatch match;

            public MatchRow(RankedPlayLiveMatch match)
            {
                this.match = match;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var rows = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 5),
                    Padding = new MarginPadding(9),
                };

                foreach (var p in match.Players.Take(2))
                    rows.Add(new PlayerLine(p));

                // Que mapa y en que anda. Si todavia no eligieron, se dice eso en vez de
                // dejar el renglon vacio: "eligiendo mapa" tambien es informacion.
                string status = match.InGameplay
                    ? match.Beatmap ?? @"Playing"
                    : string.IsNullOrEmpty(match.Stage) ? @"In lobby" : match.Stage;

                rows.Add(new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Margin = new MarginPadding { Top = 2 },
                    Children = new Drawable[]
                    {
                        new TextFlowContainer(t =>
                        {
                            t.Font = OsuFont.GetFont(size: 10);
                            t.Colour = Color4.White.Opacity(0.45f);
                        })
                        {
                            // TextFlow y no SpriteText: los titulos de mapa son largos y
                            // en un panel de 320px un texto fijo se sale por el costado.
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Text = match.Round > 0 ? $"Round {match.Round}  ·  {status}" : status,
                        },
                    },
                });

                InternalChild = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = 9,
                    CornerExponent = 2.4f,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.White.Opacity(0.05f),
                        },
                        rows,
                    },
                };
            }
        }

        /// <summary>Un jugador con su barra de vida.</summary>
        private partial class PlayerLine : CompositeDrawable
        {
            private readonly RankedPlayLivePlayer player;

            public PlayerLine(RankedPlayLivePlayer player)
            {
                this.player = player;

                RelativeSizeAxes = Axes.X;
                Height = 16;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                // La vida se pinta de verde a rojo. Que el color cambie es lo que deja
                // ver de un vistazo quien esta por perder sin leer numeros.
                float life = player.LifeFraction;
                var barColour = Interpolation.ValueAt(life, new Color4(226, 74, 74, 255), live_green, 0f, 1f);

                InternalChildren = new Drawable[]
                {
                    // TruncatingSpriteText y no OsuSpriteText con Truncate: poner
                    // Truncate en un OsuSpriteText tira excepcion en el constructor
                    // ("Use TruncatingSpriteText instead") y se lleva puesto el panel
                    // entero, que fue exactamente el crash al abrirlo con una partida
                    // en curso.
                    new TruncatingSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                        // Si no se pudo resolver, el id crudo es mas honesto que un
                        // nombre inventado: se ve que es un id y no confunde.
                        Text = string.IsNullOrEmpty(player.Username) ? $"#{player.UserId}" : player.Username,
                        Colour = Color4.White.Opacity(0.9f),
                        RelativeSizeAxes = Axes.X,
                        Width = 0.42f,
                    },
                    new Container
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.54f,
                        Height = 7,
                        Masking = true,
                        CornerRadius = 3.5f,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = Color4.Black.Opacity(0.45f),
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = life,
                                Colour = barColour,
                            },
                        },
                    },
                };
            }
        }
    }
}
