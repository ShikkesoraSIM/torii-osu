// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Backgrounds;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input.Bindings;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    public abstract partial class ToolbarButton : OsuClickableContainer, IKeyBindingHandler<GlobalAction>
    {
        // 3 -> 5 con glass: mas aire ENTRE pastillas, que es lo que hace que se lean
        // como botones sueltos y no como una tira de bloques pegados.
        public static readonly float PADDING = OsuColour.IsGlassTheme ? 5 : 3;

        /// <summary>
        /// Radio de la pastilla del boton. Publico porque el boton de usuario lo necesita para
        /// que la esquina de la foto de perfil quede concentrica con la del boton.
        /// </summary>
        public static readonly float CHIP_CORNER_RADIUS = OsuColour.IsGlassTheme ? 10 : 6;

        protected GlobalAction? Hotkey { get; set; }

        public void SetIcon(Drawable icon)
        {
            IconContainer.Icon = icon;
            IconContainer.Show();
        }

        public void SetIcon(IconUsage icon) => SetIcon(new SpriteIcon { Icon = icon });

        public LocalisableString TooltipMain
        {
            get => tooltip1.Text;
            set => tooltip1.Text = value;
        }

        public LocalisableString TooltipSub
        {
            get => tooltip2.Text;
            set => tooltip2.Text = value;
        }

        protected virtual Anchor TooltipAnchor => Anchor.TopLeft;

        protected readonly Container ButtonContent;
        protected ConstrainedIconContainer IconContainer;
        protected Box HoverBackground;
        private readonly Box flashBackground;
        private readonly FillFlowContainer tooltipContainer;
        private readonly SpriteText tooltip1;
        private readonly SpriteText tooltip2;
        protected FillFlowContainer Flow;

        protected readonly Container BackgroundContent;

        private readonly FillFlowContainer subTooltipFlow;

        protected ToolbarButton()
        {
            AutoSizeAxes = Axes.X;
            RelativeSizeAxes = Axes.Y;

            Children = new Drawable[]
            {
                ButtonContent = new Container
                {
                    Width = Toolbar.HEIGHT,
                    RelativeSizeAxes = Axes.Y,
                    Padding = new MarginPadding(PADDING),
                    Children = new Drawable[]
                    {
                        BackgroundContent = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            // Mismo vocabulario que los paneles del carousel (ver Panel.cs):
                            // radio grande con glass, borde fino y sombra corta. El
                            // CornerExponent 3 es lo que hace la esquina squircle en vez de
                            // un cuarto de circulo, que es la diferencia entre que se lea
                            // Apple o se lea Android.
                            CornerRadius = CHIP_CORNER_RADIUS,
                            CornerExponent = 3f,
                            BorderThickness = OsuColour.IsGlassTheme ? 1 : 0,
                            BorderColour = Color4.White.Opacity(0.18f),
                            EdgeEffect = OsuColour.IsGlassTheme
                                ? new EdgeEffectParameters
                                {
                                    Type = EdgeEffectType.Shadow,
                                    Colour = Color4.Black.Opacity(0.25f),
                                    Radius = 4,
                                    Offset = new Vector2(0, 1),
                                    Hollow = true,
                                }
                                : default,
                            Children = new Drawable[]
                            {
                                // Relleno permanente, no solo al hover: sin esto el boton es
                                // un icono flotando y no una pastilla.
                                //
                                // Va OSCURO y no blanco a proposito. La barra quedo casi
                                // transparente, asi que el contraste tiene que salir de aca:
                                // un chip oscuro se recorta contra el fondo del juego, que
                                // casi siempre es mas claro que el. Y cuando el fondo tambien
                                // es oscuro, lo que define el borde del boton es el
                                // BorderColour blanco de arriba, no el relleno.
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = Color4.Black.Opacity(OsuColour.IsGlassTheme ? 0.45f : 0f),
                                },
                                HoverBackground = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = OsuColour.Gray(80).Opacity(180),
                                    Blending = BlendingParameters.Additive,
                                    Alpha = 0,
                                },
                                flashBackground = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Alpha = 0,
                                    Colour = Color4.White.Opacity(100),
                                    Blending = BlendingParameters.Additive,
                                },
                            }
                        },
                        Flow = new FillFlowContainer
                        {
                            Direction = FillDirection.Horizontal,
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Padding = new MarginPadding { Left = Toolbar.HEIGHT / 2, Right = Toolbar.HEIGHT / 2 },
                            RelativeSizeAxes = Axes.Y,
                            AutoSizeAxes = Axes.X,
                            Children = new Drawable[]
                            {
                                IconContainer = new ConstrainedIconContainer
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Size = new Vector2(20),
                                    Alpha = 0,
                                },
                            },
                        },
                    },
                },
                tooltipContainer = new FillFlowContainer
                {
                    Direction = FillDirection.Vertical,
                    RelativeSizeAxes = Axes.Both, // stops us being considered in parent's autosize
                    Anchor = TooltipAnchor.HasFlag(Anchor.x0) ? Anchor.BottomLeft : Anchor.BottomRight,
                    Origin = TooltipAnchor,
                    Position = new Vector2(TooltipAnchor.HasFlag(Anchor.x0) ? 5 : -5, 5),
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        tooltip1 = new OsuSpriteText
                        {
                            Anchor = TooltipAnchor,
                            Origin = TooltipAnchor,
                            Shadow = true,
                            Font = OsuFont.GetFont(size: 22, weight: FontWeight.Bold),
                        },
                        subTooltipFlow = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Anchor = TooltipAnchor,
                            Origin = TooltipAnchor,
                            Direction = FillDirection.Horizontal,
                            Children = new Drawable[]
                            {
                                tooltip2 = new OsuSpriteText { Shadow = true },
                            }
                        }
                    }
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (Hotkey != null)
            {
                subTooltipFlow.Add(new HotkeyDisplay
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Hotkey = new Hotkey(Hotkey.Value),
                    Margin = new MarginPadding { Left = 3 },
                });
            }
        }

        protected override bool OnMouseDown(MouseDownEvent e) => false;

        protected override bool OnClick(ClickEvent e)
        {
            flashBackground.FadeIn(50).Then().FadeOutFromOne(800, Easing.OutQuint);
            tooltipContainer.FadeOut(100);
            return base.OnClick(e);
        }

        protected override bool OnHover(HoverEvent e)
        {
            HoverBackground.FadeIn(300, Easing.OutQuint);
            tooltipContainer.FadeIn(200, Easing.OutQuint);

            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            HoverBackground.FadeOut(200, Easing.Out);
            tooltipContainer.FadeOut(100, Easing.Out);
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Action == Hotkey && !e.Repeat)
            {
                TriggerClick();
                return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }
    }

    public partial class OpaqueBackground : Container
    {
        public OpaqueBackground()
        {
            RelativeSizeAxes = Axes.Both;

            // Con el tema glass esto queda VACIO a proposito.
            //
            // Lo usa el selector de rulesets, y tal cual venia es un Box gris OPACO con
            // Triangles v1 animados encima. Arriba de una barra que ahora es frost
            // transparente, eso es un parche gris con triangulos moviendose en el medio: es
            // exactamente lo que rompia la barra. Sin el, el selector deja ver el frost como
            // cualquier otro tramo.
            //
            // De paso nos saca de encima las particulas de los Triangles, que se reanimaban
            // en cada cuadro con la barra SIEMPRE en pantalla.
            if (OsuColour.IsGlassTheme)
                return;

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = OsuColour.Gray(30)
                },
                new Triangles
                {
                    RelativeSizeAxes = Axes.Both,
                    ColourLight = OsuColour.Gray(40),
                    ColourDark = OsuColour.Gray(20),
                },
            };
        }
    }
}
