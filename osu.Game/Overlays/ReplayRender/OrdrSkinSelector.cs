// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ReplayRender
{
    /// <summary>
    /// selector de skin de o!rdr para el panel de render. reemplaza el textbox pelado:
    /// escribís y te busca en el catálogo de o!rdr en vivo (via el proxy del server),
    /// y elegís de una lista en vez de adivinar el nombre exacto. sin nada escrito
    /// muestra las más usadas. <see cref="Current"/> guarda el id interno de la skin
    /// (lo que espera o!rdr), pero mostramos el nombre lindo.
    /// </summary>
    public partial class OrdrSkinSelector : CompositeDrawable
    {
        /// <summary>id interno de la skin elegida (lo que se manda a o!rdr). "default" = danser.</summary>
        public Bindable<string> Current { get; } = new Bindable<string>("default");

        [Resolved]
        private IAPIProvider api { get; set; }

        private FormTextBox search;
        private OsuSpriteText selectedLabel;
        private FillFlowContainer resultsFlow;
        private ScheduledDelegate debounce;
        private int searchToken;

        public OrdrSkinSelector()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                Children = new Drawable[]
                {
                    search = new FormTextBox
                    {
                        Caption = "Skin",
                        HintText = "Search o!rdr's skins and pick one — no need to remember exact names.",
                        PlaceholderText = "type a skin name…",
                    },
                    selectedLabel = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                        Colour = BriefingTheme.AccentPink,
                    },
                    resultsFlow = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 2),
                    },
                },
            };

            search.Current.BindValueChanged(e => scheduleSearch(e.NewValue));
            Current.BindValueChanged(e => updateSelectedLabel(e.NewValue), true);
        }

        /// <summary>arranca (o resetea) el selector cada vez que se abre el panel: limpia
        /// la busqueda y muestra las skins mas usadas para elegir sin escribir.</summary>
        public void Prime()
        {
            search.Current.Value = string.Empty;
            resultsFlow.Clear();
            runSearch(string.Empty);
        }

        private void updateSelectedLabel(string skinId)
        {
            selectedLabel.Text = string.IsNullOrWhiteSpace(skinId) || skinId == "default"
                ? "Using: Default (danser)"
                : $"Using: {skinId}";
        }

        private void scheduleSearch(string query)
        {
            debounce?.Cancel();
            debounce = Scheduler.AddDelayed(() => runSearch(query), 350);
        }

        private void runSearch(string query)
        {
            int token = ++searchToken;
            var req = new SearchOrdrSkinsRequest(query);

            req.Success += list => Schedule(() =>
            {
                if (IsDisposed || token != searchToken)
                    return;

                populate(list, string.IsNullOrWhiteSpace(query));
            });
            req.Failure += _ => { }; // sin resultados no rompemos nada; queda lo elegido

            api?.Queue(req);
        }

        private void populate(APIOrdrSkinList list, bool emptyQuery)
        {
            resultsFlow.Clear();

            var skins = list?.Skins ?? new System.Collections.Generic.List<APIOrdrSkin>();

            // sin búsqueda, mostramos las más usadas arriba (o!rdr las manda alfabéticas).
            if (emptyQuery)
                skins = skins.OrderByDescending(s => s.TimesUsed).ToList();

            // pin "Default (danser)" arriba de todo cuando no hay búsqueda: es la opción
            // que la mayoría quiere y no aparece en el catálogo de skins subidas.
            if (emptyQuery)
                resultsFlow.Add(new SkinRow("default", "Default (danser)", "built-in", () => pick("default")));

            foreach (var s in skins.Take(6))
            {
                string id = s.Skin;
                if (string.IsNullOrEmpty(id) || (emptyQuery && id == "default"))
                    continue;

                string sub = string.IsNullOrWhiteSpace(s.Author) ? "" : $"by {s.Author}";
                resultsFlow.Add(new SkinRow(id, string.IsNullOrWhiteSpace(s.Name) ? id : s.Name, sub, () => pick(id)));
            }
        }

        private void pick(string skinId)
        {
            Current.Value = skinId;
            // colapsamos la lista tras elegir; el label de arriba confirma la elección.
            search.Current.Value = string.Empty;
            resultsFlow.Clear();
        }

        /// <summary>fila clickeable de una skin en los resultados.</summary>
        private partial class SkinRow : CompositeDrawable, IHasTooltip
        {
            private readonly string title;
            private readonly string subtitle;
            private readonly Action action;
            private Box hover;

            public osu.Framework.Localisation.LocalisableString TooltipText { get; }

            public SkinRow(string id, string title, string subtitle, Action action)
            {
                this.title = title;
                this.subtitle = subtitle;
                this.action = action;
                TooltipText = id;
                RelativeSizeAxes = Axes.X;
                Height = 26;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = BriefingTheme.CornerSm;

                InternalChildren = new Drawable[]
                {
                    hover = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White.Opacity(0.06f),
                        Alpha = 0,
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Y,
                        AutoSizeAxes = Axes.X,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                        Padding = new MarginPadding { Left = BriefingTheme.SpacingSm },
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = FontAwesome.Solid.PaintBrush,
                                Size = new Vector2(10),
                                Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = title,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = subtitle,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                                Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                            },
                        },
                    },
                };
            }

            protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
            {
                hover.FadeIn(80);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
            {
                hover.FadeOut(120);
                base.OnHoverLost(e);
            }

            protected override bool OnClick(osu.Framework.Input.Events.ClickEvent e)
            {
                action?.Invoke();
                return true;
            }
        }
    }
}
