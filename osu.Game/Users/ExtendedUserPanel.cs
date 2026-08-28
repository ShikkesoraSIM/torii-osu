// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Users.Drawables;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Metadata;

namespace osu.Game.Users
{
    public abstract partial class ExtendedUserPanel : UserPanel
    {
        protected TextFlowContainer LastVisitMessage { get; private set; } = null!;

        private StatusIcon statusIcon = null!;
        private StatusText statusMessage = null!;

        [Resolved]
        private MetadataClient? metadata { get; set; }

        // Torii: client/platform badge. Null until a subclass opts in via CreateClientBadge()
        // and places it in its layout; UpdateClientName below keeps it in sync.
        private ToriiClientBadge? toriiClientBadge;

        private UserStatus? lastStatus;
        private UserActivity? lastActivity;
        private string? lastClientName;
        private DateTimeOffset? lastVisit;

        protected ExtendedUserPanel(APIUser user)
            : base(user)
        {
            lastVisit = user.LastVisit;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            BorderColour = ColourProvider?.Light1 ?? Colours.GreyVioletLighter;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updatePresence();

            // Colour should be applied immediately on first load.
            statusIcon.FinishTransforms();
        }

        protected override void Update()
        {
            base.Update();
            updatePresence();

            if (founderIdle)
            {
                // Una vuelta entera al circulo de color cada 6 segundos. Saturacion y
                // brillo fijos para que ningun tramo quede apagado contra el fondo de la
                // tarjeta: lo unico que cambia es el tono.
                statusIcon.Colour = Colour4.FromHSV((float)(Time.Current / 6000.0 % 1.0), 0.65f, 1f);
            }
        }

        protected Container CreateStatusIcon() => statusIcon = new StatusIcon();

        /// <summary>Torii: create the client/platform badge for a subclass to place in its layout.</summary>
        protected ToriiClientBadge CreateClientBadge() => toriiClientBadge = new ToriiClientBadge();

        protected FillFlowContainer CreateStatusMessage(bool rightAlignedChildren)
        {
            var statusContainer = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical
            };

            var alignment = rightAlignedChildren ? Anchor.CentreRight : Anchor.CentreLeft;

            statusContainer.Add(LastVisitMessage = new TextFlowContainer(t => t.Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold)).With(text =>
            {
                text.Anchor = alignment;
                text.Origin = alignment;
                text.AutoSizeAxes = Axes.Both;
                text.Alpha = 0;
            }));

            statusContainer.Add(statusMessage = new StatusText
            {
                Anchor = alignment,
                Origin = alignment,
                Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold)
            });

            return statusContainer;
        }

        /// <summary>
        /// El fundador. Cuando NO esta jugando de verdad igual se lo muestra presente,
        /// con su propio texto y un anillo que cicla de color.
        /// </summary>
        /// <remarks>
        /// Que este presente lo decide el SERVER (el spectator manda su presencia aunque
        /// no este conectado). Lo que se decide aca es solo como se dibuja, y por una
        /// razon concreta: un texto propio tendria que viajar como un
        /// <see cref="UserActivity"/> nuevo, y esa clase es parte del contrato messagepack
        /// del paquete compartido. Agregarle un tipo obliga a republicar el paquete y ata
        /// cliente y server a la misma version, o sea que cualquier cliente viejo se
        /// rompe. Dibujandolo de este lado el cable no cambia.
        ///
        /// "Presente pero sin actividad" es justamente la señal de que es la presencia
        /// sintetica: adentro del juego el cliente SIEMPRE manda una actividad (eligiendo
        /// mapa, jugando, en el editor). Asi que cuando esta jugando de verdad esto no se
        /// activa y la tarjeta se comporta como la de cualquiera, que es lo que se quiere.
        /// </remarks>
        private const int founder_user_id = 3;

        private bool founderIdle;

        private void updatePresence()
        {
            // TODO: we probably don't want to do this every frame.
            UserPresence? presence = metadata?.GetPresence(User.OnlineID);
            UserStatus status = presence?.Status ?? UserStatus.Offline;
            UserActivity? activity = presence?.Activity;
            // Torii: prefer the presence client name; fall back to the verified-name side channel.
            string? clientName = presence?.ClientName ?? metadata?.GetVerifiedClientName(User.OnlineID);

            if (status == lastStatus && activity == lastActivity && clientName == lastClientName)
                return;

            toriiClientBadge?.UpdateClientName(clientName);
            lastClientName = clientName;

            if (status == UserStatus.Offline && lastVisit != null)
            {
                LastVisitMessage.FadeTo(1);
                LastVisitMessage.Clear();
                LastVisitMessage.AddText(@"Last seen ");
                LastVisitMessage.AddText(new DrawableDate(lastVisit.Value, italic: false)
                {
                    Shadow = false
                });
            }
            else
                LastVisitMessage.FadeTo(0);

            founderIdle = User.OnlineID == founder_user_id && activity == null && status == UserStatus.Online;

            if (founderIdle)
            {
                statusMessage.Text = @"Watching my Grasshoppers";
                statusMessage.TooltipText = string.Empty;
            }
            else if (activity == null || status == UserStatus.Offline)
            {
                statusMessage.Text = status.GetLocalisableDescription();
                statusMessage.TooltipText = string.Empty;
            }
            else
            {
                statusMessage.Text = activity.GetStatus();
                statusMessage.TooltipText = activity.GetDetails() ?? string.Empty;
            }

            if (founderIdle)
            {
                // Sin FadeColour: el color lo maneja Update() cuadro a cuadro, y una
                // transicion encima se lo pisaria a mitad de camino.
                statusIcon.ClearTransforms(targetMember: nameof(statusIcon.Colour));
            }
            else if (activity == null || status != UserStatus.Online)
                statusIcon.FadeColour(status.GetAppropriateColour(Colours), 500, Easing.OutQuint);
            else
                statusIcon.FadeColour(activity.GetAppropriateColour(Colours), 500, Easing.OutQuint);

            lastStatus = status;
            lastActivity = activity;
            lastVisit = status != UserStatus.Offline ? DateTimeOffset.Now : lastVisit;
        }

        protected override bool OnHover(HoverEvent e)
        {
            BorderThickness = 2;
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            BorderThickness = 0;
            base.OnHoverLost(e);
        }

        private partial class StatusText : OsuSpriteText, IHasTooltip
        {
            public LocalisableString TooltipText { get; set; }
        }
    }
}
