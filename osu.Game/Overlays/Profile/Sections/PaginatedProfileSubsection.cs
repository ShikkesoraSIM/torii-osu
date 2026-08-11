// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Graphics.Containers;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osuTK;

namespace osu.Game.Overlays.Profile.Sections
{
    public abstract partial class PaginatedProfileSubsection<TModel> : ProfileSubsection
    {
        /// <summary>
        /// The number of items displayed per page.
        /// </summary>
        protected virtual int ItemsPerPage => 50;

        /// <summary>
        /// The number of items displayed initially.
        /// </summary>
        protected virtual int InitialItemsCount => 5;

        /// <summary>
        /// torii: si esta subseccion pide sus datos apenas se abre el perfil, o recien cuando el
        /// jugador scrollea.
        ///
        /// Abrir un perfil disparaba QUINCE requests de una: las tres de Ranks, las cuatro de
        /// Historical, las SIETE de Beatmaps (favourite, graveyard, guest, loved, nominated,
        /// pending, ranked) y kudosu. Y la cola de la API los corria de a uno, asi que a 190 ms
        /// por viaje eran casi 3 segundos hasta ver las top plays, que son lo unico que la
        /// mayoria mira. Medido en el log de red del cliente.
        ///
        /// Las que el jugador ve primero cargan al toque; el resto espera a que scrollee. Si
        /// nunca baja, nunca se piden.
        /// </summary>
        protected virtual bool LoadWithoutScrolling => false;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private bool waitingForScroll;
        private ScrollContainer<Drawable>? parentScroll;

        protected PaginationParameters? CurrentPage { get; private set; }

        protected ReverseChildIDFillFlowContainer<Drawable> ItemsContainer { get; private set; } = null!;

        private APIRequest<List<TModel>>? retrievalRequest;
        private CancellationTokenSource? loadCancellation;

        private ShowMoreButton moreButton = null!;
        private OsuSpriteText missing = null!;
        private readonly LocalisableString? missingText;

        protected PaginatedProfileSubsection(Bindable<UserProfileData?> user, LocalisableString? headerText = null, LocalisableString? missingText = null)
            : base(user, headerText, CounterVisibilityState.AlwaysVisible)
        {
            this.missingText = missingText;
        }

        protected override Drawable CreateContent() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Children = new Drawable[]
            {
                // reverse ID flow is required for correct Z-ordering of the items (last item should be front-most).
                // particularly important in PaginatedBeatmapContainer, as it uses beatmap cards, which have expandable overhanging content.
                ItemsContainer = new ReverseChildIDFillFlowContainer<Drawable>
                {
                    AutoSizeAxes = Axes.Y,
                    RelativeSizeAxes = Axes.X,
                    Spacing = new Vector2(0, 2),
                    // ensure the container and its contents are in front of the "more" button.
                    Depth = float.MinValue
                },
                moreButton = new ShowMoreButton
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Alpha = 0,
                    Margin = new MarginPadding { Top = 10 },
                    Action = showMore,
                },
                missing = new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 15),
                    Text = missingText ?? string.Empty,
                    Alpha = 0,
                }
            }
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();
            User.BindValueChanged(onUserChanged, true);
        }

        private void onUserChanged(ValueChangedEvent<UserProfileData?> e)
        {
            loadCancellation?.Cancel();
            retrievalRequest?.Cancel();

            CurrentPage = null;
            ItemsContainer.Clear();

            waitingForScroll = false;

            if (e.NewValue?.User != null)
            {
                SetCount(GetCount(e.NewValue.User));

                if (LoadWithoutScrolling)
                    showMore();
                else
                    // Las que se difieren muestran su titulo con el contador, que sale del
                    // payload del usuario y no de este request, asi que no quedan en blanco.
                    // OJO: eso vale solo para las que tienen headerText. Las que se construyen
                    // sin el (Recent Activity, Kudosu) no tienen nada que mostrar mientras
                    // esperan, por eso esas cargan de una.
                    waitingForScroll = true;
            }
        }

        protected override void Update()
        {
            base.Update();

            if (!waitingForScroll)
                return;

            parentScroll ??= this.FindClosestParent<ScrollContainer<Drawable>>();

            // Se usa el scroll y no "esta en pantalla" a proposito: al abrir el perfil las
            // secciones todavia estan vacias, asi que miden un titulo cada una y entran TODAS
            // en pantalla. Con visibilidad se cargarian igual todas. Que el jugador haya
            // scrolleado es la unica señal honesta de que le interesa lo de mas abajo.
            if (parentScroll == null || parentScroll.Current <= 0)
                return;

            waitingForScroll = false;
            showMore();
        }

        private void showMore()
        {
            if (User.Value == null)
                return;

            loadCancellation = new CancellationTokenSource();

            CurrentPage = CurrentPage?.TakeNext(ItemsPerPage) ?? new PaginationParameters(InitialItemsCount);

            retrievalRequest = CreateRequest(User.Value, new PaginationParameters(CurrentPage.Value.Offset, CurrentPage.Value.Limit + 1));
            retrievalRequest.Success += items => UpdateItems(items, loadCancellation);

            api.Queue(retrievalRequest);
        }

        protected virtual void UpdateItems(List<TModel> items, CancellationTokenSource cancellationTokenSource) => Schedule(() =>
        {
            if (!items.Any() && CurrentPage?.Offset == 0)
            {
                moreButton.Hide();
                moreButton.IsLoading = false;

                if (missingText.HasValue)
                    missing.Show();

                return;
            }

            bool hasMore = items.Count > CurrentPage?.Limit;

            if (hasMore)
                items.RemoveAt(items.Count - 1);

            OnItemsReceived(items);

            LoadComponentsAsync(items.Select(CreateDrawableItem).Where(d => d != null).Cast<Drawable>(), drawables =>
            {
                missing.Hide();

                moreButton.FadeTo(hasMore ? 1 : 0);
                moreButton.IsLoading = false;

                ItemsContainer.AddRange(drawables);
            }, cancellationTokenSource.Token);
        });

        protected virtual int GetCount(APIUser user) => 0;

        protected virtual void OnItemsReceived(List<TModel> items)
        {
        }

        protected abstract APIRequest<List<TModel>> CreateRequest(UserProfileData user, PaginationParameters pagination);

        protected abstract Drawable? CreateDrawableItem(TModel model);

        protected override void Dispose(bool isDisposing)
        {
            retrievalRequest?.Cancel();
            loadCancellation?.Cancel();
            base.Dispose(isDisposing);
        }
    }
}
