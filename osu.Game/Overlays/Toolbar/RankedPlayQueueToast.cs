// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// El cartelito que asoma debajo de la pildora: "Fulano is now in Queue!".
    /// </summary>
    /// <remarks>
    /// Existe para que entrar a la cola sea un evento social y no un acto solitario. Si
    /// alguien esta en el menu principal con el toolbar a la vista y ve pasar esto, tiene
    /// un motivo concreto para entrar EN ESE MOMENTO, que es justo lo que le falta a una
    /// cola vacia.
    ///
    /// A proposito NO es una notificacion del sistema: no se apila, no deja rastro y no
    /// hay que cerrarla. Aparece, se lee, se va. Si el toolbar esta escondido no se
    /// muestra nada, porque seria un cartel flotando en el aire.
    /// </remarks>
    public partial class RankedPlayQueueToast : CompositeDrawable
    {
        [Resolved]
        private UserLookupCache? userLookup { get; set; }

        private Container box = null!;
        private OsuSpriteText label = null!;

        private CancellationTokenSource? pendingLookup;

        public RankedPlayQueueToast()
        {
            AutoSizeAxes = Axes.Both;
            // Vive con Alpha 0 casi todo el tiempo; sin esto no corre Update y las
            // animaciones agendadas no salen nunca.
            AlwaysPresent = true;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = box = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 8,
                CornerExponent = 2.4f,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(24, 18, 12, 235),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(255, 146, 43, 255).Opacity(0.12f),
                        Blending = BlendingParameters.Additive,
                    },
                    label = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Margin = new MarginPadding { Horizontal = 10, Vertical = 6 },
                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                        Colour = Color4.White,
                    },
                },
            };
        }

        /// <summary>
        /// Anuncia por id, resolviendo los nombres contra el cache de usuarios.
        /// </summary>
        public void AnnounceJoined(int[] userIds)
        {
            if (userIds.Length == 0)
                return;

            pendingLookup?.Cancel();
            pendingLookup = new CancellationTokenSource();
            var token = pendingLookup.Token;

            if (userLookup == null)
            {
                // Sin cache de usuarios (tests, o arranque temprano) igual se avisa:
                // "alguien entro" es mejor que silencio.
                show(userIds.Length == 1 ? "Someone" : $"{userIds.Length} players", userIds.Length);
                return;
            }

            // await y no ContinueWith + .Result: Task.Result esta prohibido en el
            // proyecto justamente porque bloquea el hilo y deadlockea.
            lookupAndShow(userIds, token);
        }

        private async void lookupAndShow(int[] userIds, CancellationToken token)
        {
            string[] names;

            try
            {
                var users = await userLookup!.GetUsersAsync(userIds, token).ConfigureAwait(false);

                names = users?
                        .Where(u => u != null && !string.IsNullOrEmpty(u.Username))
                        .Select(u => u!.Username)
                        .ToArray() ?? [];
            }
            catch (Exception)
            {
                // Si no se pueden resolver los nombres se avisa igual sin ellos: el
                // punto del cartel es que sepas que hay alguien, no quien exactamente.
                names = [];
            }

            if (token.IsCancellationRequested)
                return;

            if (names.Length == 0)
                names = [userIds.Length == 1 ? "Someone" : $"{userIds.Length} players"];

            Schedule(() =>
            {
                if (!token.IsCancellationRequested)
                    AnnounceJoined(names);
            });
        }

        /// <summary>
        /// Anuncia con nombres ya resueltos.
        /// </summary>
        public void AnnounceJoined(string[] names)
        {
            if (names.Length == 0)
                return;

            show(describe(names), names.Length);
        }

        /// <summary>
        /// "Fulano", "Fulano and Mengano", "Fulano and 3 others". Nombrar a la gente es
        /// medio el punto: "2 players joined" no le da a nadie ganas de entrar.
        /// </summary>
        private static string describe(string[] names) => names.Length switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]} and {names.Length - 1} others",
        };

        private void show(string who, int count)
        {
            label.Text = count == 1 ? $"{who} is now in Queue!" : $"{who} are now in Queue!";

            ClearTransforms();
            box.ClearTransforms();

            // Baja, se queda, y se va sola. Lo suficiente para leerla de reojo.
            this.FadeIn(160, Easing.OutQuint);
            box.MoveToY(-4).MoveToY(0, 260, Easing.OutBack);
            box.ScaleTo(0.92f).ScaleTo(1f, 260, Easing.OutBack);

            this.Delay(3200).FadeOut(280, Easing.OutQuint);
        }

        protected override void Dispose(bool isDisposing)
        {
            pendingLookup?.Cancel();
            base.Dispose(isDisposing);
        }
    }
}
