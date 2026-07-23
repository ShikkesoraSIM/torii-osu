// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Extensions;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osuTK;
using osuTK.Input;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay.Components
{
    /// <summary>
    /// torii DIBUJITO: pizarra compartida del lobby de ranked play. Un lapicito al costado abre la
    /// paleta (colores + grosores + basurita); con el pincel agarrado se dibuja a mano alzada sobre
    /// el espacio virtual compartido y el rival lo ve EN VIVO (chunks de puntos normalizados por el
    /// mismo canal raw que el ghost cursor, sin tipos nuevos en el protocolo).
    ///
    /// Los dibujos viven en memoria toda la partida: cuando el centro de la pantalla se ocupa
    /// (reveal de cartas, warmup, resultados) la capa se esfuma sola y vuelve intacta en la
    /// proxima fase con espacio libre. Caps por todos lados (puntos por trazo, trazos por usuario,
    /// tamanio de chunk) para que ni un cliente modificado ni un artista MUY inspirado la rompan.
    /// </summary>
    public partial class RankedPlayDrawingLayer : CompositeDrawable
    {
        /// <summary>Paleta compartida entre clientes — por la red viaja solo el INDICE.</summary>
        public static readonly Colour4[] PALETTE =
        {
            Colour4.White,
            new Colour4(255, 112, 180, 255), // rosa
            new Colour4(255, 92, 92, 255), // rojo
            new Colour4(255, 176, 66, 255), // naranja
            new Colour4(252, 233, 89, 255), // amarillo
            new Colour4(116, 226, 128, 255), // verde
            new Colour4(96, 199, 255, 255), // celeste
            new Colour4(196, 130, 255, 255), // violeta
        };

        /// <summary>Diametros de pincel en unidades del espacio virtual.</summary>
        public static readonly float[] BRUSH_SIZES = { 3.5f, 7f, 12f };

        private const int max_points_per_stroke = 400;
        private const int max_strokes_per_user = 50;
        private const int chunk_points_max = 32;
        private const double chunk_interval_ms = 100;
        private const float min_point_distance = 3.5f;

        [Resolved]
        private MultiplayerClient client { get; set; } = null!;

        [Resolved]
        private RankedPlayMatchInfo matchInfo { get; set; } = null!;

        private IBindable<RankedPlayStage> stage = null!;

        private readonly BindableBool penActive = new BindableBool();
        private readonly Bindable<int> selectedColour = new Bindable<int>(6); // celeste de arranque
        private readonly Bindable<int> selectedSize = new Bindable<int>(1);

        private readonly Dictionary<(int userId, int strokeId), DrawingStroke> strokes = new Dictionary<(int, int), DrawingStroke>();
        private readonly Dictionary<int, List<DrawingStroke>> strokesByUser = new Dictionary<int, List<DrawingStroke>>();

        private Container fadeContent = null!;
        private Container strokesContainer = null!;
        private DrawingCanvas canvas = null!;

        // trazo local en curso: el color/grosor se capturan al APOYAR el pincel, no al soltar.
        private DrawingStroke? activeStroke;
        private int activeStrokeId = -1;
        private int activeColourIndex;
        private float activeThickness;
        private int nextStrokeId;

        private readonly List<Vector2> pendingPoints = new List<Vector2>();
        private Vector2 lastPointVirtual;
        private Vector2 lastPointNormalised;
        private double lastChunkTime;

        private bool centreFree;
        private bool shown;

        private bool active = true;

        /// <summary>Mientras este apagado (ej: gameplay encima) la capa se esconde entera.</summary>
        public bool Active
        {
            get => active;
            set
            {
                active = value;

                if (IsLoaded)
                    updateVisibility();
            }
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = fadeContent = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
                Children = new Drawable[]
                {
                    strokesContainer = new Container { RelativeSizeAxes = Axes.Both },
                    canvas = new DrawingCanvas
                    {
                        RelativeSizeAxes = Axes.Both,
                        CanDraw = () => shown && penActive.Value,
                        StrokeStarted = beginStroke,
                        StrokeContinued = continueStroke,
                        StrokeEnded = endActiveStroke,
                        DotRequested = drawDot,
                        PenDismissRequested = () => penActive.Value = false,
                    },
                    new DrawingToolbox(penActive, selectedColour, selectedSize, clearOwnStrokes)
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Margin = new MarginPadding { Right = 10 },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // llegan en un thread de SignalR — SIEMPRE agendar al update thread.
            client.RankedPlayDrawStrokeReceived += onRemoteStroke;
            client.RankedPlayDrawClearReceived += onRemoteClear;

            stage = matchInfo.Stage.GetBoundCopy();
            stage.BindValueChanged(s =>
            {
                // el centro esta libre en las fases de mano (descartar/elegir carta) — en reveal,
                // warmup y resultados el medio se usa, asi que la pizarra se corre sola.
                centreFree = s.NewValue is RankedPlayStage.CardDiscard or RankedPlayStage.CardPlay;
                updateVisibility();
            }, true);

            penActive.BindValueChanged(pen =>
            {
                if (!pen.NewValue)
                    endActiveStroke();
            });
        }

        protected override void Update()
        {
            base.Update();

            // flush del trazo en curso: cada ~100ms o cuando el chunk se llena, lo que pase primero.
            if (activeStroke != null && pendingPoints.Count > 0
                && (Time.Current - lastChunkTime >= chunk_interval_ms || pendingPoints.Count >= chunk_points_max))
            {
                flushPendingChunk(false);
            }
        }

        private void updateVisibility()
        {
            bool visible = active && centreFree;

            if (visible == shown)
                return;

            shown = visible;

            if (visible)
                fadeContent.FadeIn(300, Easing.OutQuint);
            else
            {
                // pincel suelto al esconder: que no quede agarrado tapando los clicks de las cartas.
                endActiveStroke();
                penActive.Value = false;
                fadeContent.FadeOut(250, Easing.OutQuint);
            }
        }

        #region trazo local

        private void beginStroke(Vector2 position)
        {
            endActiveStroke();

            if (!shown || !penActive.Value)
                return;

            activeStrokeId = nextStrokeId++;
            activeColourIndex = selectedColour.Value;
            activeThickness = BRUSH_SIZES[selectedSize.Value];
            activeStroke = createStroke(localUserId, activeStrokeId, activeColourIndex, activeThickness);
            lastChunkTime = Time.Current;

            addPoint(position, force: true);
        }

        private void continueStroke(Vector2 position) => addPoint(position, force: false);

        private void drawDot(Vector2 position)
        {
            beginStroke(position);

            if (activeStroke == null)
                return;

            // un path necesita dos vertices para renderizar: el puntito es un trazo minusculo.
            addPoint(position + new Vector2(0.75f), force: true);
            endActiveStroke();
        }

        private void addPoint(Vector2 position, bool force)
        {
            if (activeStroke == null)
                return;

            if (!force && Vector2.Distance(position, lastPointVirtual) < min_point_distance)
                return;

            activeStroke.AddPoint(position);
            lastPointVirtual = position;
            lastPointNormalised = Vector2.Divide(position, DrawSize);
            pendingPoints.Add(lastPointNormalised);

            // trazo largo: al llegar al cap NO se corta el gesto — se cierra este trazo y se
            // encadena uno nuevo desde el mismo punto. invisible para el que dibuja (antes el
            // dibujo se "moria" a los ~2 segundos de garabato continuo) y continuo para el rival.
            if (activeStroke.PointCount >= max_points_per_stroke)
                rolloverStroke(position);
        }

        private void rolloverStroke(Vector2 position)
        {
            int colourIndex = activeColourIndex;
            float thickness = activeThickness;

            endActiveStroke();

            activeStrokeId = nextStrokeId++;
            activeColourIndex = colourIndex;
            activeThickness = thickness;
            activeStroke = createStroke(localUserId, activeStrokeId, colourIndex, thickness);
            lastChunkTime = Time.Current;

            // el tramo nuevo arranca EXACTO donde termino el anterior: costura invisible.
            addPoint(position, force: true);
        }

        private void endActiveStroke()
        {
            if (activeStroke == null)
                return;

            flushPendingChunk(true);

            activeStroke = null;
            activeStrokeId = -1;
        }

        private void flushPendingChunk(bool done)
        {
            if (activeStrokeId < 0)
                return;

            if (pendingPoints.Count == 0)
            {
                if (!done)
                    return;

                // chunk final vacio: repetimos el ultimo punto solo para transportar el "done".
                pendingPoints.Add(lastPointNormalised);
            }

            client.SendRankedPlayDrawStroke(activeStrokeId, pendingPoints.ToArray(), activeColourIndex, activeThickness, done).FireAndForget();
            pendingPoints.Clear();
            lastChunkTime = Time.Current;
        }

        private void clearOwnStrokes()
        {
            endActiveStroke();
            clearUserStrokes(localUserId);
            client.SendRankedPlayDrawClear().FireAndForget();
        }

        private int localUserId => client.LocalUser?.UserID ?? 0;

        #endregion

        #region trazos remotos

        private void onRemoteStroke(int userId, int strokeId, Vector2[] points, int colourIndex, float thickness, bool done) => Scheduler.Add(() =>
        {
            if (userId == localUserId)
                return;

            if (!strokes.TryGetValue((userId, strokeId), out var stroke))
            {
                // entrada remota = entrada hostil hasta que se demuestre lo contrario: todo clampeado.
                colourIndex = Math.Clamp(colourIndex, 0, PALETTE.Length - 1);
                thickness = Math.Clamp(thickness, 1f, 24f);
                stroke = createStroke(userId, strokeId, colourIndex, thickness);
            }

            foreach (var p in points)
            {
                var clamped = new Vector2(Math.Clamp(p.X, -0.05f, 1.05f), Math.Clamp(p.Y, -0.05f, 1.05f));
                stroke.AddPoint(clamped * DrawSize);
            }
        });

        private void onRemoteClear(int userId) => Scheduler.Add(() =>
        {
            if (userId != localUserId)
                clearUserStrokes(userId);
        });

        #endregion

        private DrawingStroke createStroke(int userId, int strokeId, int colourIndex, float thickness)
        {
            // alpha 1: con transparencia, la costura entre trazos encadenados (y el punto donde un
            // trazo pisa a otro) se veria como una manchita mas oscura.
            var stroke = new DrawingStroke(userId, strokeId)
            {
                PathRadius = thickness / 2,
                Colour = PALETTE[colourIndex],
            };

            strokes[(userId, strokeId)] = stroke;

            if (!strokesByUser.TryGetValue(userId, out var userStrokes))
                strokesByUser[userId] = userStrokes = new List<DrawingStroke>();

            userStrokes.Add(stroke);

            // cap por usuario: el trazo mas viejo se va (mismo orden en ambos clientes).
            while (userStrokes.Count > max_strokes_per_user)
            {
                var oldest = userStrokes[0];
                userStrokes.RemoveAt(0);
                strokes.Remove((oldest.UserId, oldest.StrokeId));
                oldest.FadeOut(200, Easing.OutQuint).Expire();
            }

            strokesContainer.Add(stroke);
            return stroke;
        }

        private void clearUserStrokes(int userId)
        {
            if (!strokesByUser.TryGetValue(userId, out var userStrokes))
                return;

            foreach (var stroke in userStrokes)
            {
                strokes.Remove((stroke.UserId, stroke.StrokeId));
                stroke.FadeOut(250, Easing.OutQuint).Expire();
            }

            userStrokes.Clear();
        }

        protected override void Dispose(bool isDisposing)
        {
            if (client.IsNotNull())
            {
                client.RankedPlayDrawStrokeReceived -= onRemoteStroke;
                client.RankedPlayDrawClearReceived -= onRemoteClear;
            }

            base.Dispose(isDisposing);
        }

        /// <summary>
        /// Un trazo: un <see cref="SmoothPath"/> con vertices en coordenadas absolutas de la capa,
        /// re-anclado tras cada vertice (el bounding box del path se recalcula al crecer y sin esto
        /// el dibujo entero se corre — mismo truco que el ribbon trail de cosmetics).
        /// </summary>
        private partial class DrawingStroke : SmoothPath
        {
            public readonly int UserId;
            public readonly int StrokeId;

            public int PointCount => Vertices.Count;

            public DrawingStroke(int userId, int strokeId)
            {
                UserId = userId;
                StrokeId = strokeId;
            }

            public void AddPoint(Vector2 position)
            {
                if (Vertices.Count >= max_points_per_stroke)
                    return;

                AddVertex(position);
                Position = -PositionInBoundingBox(Vector2.Zero);
            }
        }

        /// <summary>
        /// Superficie de dibujo: transparente al input salvo con el pincel agarrado, en cuyo caso
        /// captura el mouse (las cartas de abajo no reciben nada). Click derecho suelta el pincel.
        /// </summary>
        private partial class DrawingCanvas : Drawable
        {
            public Func<bool> CanDraw = () => false;
            public Action<Vector2>? StrokeStarted;
            public Action<Vector2>? StrokeContinued;
            public Action? StrokeEnded;
            public Action<Vector2>? DotRequested;
            public Action? PenDismissRequested;

            public override bool HandlePositionalInput => CanDraw();

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (e.Button == MouseButton.Right)
                {
                    PenDismissRequested?.Invoke();
                    return true;
                }

                return e.Button == MouseButton.Left;
            }

            protected override bool OnClick(ClickEvent e)
            {
                if (e.Button != MouseButton.Left)
                    return false;

                DotRequested?.Invoke(e.MousePosition);
                return true;
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                if (e.Button != MouseButton.Left)
                    return false;

                StrokeStarted?.Invoke(e.MouseDownPosition);
                StrokeContinued?.Invoke(e.MousePosition);
                return true;
            }

            protected override void OnDrag(DragEvent e) => StrokeContinued?.Invoke(e.MousePosition);

            protected override void OnDragEnd(DragEndEvent e) => StrokeEnded?.Invoke();

            // en modo dibujo tampoco dejamos pasar el hover: las cartas no reaccionan bajo el pincel.
            protected override bool OnHover(HoverEvent e) => true;
        }

        /// <summary>
        /// El lapicito flotante: click y se agarra el pincel, abriendose la paleta (colores en
        /// grilla, tres grosores y la basurita para borrar lo propio). Todo iconitos, cero texto.
        /// </summary>
        private partial class DrawingToolbox : CompositeDrawable
        {
            private readonly BindableBool penActive;
            private readonly Bindable<int> selectedColour;
            private readonly Bindable<int> selectedSize;
            private readonly Action clearRequested;

            private IconButton pencilButton = null!;
            private FillFlowContainer expanded = null!;

            public DrawingToolbox(BindableBool penActive, Bindable<int> selectedColour, Bindable<int> selectedSize, Action clearRequested)
            {
                this.penActive = penActive;
                this.selectedColour = selectedColour;
                this.selectedSize = selectedSize;
                this.clearRequested = clearRequested;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                AutoSizeAxes = Axes.Both;

                InternalChild = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    AutoSizeDuration = 220,
                    AutoSizeEasing = Easing.OutQuint,
                    Masking = true,
                    CornerRadius = 14,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Colour4.Black.Opacity(0.55f),
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding(7),
                            Spacing = new Vector2(0, 7),
                            Children = new Drawable[]
                            {
                                pencilButton = new IconButton
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Icon = FontAwesome.Solid.PencilAlt,
                                    Action = () => penActive.Toggle(),
                                },
                                expanded = new FillFlowContainer
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 7),
                                    Alpha = 0,
                                    BypassAutoSizeAxes = Axes.Both,
                                    Children = new Drawable[]
                                    {
                                        createSwatchGrid(),
                                        createSizeRow(),
                                        new IconButton
                                        {
                                            Anchor = Anchor.TopCentre,
                                            Origin = Anchor.TopCentre,
                                            Icon = FontAwesome.Solid.TrashAlt,
                                            IconScale = new Vector2(0.8f),
                                            Action = clearRequested,
                                        },
                                    },
                                },
                            },
                        },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                penActive.BindValueChanged(pen =>
                {
                    if (pen.NewValue)
                    {
                        expanded.BypassAutoSizeAxes = Axes.None;
                        expanded.FadeIn(180, Easing.OutQuint);
                        // el lapiz se "inclina" al agarrarlo — detalle bobo pero rico.
                        pencilButton.RotateTo(-16, 260, Easing.OutBack);
                    }
                    else
                    {
                        expanded.BypassAutoSizeAxes = Axes.Both;
                        expanded.FadeOut(150, Easing.OutQuint);
                        pencilButton.RotateTo(0, 260, Easing.OutBack);
                    }

                    updatePencilColour();
                }, true);

                selectedColour.BindValueChanged(_ => updatePencilColour());
            }

            private void updatePencilColour()
                => pencilButton.IconColour = penActive.Value ? PALETTE[selectedColour.Value] : Colour4.White.Opacity(0.75f);

            private Drawable createSwatchGrid()
            {
                var grid = new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Width = 58,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Full,
                    Spacing = new Vector2(6),
                };

                for (int i = 0; i < PALETTE.Length; i++)
                    grid.Add(new ColourSwatch(i, selectedColour));

                return grid;
            }

            private Drawable createSizeRow()
            {
                var row = new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(4),
                };

                for (int i = 0; i < BRUSH_SIZES.Length; i++)
                    row.Add(new SizeDot(i, selectedSize));

                return row;
            }

            private partial class ColourSwatch : OsuClickableContainer
            {
                private readonly int index;
                private readonly Bindable<int> selection;
                private CircularContainer circle = null!;

                public ColourSwatch(int index, Bindable<int> selection)
                {
                    this.index = index;
                    this.selection = selection;
                }

                [BackgroundDependencyLoader]
                private void load()
                {
                    Size = new Vector2(26);

                    Child = circle = new CircularContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        BorderColour = Colour4.White,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = PALETTE[index],
                        },
                    };

                    Action = () =>
                    {
                        selection.Value = index;
                        circle.ScaleTo(0.8f).ScaleTo(1, 300, Easing.OutBack);
                    };
                }

                protected override void LoadComplete()
                {
                    base.LoadComplete();
                    selection.BindValueChanged(s => circle.BorderThickness = s.NewValue == index ? 3.5f : 0, true);
                }
            }

            private partial class SizeDot : OsuClickableContainer
            {
                private readonly int index;
                private readonly Bindable<int> selection;
                private Circle dot = null!;

                public SizeDot(int index, Bindable<int> selection)
                {
                    this.index = index;
                    this.selection = selection;
                }

                [BackgroundDependencyLoader]
                private void load()
                {
                    Size = new Vector2(18, 22);

                    Child = dot = new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        // la bolita crece con el grosor que representa.
                        Size = new Vector2(6 + BRUSH_SIZES[index]),
                    };

                    Action = () =>
                    {
                        selection.Value = index;
                        dot.ScaleTo(0.7f).ScaleTo(1, 300, Easing.OutBack);
                    };
                }

                protected override void LoadComplete()
                {
                    base.LoadComplete();
                    selection.BindValueChanged(s => dot.FadeColour(s.NewValue == index ? Colour4.White : Colour4.White.Opacity(0.35f), 120), true);
                }
            }
        }
    }
}
