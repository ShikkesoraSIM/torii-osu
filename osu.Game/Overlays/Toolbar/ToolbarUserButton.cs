// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    public partial class ToolbarUserButton : ToolbarOverlayToggleButton
    {
        private UpdateableAvatar avatar = null!;

        private IBindable<APIUser> localUser = null!;

        private LoadingSpinner spinner = null!;

        private SpriteIcon failingIcon = null!;

        private IBindable<APIState> apiState = null!;

        private OsuSpriteText usernameText = null!;

        // Torii: wraps usernameText so the local user's aura + name colour render
        // behind their name in the toolbar too. Built with a null user and pointed
        // at the real local user via SetUser once they sign in (userChanged), so
        // it survives login-after-load. Potato mode / the aura setting suppress it
        // from inside the container.
        private UserAuraContainer usernameAura = null!;

        /// <summary>
        /// Aire entre la foto y el borde de la pastilla del boton. Tambien es lo que se le
        /// resta al radio de la pastilla para que la esquina de la foto quede CONCENTRICA
        /// con ella y no con un radio cualquiera.
        /// </summary>
        private const float avatar_inset = 2;

        public ToolbarUserButton()
        {
            ButtonContent.AutoSizeAxes = Axes.X;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, IAPIProvider api, LoginOverlay? login)
        {
            Flow.AddRange(new Drawable[]
            {
                usernameAura = new UserAuraContainer(null, usernameText = new OsuSpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                })
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Right = 5 },
                },
                new Container
                {
                    Masking = true,
                    // Con glass la foto se ENCAJA en la pastilla en vez de flotar encima.
                    //
                    // Venia con 32 de lado adentro de una pastilla que mide 30
                    // (Toolbar.HEIGHT menos el padding de arriba y abajo del boton), o sea que
                    // se salia un pixel por lado. Sumado a la sombra propia, leia como una foto
                    // apoyada arriba del boton y no como parte de el.
                    //
                    // Ahora ocupa el alto de la pastilla menos avatar_inset por lado, y el radio
                    // sale del radio de la pastilla menos ese mismo inset, que es la cuenta que
                    // deja las dos esquinas concentricas. La sombra se va: era justamente lo que
                    // le daba el aspecto de estar flotando.
                    CornerRadius = OsuColour.IsGlassTheme
                        ? CHIP_CORNER_RADIUS - avatar_inset
                        : 4,
                    CornerExponent = OsuColour.IsGlassTheme ? 3f : 2f,
                    Size = new Vector2(OsuColour.IsGlassTheme
                        ? Toolbar.HEIGHT - 2 * PADDING - 2 * avatar_inset
                        : 32),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    EdgeEffect = OsuColour.IsGlassTheme
                        ? default
                        : new EdgeEffectParameters
                        {
                            Type = EdgeEffectType.Shadow,
                            Radius = 4,
                            Colour = Color4.Black.Opacity(0.1f),
                        },
                    Children = new Drawable[]
                    {
                        avatar = new UpdateableAvatar(isInteractive: false)
                        {
                            RelativeSizeAxes = Axes.Both,
                        },
                        spinner = new LoadingLayer(dimBackground: true, withBox: false)
                        {
                            BlockPositionalInput = false,
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                        },
                        failingIcon = new SpriteIcon
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Alpha = 0,
                            Size = new Vector2(0.3f),
                            Icon = FontAwesome.Solid.ExclamationTriangle,
                            RelativeSizeAxes = Axes.Both,
                            Colour = colours.YellowLight,
                        },
                    }
                },
                new TransientUserStatisticsUpdateDisplay
                {
                    Alpha = 0,
                }
            });

            if (OsuColour.IsGlassTheme)
            {
                // El Flow de ToolbarButton reserva medio Toolbar.HEIGHT (20px) de cada lado,
                // que para un boton de icono suelto esta bien pero aca deja la foto colgada
                // con un vacio enorme a su derecha. A la derecha alcanza con el mismo aire que
                // tiene arriba y abajo, asi la foto queda a la misma distancia de los tres
                // bordes. La izquierda queda como estaba, que es donde arranca el nombre.
                Flow.Padding = new MarginPadding
                {
                    // La izquierda tampoco necesita los 20px heredados: son para centrar un
                    // icono suelto, y aca lo que arranca es texto. 10 le deja aire al nombre
                    // sin que la pastilla quede medio vacia con nombres largos.
                    Left = 10,
                    Right = avatar_inset,
                };
            }

            apiState = api.State.GetBoundCopy();
            apiState.BindValueChanged(onlineStateChanged, true);

            localUser = api.LocalUser.GetBoundCopy();
            localUser.BindValueChanged(userChanged, true);

            StateContainer = login;
        }

        private void userChanged(ValueChangedEvent<APIUser> user) => Schedule(() =>
        {
            usernameText.Text = user.NewValue.Username;
            avatar.User = user.NewValue;
            // Point the aura wrapper at the (now signed-in) local user so their
            // cosmetic renders behind the toolbar name. Text is set first so the
            // glow layer, which mirrors the text shape, rebuilds against it.
            usernameAura.SetUser(user.NewValue);
        });

        private void onlineStateChanged(ValueChangedEvent<APIState> state) => Schedule(() =>
        {
            failingIcon.FadeTo(state.NewValue == APIState.Failing || state.NewValue == APIState.RequiresSecondFactorAuth ? 1 : 0, 200, Easing.OutQuint);

            switch (state.NewValue)
            {
                case APIState.Connecting:
                    TooltipText = ToolbarStrings.Connecting;
                    spinner.Show();
                    break;

                case APIState.Failing:
                    TooltipText = ToolbarStrings.AttemptingToReconnect;
                    spinner.Show();
                    failingIcon.Icon = FontAwesome.Solid.ExclamationTriangle;
                    break;

                case APIState.RequiresSecondFactorAuth:
                    TooltipText = ToolbarStrings.VerificationRequired;
                    spinner.Show();
                    failingIcon.Icon = FontAwesome.Solid.Key;
                    break;

                case APIState.Offline:
                case APIState.Online:
                    TooltipText = string.Empty;
                    spinner.Hide();
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(state.NewValue));
            }
        });
    }
}
