// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Backdrops;
using osuTK;
using osuTK.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Allocation;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Game.Rulesets;
using osu.Framework.Input.Bindings;
using osu.Game.Configuration;
using osu.Game.Graphics.Containers;
using osu.Game.Input.Bindings;

namespace osu.Game.Overlays.Toolbar
{
    public partial class Toolbar : OverlayContainer, IKeyBindingHandler<GlobalAction>
    {
        public const float HEIGHT = 40;
        public const float TOOLTIP_HEIGHT = 30;

        /// <summary>
        /// Whether the user hid this <see cref="Toolbar"/> with <see cref="GlobalAction.ToggleToolbar"/>.
        /// In this state, automatic toggles should not occur, respecting the user's preference to have no toolbar.
        /// </summary>
        private bool hiddenByUser;

        /// <summary>
        /// torii: cuando el auto-hide esta prendido, esta toolbar la maneja el reveal-por-hover de
        /// OsuGame. en ese caso ignoramos <see cref="hiddenByUser"/> asi un Ctrl+T previo no deja la
        /// toolbar trabada sin poder revelarse al llevar el mouse arriba.
        /// </summary>
        public bool AutoHideActive { get; set; }

        // torii: cuenta los Ctrl+T; OsuGame sugiere el auto-hide cuando llega a ~30.
        private Bindable<int> toolbarToggleCount = null!;

        private InputManager cachedInputManager;

        /// <summary>
        /// torii: la toolbar esta en uso de verdad, asi que el auto-hide no tiene que
        /// arrancar la cuenta regresiva. Cubre dos cosas que el IsHovered pelado no ve:
        ///
        /// 1. Un popup NUESTRO abierto que esconderse rompe: la tarjetita de login (la
        ///    cierra <see cref="PopOut"/>) y el popover del pulse (se desvanece con la
        ///    barra). Los overlays grandes (chat, settings) viven afuera y esconder la
        ///    barra no les hace nada, como siempre: a proposito NO traban el auto-hide,
        ///    porque se abren tambien por tecla y dejarian la barra clavada para siempre.
        ///
        /// 2. El cursor sobre cualquier cosa del arbol de la toolbar. IsHovered miente
        ///    aca: el InputManager marca hovered hasta el primer drawable que handlea
        ///    OnHover, y los botones lo handlean, asi que con el cursor sobre un boton
        ///    (o sobre la tarjetita de login, que cuelga mas abajo de la banda) la
        ///    toolbar misma nunca figura como hovered.
        /// </summary>
        public bool HasOpenPopup
        {
            get
            {
                // antes de cargar no hay arbol de input ni popups que mirar.
                if (!IsLoaded)
                    return false;

                if (userButton?.StateContainer?.State.Value == Visibility.Visible)
                    return true;

                if (pulseButton?.PopoverVisible == true)
                    return true;

                // Mismo motivo que el pulse: el panel se desvanece junto con la barra,
                // asi que esconderse mientras esta abierto se lo cierra en la cara al
                // que lo estaba leyendo.
                if (rankedButton?.PopoverVisible == true)
                    return true;

                cachedInputManager ??= GetContainingInputManager();

                if (cachedInputManager == null)
                    return false;

                foreach (var hovered in cachedInputManager.HoveredDrawables)
                {
                    for (Drawable d = hovered; d != null; d = d.Parent)
                    {
                        if (d == this)
                            return true;
                    }
                }

                return false;
            }
        }

        public Action OnHome;

        /// <summary>
        /// Acompania el oscurecido que OsuGame le hace a la pantalla cuando se abre un overlay
        /// que bloquea (ajustes, por ejemplo). Antes la barra se quedaba brillante arriba de una
        /// pantalla apagada, que es justo lo que se nota.
        /// </summary>
        /// <remarks>
        /// Va sobre los BOTONES y no sobre la barra entera a proposito. El fondo es un
        /// GlassBackdrop que samplea la escena, y la escena YA viene oscurecida por el fade que
        /// OsuGame le aplica al ScreenContainer: tintar la barra entera lo oscureceria dos veces.
        /// </remarks>
        public void DimForOverlay(bool dim) =>
            chrome?.FadeColour(dim ? OsuColour.Gray(0.5f) : Color4.White, 500, Easing.OutQuint);

        private ToolbarUserButton userButton;
        private ToriiServerPulseButton pulseButton;
        private ToolbarRankedPlayButton rankedButton;

        /// <summary>
        /// La pildora de ranked play. Expuesta para que la test scene del toolbar pueda
        /// mostrarla con cola y partida sin necesidad de un server atras: verla al lado
        /// del pulse y de los puntos es la unica forma de juzgar si el tamaño y el aire
        /// entre pildoras estan bien.
        /// </summary>
        internal ToolbarRankedPlayButton RankedPlayButton => rankedButton;
        private ToolbarRulesetSelector rulesetSelector;

        /// <summary>
        /// Los botones de la barra, sin el fondo. Lo oscurece <see cref="DimForOverlay"/>.
        /// </summary>
        private Drawable chrome;

        // Torii custom UI hue (Menu scope): re-tints the toolbar chrome live.
        private IDisposable customUiHueBinding;

        private const double transition_time = 500;

        protected readonly IBindable<OverlayActivation> OverlayActivationMode = new Bindable<OverlayActivation>(OverlayActivation.All);

        // Toolbar and its components need keyboard input even when hidden.
        public override bool PropagateNonPositionalInputSubTree => OverlayActivationMode.Value != OverlayActivation.Disabled;

        public Toolbar()
        {
            RelativeSizeAxes = Axes.X;
            Size = new Vector2(1, HEIGHT);
            AlwaysPresent = true;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            // this only needed to be set for the initial LoadComplete/Update, so layout completes and gets buttons in a state they can correctly handle keyboard input for hotkeys.
            AlwaysPresent = false;
        }

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; }

        [BackgroundDependencyLoader(true)]
        private void load(OsuGame osuGame, OsuConfigManager config)
        {
            toolbarToggleCount = config.GetBindable<int>(OsuSetting.ToriiToolbarToggleCount);

            ToolbarBackground background;
            HoverInterceptor interceptor;

            Children = new Drawable[]
            {
                background = new ToolbarBackground(),
                chrome = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ColumnDimensions = new[]
                    {
                        new Dimension(GridSizeMode.AutoSize),
                        new Dimension(),
                        new Dimension(GridSizeMode.AutoSize)
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new Container
                            {
                                Name = "Left buttons",
                                RelativeSizeAxes = Axes.Y,
                                AutoSizeAxes = Axes.X,
                                Depth = float.MinValue,
                                Children = new Drawable[]
                                {
                                    // ACA ESTABA el "mitad negra, mitad transparente".
                                    //
                                    // Upstream le pone a cada grupo de botones su PROPIO Box
                                    // opaco encima del ToolbarBackground, asi que el fondo
                                    // real de la barra en los extremos son estos y no el
                                    // ToolbarBackground: por mas frost que le pongamos atras,
                                    // queda tapado. El unico tramo sin Box es el del medio
                                    // (el selector de rulesets), y por eso era el unico que
                                    // dejaba ver algo.
                                    //
                                    // Con glass los sacamos y el frost del ToolbarBackground
                                    // pasa a ser el fondo de la barra ENTERA, que es la idea.
                                    // Alpha 0 y no IsPresent, o sea ni se dibujan.
                                    new Box
                                    {
                                        Colour = OsuColour.Gray(0.1f),
                                        RelativeSizeAxes = Axes.Both,
                                        Alpha = OsuColour.IsGlassTheme ? 0 : 1,
                                    },
                                    new FillFlowContainer
                                    {
                                        Direction = FillDirection.Horizontal,
                                        RelativeSizeAxes = Axes.Y,
                                        AutoSizeAxes = Axes.X,
                                        Children = new Drawable[]
                                        {
                                            new ToolbarSettingsButton(),
                                            new ToolbarHomeButton
                                            {
                                                Action = () => OnHome?.Invoke()
                                            },
                                        },
                                    },
                                }
                            },
                            new Container
                            {
                                Name = "Ruleset selector",
                                RelativeSizeAxes = Axes.Both,
                                Children = new Drawable[]
                                {
                                    new OsuScrollContainer(Direction.Horizontal)
                                    {
                                        ScrollbarVisible = false,
                                        RelativeSizeAxes = Axes.Both,
                                        Masking = false,
                                        Children = new Drawable[]
                                        {
                                            rulesetSelector = new ToolbarRulesetSelector()
                                        }
                                    },
                                    // Este degradado existia para que el scroll de rulesets
                                    // se desvaneciera contra el Box opaco de los botones de
                                    // la derecha. Sin ese Box no tiene contra que desvanecer:
                                    // lo unico que hace es cortar el frost con una mancha
                                    // oscura de 50px justo en el medio de la barra.
                                    new Box
                                    {
                                        Colour = ColourInfo.GradientHorizontal(OsuColour.Gray(0.1f).Opacity(0), OsuColour.Gray(0.1f)),
                                        Width = 50,
                                        RelativeSizeAxes = Axes.Y,
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                        Alpha = OsuColour.IsGlassTheme ? 0 : 1,
                                    },
                                }
                            },
                            new Container
                            {
                                Name = "Right buttons",
                                RelativeSizeAxes = Axes.Y,
                                AutoSizeAxes = Axes.X,
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Children = new Drawable[]
                                {
                                    // Mismo caso que el de la izquierda: tapaba el frost.
                                    new Box
                                    {
                                        Colour = OsuColour.Gray(0.1f),
                                        RelativeSizeAxes = Axes.Both,
                                        Alpha = OsuColour.IsGlassTheme ? 0 : 1,
                                    },
                                    new FillFlowContainer
                                    {
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                        Direction = FillDirection.Horizontal,
                                        RelativeSizeAxes = Axes.Y,
                                        AutoSizeAxes = Axes.X,
                                        Children = new Drawable[]
                                        {
                                            // torii: News / Changelog / Wiki sacados del toolbar (no los usamos).
                                            new ToolbarRankingsButton(),
                                            new ToolbarBeatmapListingButton(),
                                            new ToolbarChatButton(),
                                            new ToolbarSocialButton(),
                                            new ToolbarMusicButton(),
                                            userButton = new ToolbarUserButton(),
                                            new ToolbarAdminButton(),
                                            new ToolbarCosmeticStoreButton(),
                                            // torii: un poco de aire alrededor de los pills de puntos + pulse
                                            // asi no se ven pegados entre si ni contra el store / clock.
                                            new Container { RelativeSizeAxes = Axes.Y, Width = 4 },
                                            new ToolbarPointsButton(),
                                            new Container { RelativeSizeAxes = Axes.Y, Width = 4 },
                                            new ToriiServerPulseButton(),
                                            new Container { RelativeSizeAxes = Axes.Y, Width = 4 },
                                            rankedButton = new ToolbarRankedPlayButton(),
                                            new Container { RelativeSizeAxes = Axes.Y, Width = 4 },
                                            new ToolbarClock(),
                                            new ToolbarNotificationButton(),
                                        }
                                    },
                                }
                            },
                        },
                    }
                },
                interceptor = new HoverInterceptor
                {
                    RelativeSizeAxes = Axes.Both
                }
            };

            ((IBindable<bool>)background.ShowGradient).BindTo(interceptor.ReceivedHover);

            if (osuGame != null)
                OverlayActivationMode.BindTo(osuGame.OverlayActivationMode);

            // Seed the saved hue and keep the toolbar chrome in sync as the user
            // edits the custom UI hue (Menu scope) live in settings.
            if (config != null)
            {
                background.Hue = CustomUiHueHelper.ResolveHue(config, OverlayColourScheme.Blue.GetHue(), CustomUiHueScope.Menu);
                customUiHueBinding = CustomUiHueHelper.BindHue(config, OverlayColourScheme.Blue.GetHue(), CustomUiHueScope.Menu, hue => background.Hue = hue);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            rulesetSelector.Current.BindTo(ruleset);
        }

        public partial class ToolbarBackground : Container
        {
            public Bindable<bool> ShowGradient { get; } = new BindableBool();

            private readonly Box solidBackground;
            private readonly Box gradientBackground;

            // Solo con el tema glass; null en el resto y ahi nada de esto corre.
            private readonly GlassBackdrop glass;

            // Default fallback (Blue scheme hue 200°) matches the rest of the
            // menu chrome. ResolveHue() in Toolbar.load overrides this with the
            // user's CustomUIHue if enabled for the Menu scope.
            private int hue = 200;

            public int Hue
            {
                get => hue;
                set
                {
                    if (hue == value) return;

                    hue = value;
                    applyHue();
                }
            }

            public ToolbarBackground()
            {
                RelativeSizeAxes = Axes.Both;

                // Para recortar el vidrio, que a proposito es mas alto que la barra.
                Masking = true;
                ChildrenEnumerable = new Drawable[]
                {
                    // Frost real en toda la barra. Los numeros NO son los del resto de los
                    // paneles de vidrio y esa es toda la gracia: la barra mide 40 de alto, y
                    // con la escala default (0.2) el buffer del blur quedaba en 8 texeles.
                    // Un gaussiano de sigma 3.5 cubre mas que eso, asi que en vez de
                    // desenfocar promediaba la franja entera a un gris plano.
                    //
                    // Con escala 0.6 el buffer queda en ~24 texeles de alto, suficiente para
                    // que el kernel tenga donde desenfocar, y sigma 5 da unos 8px de radio en
                    // pantalla (el radio que se ve es sigma dividido la escala). Sale barato
                    // igual: el area es chiquita porque la barra es una franja.
                    glass = OsuColour.IsGlassTheme
                        ? new GlassBackdrop
                        {
                            // Mas ALTO que la barra a proposito, y el Masking de arriba lo
                            // recorta. Un blur samplea lo que tiene alrededor: si el buffer
                            // termina justo en el borde de la barra, las ultimas filas se
                            // mezclan con el vacio de afuera y el borde de abajo queda
                            // lavado. Con el doble de alto ese artefacto cae fuera de lo que
                            // se ve, y de paso el blur agarra contexto de la escena que sigue
                            // abajo, que es lo que hace que se lea continuo con la pantalla.
                            RelativeSizeAxes = Axes.X,
                            Height = HEIGHT * 2,
                            // Asimetrico en los DOS parametros, y esa es la clave.
                            //
                            // Lo que se ve es sigma dividido la escala. En horizontal queremos
                            // radio grande (~43px) para que el borde del wedge de song select
                            // y el de la barra de busqueda se deshagan en un degradado en vez
                            // de leerse como bloques. En vertical la franja tiene pocos
                            // texeles y pasarse la aplana a un color liso, asi que va corto.
                            //
                            // Antes esto era sigma 26 sobre escala 0.6 pareja: mismos 43px
                            // pero ~57 taps por texel sobre un buffer cuatro veces mas ancho
                            // del necesario. Con la escala tambien por eje, los mismos 43px
                            // salen ~19 taps. Identico en pantalla, como un tercio del trabajo.
                            BlurSigmaVector = new Vector2(8.6f, 3f),
                            FrameBufferScaleVector = new Vector2(0.2f, 0.6f),
                        }
                        : null,
                    solidBackground = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                    gradientBackground = new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Anchor = Anchor.BottomLeft,
                        Alpha = 0,
                        Height = 80,
                        // Mucho mas suave que antes (0.7 -> 0.25): con la barra ya
                        // frosteada, un degradado negro fuerte al pasar el mouse es
                        // justamente lo que la hacia ver "negra abajo y transparente
                        // arriba" en vez de pareja. Sigue estando para que las
                        // descripciones se lean.
                        Colour = ColourInfo.GradientVertical(
                            OsuColour.Gray(0f).Opacity(0.25f), OsuColour.Gray(0).Opacity(0)),
                    },
                }.Where(d => d != null);

                applyHue();
            }

            // OverlayColourProvider derives Background6 as HSL(hue, 0.1, 0.1) —
            // same lightness as the legacy Gray(0.1f) but tinted by the active
            // hue. We instantiate a throwaway provider rather than resolving the
            // ambient one because the Toolbar lives at the root of the OsuGame
            // tree, outside any OverlayColourProvider scope.
            private void applyHue()
            {
                if (solidBackground == null)
                    return;

                var background6 = new OverlayColourProvider(hue).Background6;

                if (glass != null)
                {
                    // El tinte va ADENTRO del GlassBackdrop, no en un Box aparte.
                    //
                    // Y hay que setearlo si o si: el GlassBackdrop se arma con un Box de
                    // tinte en Depth -1, o sea DELANTE del blur, y un Box sin Colour arranca
                    // en blanco OPACO. Si no le pasas TintColour, ese blanco te tapa el
                    // desenfoque entero y la barra queda un plano liso. Todos los demas
                    // paneles de vidrio lo setean; el que no lo hacia era este.
                    glass.TintColour = background6.Opacity(0.3f);
                    solidBackground.Alpha = 0;
                    return;
                }

                solidBackground.Alpha = 1;
                solidBackground.Colour = background6.Opacity(ThemeAware.GlassAlpha(0.7f));
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                ShowGradient.BindValueChanged(_ => updateState(), true);
            }

            private void updateState()
            {
                if (ShowGradient.Value)
                    gradientBackground.FadeIn(2500, Easing.OutQuint);
                else
                    gradientBackground.FadeOut(200, Easing.OutQuint);
            }
        }

        /// <summary>
        /// Whenever the mouse cursor is within the bounds of the toolbar, we want the background gradient to show, for toolbar button descriptions to be legible.
        /// Unfortunately we also need to ensure that the toolbar buttons handle hover, to prevent the possibility of multiple descriptions being shown
        /// due to hover events passing through multiple buttons.
        /// This drawable is a workaround, that when placed front-most in the toolbar, allows to see whether hover events have been propagated through it without handling them.
        /// </summary>
        private partial class HoverInterceptor : Drawable
        {
            public IBindable<bool> ReceivedHover => receivedHover;
            private readonly Bindable<bool> receivedHover = new BindableBool();

            protected override bool OnHover(HoverEvent e)
            {
                receivedHover.Value = true;
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                receivedHover.Value = false;
                base.OnHoverLost(e);
            }
        }

        protected override void UpdateState(ValueChangedEvent<Visibility> state)
        {
            bool blockShow = (hiddenByUser && !AutoHideActive) || OverlayActivationMode.Value == OverlayActivation.Disabled;

            if (state.NewValue == Visibility.Visible && blockShow)
            {
                State.Value = Visibility.Hidden;
                return;
            }

            base.UpdateState(state);
        }

        protected override void PopIn()
        {
            this.MoveToY(0, transition_time, Easing.OutQuint);
            this.FadeIn(transition_time / 4, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            userButton.StateContainer?.Hide();

            this.MoveToY(-DrawSize.Y, transition_time, Easing.OutQuint);
            this.FadeOut(transition_time, Easing.InQuint);
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (OverlayActivationMode.Value == OverlayActivation.Disabled)
                return false;

            switch (e.Action)
            {
                case GlobalAction.ToggleToolbar:
                    hiddenByUser = State.Value == Visibility.Visible; // set before toggling to allow the operation to always succeed.
                    toolbarToggleCount.Value++;
                    ToggleVisibility();
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        protected override void Dispose(bool isDisposing)
        {
            customUiHueBinding?.Dispose();
            customUiHueBinding = null;

            base.Dispose(isDisposing);
        }
    }
}
