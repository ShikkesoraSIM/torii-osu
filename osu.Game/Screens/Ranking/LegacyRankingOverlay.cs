// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Screens.Select;
using osu.Game.Skinning;
using osu.Game.Skinning.Select;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Ranking
{
    /// <summary>
    /// torii: reemplaza la vista de "detalles" del results con el panel de ranking de osu!stable.
    /// va montado encima del <see cref="ResultsScreen"/>: trackea la visibilidad del
    /// <see cref="StatisticsPanel"/> (el estado al que se entra clickeando el score panel expandido)
    /// y muestra el panel legacy en su lugar, escondiendo el contenido stock de abajo. tambien
    /// re-estiliza el chrome del screen para que quede como stable: la barra de abajo stock (backing
    /// gris, con altura reservada) pasa a ser una barra transparente flotante con los mismos botones,
    /// y el back button global pasa a ser el legacy skineado.
    /// </summary>
    public partial class LegacyRankingOverlay : CompositeDrawable
    {
        private readonly ResultsScreen screen;
        private readonly StatisticsPanel statisticsPanel;
        private readonly Drawable scrollContent;
        private readonly Drawable stockBottomPanel;
        private readonly bool autoShowDetails;

        private BlockingLayer panelLayer = null!;
        private Container panelContainer = null!;
        private bool toolbarHidden;

        // el score para el que ya esta armado el panel: show() puede correr dos veces para el
        // mismo score (pre-armado en load + el sync del statistics panel), y rebuildear al pedo
        // duplica los computes async de los extras (pp, heatmap).
        private ScoreInfo? builtScore;

        // la animacion de entrada de stable corre solo en el PRIMER build post-gameplay; los
        // rebuilds por cambio de score muestran todo al instante.
        private bool pendingEntrance;

        // true cuando el modo directo post-gameplay quedo activo de verdad (autoShowDetails y
        // habia score al armar): el chrome stock no se ve nunca y back sale del screen.
        private bool directModeActive;

        private OsuScrollContainer? currentScroll;
        private Drawable? detailsHint;
        private bool hintVisible = true;

        // la "pagina 1" (el ranking panel) matchea la altura REAL del viewport en cada frame:
        // el espacio DrawSizePreserving puede ser mas alto que 768 (ventanas 16:10, etc) y con
        // altura fija se asomaba el banner de la pagina de abajo sin scrollear.
        private Container? panelPage;

        // la barra flotante de botones (render/nota/coleccion/favorito) estorba al panel stable
        // arriba de todo: solo se muestra scrolleada la vista (o cuando el panel esta cerrado).
        private Container floatingBar = null!;
        private bool floatingBarVisible = true;

        // los scores ajenos no tienen pagina de abajo (las bandas LOCAL/ONLINE/BEATMAP hablan
        // del usuario local): sin below-fold el hint no va y la barra flotante queda siempre.
        private bool hasBelowFold;

        [Resolved]
        private Online.API.IAPIProvider apiProvider { get; set; } = null!;

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        private OsuGame? game;

        public LegacyRankingOverlay(ResultsScreen screen, StatisticsPanel statisticsPanel, Drawable scrollContent, Drawable stockBottomPanel, bool autoShowDetails)
        {
            this.screen = screen;
            this.statisticsPanel = statisticsPanel;
            this.scrollContent = scrollContent;
            this.stockBottomPanel = stockBottomPanel;
            this.autoShowDetails = autoShowDetails;

            pendingEntrance = autoShowDetails;
        }

        [BackgroundDependencyLoader(true)]
        private void load(OsuGame? game)
        {
            this.game = game;

            RelativeSizeAxes = Axes.Both;

            var buttons = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(5),
                Direction = FillDirection.Horizontal,
            };

            // los botones de accion torii (render video, nota) + coleccion/favorito, sin el
            // backing gris de la barra stock. retry y watch-replay NO van aca: viven EN el
            // panel legacy (debajo del grade), como stable.
            buttons.Add(new RenderVideoButton(screen.SelectedScore.Value)
            {
                Score = { BindTarget = screen.SelectedScore },
            });
            buttons.Add(new AddNoteButton(screen.SelectedScore.Value)
            {
                Score = { BindTarget = screen.SelectedScore },
            });

            if (screen.Score?.BeatmapInfo != null)
                buttons.Add(new CollectionButton(screen.Score.BeatmapInfo));

            if (screen.Score?.BeatmapInfo?.BeatmapSet != null && screen.Score.BeatmapInfo.BeatmapSet.OnlineID > 0)
                buttons.Add(new FavouriteButton(screen.Score.BeatmapInfo.BeatmapSet));

            InternalChildren = new Drawable[]
            {
                // la vista de ranking legacy, toggleada con el estado del statistics panel.
                panelLayer = new BlockingLayer
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.Black,
                            Alpha = 0.8f,
                        },
                        new DrawSizePreservingFillContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            TargetDrawSize = new Vector2(1366, 768),
                            Strategy = DrawSizePreservationStrategy.Minimum,
                            Child = panelContainer = new Container { RelativeSizeAxes = Axes.Both },
                        },
                    },
                },
                // botones flotantes centrados (visibilidad manejada en Update) + back button skineado.
                floatingBar = new Container
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = TwoLayerButton.SIZE_EXTENDED.Y,
                    Child = buttons,
                },
                new DrawSizePreservingFillContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    TargetDrawSize = new Vector2(1366, 768),
                    Strategy = DrawSizePreservationStrategy.Minimum,
                    Child = new LegacyBackButton
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        // mismo two-stage que Esc: OnBackButton cierra la vista de detalles si
                        // corresponde (modo browse); en el modo stable directo devuelve false
                        // y salimos de una (nunca se ve el carousel de scores stock).
                        Action = () =>
                        {
                            if (!screen.OnBackButton())
                                screen.Exit();
                        },
                    },
                },
            };

            // post-gameplay: el panel legacy queda armado y VISIBLE aca mismo, todavia en el load
            // thread del screen (scrollContent es parte del mismo subtree en carga, mutarlo es
            // legal) — el primer frame del results ya es el ranking de stable, sin ningun flash
            // del chrome de lazer.
            if (autoShowDetails)
            {
                var score = screen.SelectedScore.Value ?? screen.Score;

                if (score != null)
                {
                    directModeActive = true;
                    rebuildPanel(score);
                    panelLayer.Alpha = 1;
                    scrollContent.Alpha = 0;

                    // arrancamos con el panel a la vista: la barra flotante espera al scroll.
                    floatingBar.Alpha = 0;
                    floatingBarVisible = false;
                }
            }
        }

        protected override void Update()
        {
            base.Update();

            // la animacion de entrada del screen vuelve a fadear la barra stock; sus hijos
            // (los botones que recreamos aca) dibujarian a altura cero si no.
            if (stockBottomPanel.Alpha > 0)
                stockBottomPanel.Alpha = 0;

            // pagina 1 del scroll = altura real del viewport (ver comentario del campo).
            if (panelPage != null && currentScroll != null && Math.Abs(panelPage.Height - currentScroll.DrawHeight) > 0.1f)
                panelPage.Height = currentScroll.DrawHeight;

            // si el screen sale con la vista de detalles abierta, dejamos la toolbar en paz:
            // OsuGame la re-muestra solo en los cambios de screen normales, y el song select
            // legacy la esconde por su cuenta. re-mostrarla aca corria una carrera contra el
            // screen de destino y la dejaba trabada visible.
            if (toolbarHidden && !screen.IsCurrentScreen())
                toolbarHidden = false;

            // el hint de scroll desaparece apenas se scrollea (como el "Online Ranking" de
            // stable, que se esconde con ScrollPosition.Y > 20) y vuelve al subir del todo.
            if (detailsHint != null && currentScroll != null && hasBelowFold)
            {
                bool shouldShow = currentScroll.Current <= 32;

                if (shouldShow != hintVisible)
                {
                    hintVisible = shouldShow;
                    detailsHint.FadeTo(shouldShow ? 1 : 0, 200);
                }
            }

            // la barra flotante de botones tapaba el hint y ensuciaba el panel stable: con el
            // panel a la vista solo aparece al scrollear a los detalles (complementa al hint);
            // con el panel cerrado (modo browse sobre el carousel stock) o sin pagina de abajo
            // (score ajeno) queda siempre.
            bool barShouldShow = panelLayer.Alpha < 0.5f || !hasBelowFold || (currentScroll != null && currentScroll.Current > 300);

            if (barShouldShow != floatingBarVisible)
            {
                floatingBarVisible = barShouldShow;
                floatingBar.FadeTo(barShouldShow ? 1 : 0, 200);
            }
        }

        /// <summary>
        /// back en el modo stable directo: si la vista esta scrolleada en los extras, primero
        /// vuelve arriba (como el Esc de stable cierra el dialog de online ranking); devuelve
        /// false cuando ya esta arriba, y ahi el screen sale de una.
        /// </summary>
        public bool HandleBackScroll()
        {
            if (currentScroll != null && currentScroll.Current > 32)
            {
                currentScroll.ScrollToStart();
                return true;
            }

            return false;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // look de stable: sin el back button rosa de lazer, y la barra stock de abajo ni
            // reserva altura de layout (su fila de grid es auto-size) ni dibuja. va ACA y no en
            // load(): setear BackButtonVisibility dispara sincronico la animacion del BackButton
            // global (PopOut), y desde el loader thread async eso es una mutacion ilegal
            // (InvalidThreadForMutationException). LoadComplete corre siempre en el update thread.
            screen.BackButtonVisibility.Value = false;
            stockBottomPanel.Height = 0;

            if (directModeActive)
            {
                // post-gameplay: el panel legacy ya esta visible desde el primer frame (lo dejo
                // armado load()). aca solo sincronizamos el estado del screen (detach del panel
                // stock, dim del fondo) para que SelectedScore y compania queden como si se
                // hubiera abierto la vista de detalles — todo tapado por el panel legacy.
                // en este modo NUNCA volvemos al chrome stock: el bind ignora el hide, y el
                // screen (LegacyStableDirectMode) hace que back salga directo y Select no togglee.
                statisticsPanel.State.BindValueChanged(v =>
                {
                    if (v.NewValue == Visibility.Visible)
                        show();
                });

                screen.LegacyStableDirectMode = true;

                // el sync va con una demora corta: el detach del score panel stock asume que el
                // panel ya se asento (el delay original de 500ms protegia ese orden, no era solo
                // estetico). como todo pasa tapado por el panel legacy, la demora no se ve.
                Scheduler.AddDelayed(() =>
                {
                    if (statisticsPanel.State.Value != Visibility.Visible && (screen.SelectedScore.Value ?? screen.Score) != null)
                        statisticsPanel.ToggleVisibility();
                }, 250);

                // stable toca el aplauso al entrar al ranking. el flair stock (accuracy circle,
                // que era quien lo disparaba al final de su animacion) se suprime en este modo,
                // asi que lo tocamos nosotros. Schedule: PlayApplause exige IsCurrentScreen.
                var score = screen.SelectedScore.Value ?? screen.Score;
                if (score != null)
                    Schedule(() => screen.PlayApplause(score.Rank));
            }
            else
            {
                statisticsPanel.State.BindValueChanged(v =>
                {
                    if (v.NewValue == Visibility.Visible)
                        show();
                    else
                        hide();
                }, true);
            }

            // cambio de score (flechas izq/der) con la vista de detalles abierta.
            screen.SelectedScore.BindValueChanged(_ =>
            {
                if (directModeActive || statisticsPanel.State.Value == Visibility.Visible)
                    show();
            });
        }

        private void show()
        {
            var score = screen.SelectedScore.Value ?? screen.Score;

            if (score == null)
                return;

            rebuildPanel(score);

            panelLayer.FadeIn(250, Easing.OutQuint);
            // esconder el contenido stock (statistics/panels) entero asi el panel legacy queda
            // solo sobre el fondo del beatmap. la toolbar tambien se va, como stable.
            scrollContent.FadeOut(120);

            if (game != null)
            {
                game.Toolbar.Hide();
                toolbarHidden = true;
            }
        }

        private void hide()
        {
            panelLayer.FadeOut(200, Easing.OutQuint);
            scrollContent.FadeIn(200);

            if (toolbarHidden)
            {
                game?.Toolbar.Show();
                toolbarHidden = false;
            }
        }

        private void rebuildPanel(ScoreInfo score)
        {
            if (score == builtScore)
                return;

            builtScore = score;

            bool animate = pendingEntrance;
            pendingEntrance = false;

            // primero el skin actual, con el classic bundleado de fallback (para el score font y
            // las texturas ranking-* que el skin no traiga). la vista scrollea como la de stable:
            // el ranking panel es la primera "pagina", scrollear para abajo revela la seccion de
            // extras (pp, heatmap de accuracy, tags del mapa). el header (barra negra + titulo +
            // ranking-title) queda FIJO por encima del scroll, como los ViewOffsetImmune de stable.
            panelContainer.Child = new SkinProvidingContainer(skins.DefaultClassicSkin)
            {
                RelativeSizeAxes = Axes.Both,
                Child = new SkinProvidingContainer(skins.CurrentSkin.Value)
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        currentScroll = new OsuScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ScrollbarVisible = false,
                            Child = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Children = new Drawable[]
                                {
                                    panelPage = new Container
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 768,
                                        Child = new LegacyRankingPanel(score, animate),
                                    },
                                    // la pagina de abajo estilo stable (el RankingDialog con las
                                    // bandas LOCAL / ONLINE / BEATMAP); vacia para scores ajenos.
                                    new LegacyOnlineRanking(score),
                                },
                            },
                        },
                        new HeaderStrip(score),
                        detailsHint = new ScrollHintButton(@"v  Online Ranking  v")
                        {
                            Action = () => currentScroll?.ScrollToEnd(),
                        },
                    },
                },
            };

            hasBelowFold = score.UserID == apiProvider.LocalUser.Value?.OnlineID;
            detailsHint.Alpha = hasBelowFold ? 1 : 0;
            hintVisible = hasBelowFold;
        }

        /// <summary>
        /// mientras esta visible, se traga el input de puntero asi los clicks no llegan al
        /// contenido stock (escondido) de abajo. los botones flotantes y el back button son
        /// hermanos puestos encima, asi que siguen andando.
        /// </summary>
        private partial class BlockingLayer : Container
        {
            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
                => Alpha > 0.5f && base.ReceivePositionalInputAt(screenSpacePos);

            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnHover(HoverEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
            protected override bool OnScroll(ScrollEvent e) => true;
        }

        /// <summary>
        /// el header fijo de stable (ViewOffsetImmune): barra negra de 60 unidades al 80% con
        /// el titulo del mapa / creador / jugador, y el ranking-title colgando arriba a la
        /// derecha. no scrollea con el panel.
        /// </summary>
        private partial class HeaderStrip : CompositeDrawable
        {
            private readonly ScoreInfo score;

            [Resolved]
            private SkinManager skins { get; set; } = null!;

            public HeaderStrip(ScoreInfo score)
            {
                this.score = score;
            }

            [BackgroundDependencyLoader]
            private void load(ISkinSource skin)
            {
                RelativeSizeAxes = Axes.X;
                Height = 60 * 1.6f;

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                        Alpha = 0.8f,
                    },
                    new OsuSpriteText
                    {
                        Position = new Vector2(0, 0),
                        Font = LegacyFonts.Get(22 * 1.6f),
                        Shadow = true,
                        Text = $"{score.BeatmapInfo?.Metadata.Artist} - {score.BeatmapInfo?.Metadata.Title} [{score.BeatmapInfo?.DifficultyName}]",
                    },
                    new OsuSpriteText
                    {
                        Position = new Vector2(1.6f, 20 * 1.6f),
                        Font = LegacyFonts.Get(16 * 1.6f),
                        Shadow = true,
                        Text = $"Beatmap by {score.BeatmapInfo?.Metadata.Author.Username}",
                    },
                    new OsuSpriteText
                    {
                        Position = new Vector2(1.6f, 34 * 1.6f),
                        Font = LegacyFonts.Get(16 * 1.6f),
                        Shadow = true,
                        Text = $"Played by {score.User.Username} on {score.Date.ToLocalTime():g}.",
                    },
                    new Sprite
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        X = -20 * 1.6f,
                        Texture = skin.GetTexture(@"ranking-title") ?? skins.DefaultClassicSkin.GetTexture(@"ranking-title"),
                    },
                };
            }
        }

        /// <summary>
        /// el boton hint de scroll de stable ("v Online Ranking v", centrado abajo): el pButton
        /// REAL de stable (texturas button-left/middle/right, three-slice, tinte BlueViolet),
        /// invita a scrollear a la seccion de abajo y desaparece mientras la vista no este arriba.
        /// </summary>
        private partial class ScrollHintButton : OsuClickableContainer
        {
            private readonly string text;

            [Resolved]
            private SkinManager skins { get; set; } = null!;

            public ScrollHintButton(string text)
            {
                this.text = text;

                Anchor = Anchor.BottomCentre;
                Origin = Anchor.BottomCentre;
                Size = new Vector2(200, 30) * 1.6f;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var left = skins.DefaultClassicSkin.GetTexture(@"button-left");
                var middle = skins.DefaultClassicSkin.GetTexture(@"button-middle");
                var right = skins.DefaultClassicSkin.GetTexture(@"button-right");

                var tint = Color4.BlueViolet;

                // three-slice como el pButton de stable: caps a tamaño natural + middle
                // estirado, todo escalado de la altura nativa de la textura (71) a la del boton.
                float scale = Height / 71f;
                float texSpaceWidth = Width / scale;
                float capWidth = left?.DisplayWidth ?? 16;

                Children = new Drawable[]
                {
                    new Container
                    {
                        Size = new Vector2(texSpaceWidth, 71),
                        Scale = new Vector2(scale),
                        Colour = tint,
                        Children = new Drawable[]
                        {
                            new Sprite { Texture = left },
                            new Sprite
                            {
                                X = capWidth,
                                Size = new Vector2(texSpaceWidth - capWidth * 2, 71),
                                Texture = middle,
                            },
                            new Sprite
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Texture = right,
                            },
                        },
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = LegacyFonts.Get(15 * 1.6f),
                        Shadow = true,
                        Text = text,
                    },
                };
            }
        }
    }
}
