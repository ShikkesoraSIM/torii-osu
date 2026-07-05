// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
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
    /// selector de skin de o!rdr para el panel de render. lista scrolleable con muchas
    /// skins (las mas usadas sin buscar), busqueda en vivo, y una caja de preview a la
    /// derecha que carga la imagen de la skin al pasar el mouse por una fila. el ojito
    /// abre el preview full en el navegador. elegir una NO cierra la lista: la resalta.
    ///
    /// nota anti-crash: layout con GridContainer + ScrollContainer (no FillFlow con
    /// anchors mezclados); las imagenes se cargan best-effort (si fallan, caja vacia,
    /// nunca crash) y solo via el proxy de nuestro server (dominio confiable).
    /// </summary>
    public partial class OrdrSkinSelector : CompositeDrawable
    {
        /// <summary>id interno de la skin elegida (lo que se manda a o!rdr). "default" = danser.</summary>
        public Bindable<string> Current { get; } = new Bindable<string>("default");

        [Resolved]
        private IAPIProvider api { get; set; }

        [Resolved]
        private LargeTextureStore textures { get; set; }

        [Resolved(canBeNull: true)]
        private OsuGame game { get; set; }

        private FormTextBox search;
        private OsuSpriteText selectedLabel;
        private OsuScrollContainer scroll;
        private FillFlowContainer rowsFlow;
        private SkinPreviewBox previewBox;
        private ScheduledDelegate debounce;
        private ScheduledDelegate previewDebounce;
        private int searchToken;

        // paginacion (infinite scroll): vamos pidiendo paginas a medida que bajas.
        private string currentQuery = string.Empty;
        private int currentPage = 1;
        private bool loadingPage;
        private bool hasMore = true;

        private readonly List<SkinRow> rows = new List<SkinRow>();

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
                        HintText = "Search o!rdr's skins and pick from the list — hover one to preview it.",
                        PlaceholderText = "type a skin name…",
                    },
                    selectedLabel = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                        Colour = BriefingTheme.AccentPink,
                    },
                    // lista (izq) + preview (der). GridContainer evita cualquier tema de
                    // anchors del FillFlow, y le da altura fija a la lista scrolleable.
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 176,
                        Child = new GridContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ColumnDimensions = new[]
                            {
                                new Dimension(),
                                new Dimension(GridSizeMode.Absolute, BriefingTheme.SpacingSm),
                                new Dimension(GridSizeMode.Absolute, 168),
                            },
                            Content = new[]
                            {
                                new[]
                                {
                                    scroll = new OsuScrollContainer
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        ScrollbarVisible = true,
                                        Child = rowsFlow = new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, 2),
                                        },
                                    },
                                    Empty(),
                                    previewBox = new SkinPreviewBox
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        OpenInBrowser = url => game?.OpenUrlExternally(url),
                                        // ASYNC: baja+decodifica en background. si fuera sincronico
                                        // (textures.Get) bloquearia el update thread = FREEZE del juego.
                                        LoadTextureAsync = (url, ct) => textures.GetAsync(url, ct),
                                    },
                                },
                            },
                        },
                    },
                },
            };

            search.Current.BindValueChanged(e => scheduleSearch(e.NewValue));
            Current.BindValueChanged(e => updateSelectedLabel(e.NewValue), true);
        }

        /// <summary>cada vez que se abre el panel: limpia la busqueda y trae la primera pagina.</summary>
        public void Prime()
        {
            search.Current.Value = string.Empty;
            resetAndSearch(string.Empty);
        }

        private void updateSelectedLabel(string skinId)
        {
            selectedLabel.Text = string.IsNullOrWhiteSpace(skinId) || skinId == "default"
                ? "Using: Default (danser)"
                : $"Using: {skinId}";

            foreach (var row in rows)
                row.SetSelected(row.SkinId == skinId);
        }

        private void scheduleSearch(string query)
        {
            debounce?.Cancel();
            debounce = Scheduler.AddDelayed(() => resetAndSearch(query), 350);
        }

        /// <summary>arranca una busqueda nueva desde la pagina 1 (limpia la lista).</summary>
        private void resetAndSearch(string query)
        {
            searchToken++;
            currentQuery = query ?? string.Empty;
            currentPage = 1;
            hasMore = true;
            loadingPage = false;

            rowsFlow.Clear();
            rows.Clear();

            // "Default (danser)" siempre primero: no vive en el catalogo de skins subidas.
            addRow(new APIOrdrSkin { Skin = "default", Name = "Default (danser)", Author = "built-in" });

            loadPage(1);
        }

        /// <summary>infinite scroll: al acercarse al fondo, pide la pagina siguiente.</summary>
        protected override void Update()
        {
            base.Update();

            if (scroll == null || loadingPage || !hasMore)
                return;

            // margen para pedir antes de tocar fondo, asi no se corta el scroll.
            if (scroll.ScrollableExtent > 0 && scroll.Current >= scroll.ScrollableExtent - 80)
                loadPage(currentPage + 1);
        }

        private void loadPage(int page)
        {
            if (loadingPage || !hasMore)
                return;

            loadingPage = true;
            int token = searchToken;
            var req = new SearchOrdrSkinsRequest(currentQuery, page);

            req.Success += list => Schedule(() =>
            {
                if (IsDisposed || token != searchToken)
                    return;

                var skins = list?.Skins ?? new List<APIOrdrSkin>();
                int added = 0;

                foreach (var s in skins)
                {
                    if (string.IsNullOrEmpty(s.Skin) || s.Skin == "default")
                        continue;
                    addRow(s);
                    added++;
                }

                // si esta pagina no trajo nada, no hay mas (o!rdr devuelve vacio al pasarse).
                hasMore = added > 0;
                currentPage = page;
                loadingPage = false;

                foreach (var row in rows)
                    row.SetSelected(row.SkinId == Current.Value);
            });
            req.Failure += _ => Schedule(() =>
            {
                if (IsDisposed) return;
                loadingPage = false; // reintentara al seguir scrolleando
            });

            api?.Queue(req);
        }

        private void addRow(APIOrdrSkin skin)
        {
            var row = new SkinRow(skin)
            {
                OnPicked = id => pick(id),
                OnHovered = queuePreview,
                OnOpenBrowser = url => game?.OpenUrlExternally(url),
            };
            rows.Add(row);
            rowsFlow.Add(row);
        }

        /// <summary>debounce del preview: solo carga la imagen si el cursor DESCANSA en la fila
        /// ~400ms. asi barrer la lista con el mouse no dispara decenas de cargas (que laggean).</summary>
        private void queuePreview(APIOrdrSkin skin)
        {
            previewDebounce?.Cancel();
            previewDebounce = Scheduler.AddDelayed(() =>
            {
                if (!IsDisposed)
                    previewBox.ShowSkin(skin);
            }, 400);
        }

        private void pick(string skinId)
        {
            // elegir NO cierra la lista: solo actualiza el label + el resaltado.
            Current.Value = skinId;
        }

        /// <summary>fila clickeable de una skin: nombre + autor, con ojito en hover.</summary>
        private partial class SkinRow : CompositeDrawable
        {
            public string SkinId => skin.Skin;

            public System.Action<string> OnPicked;
            public System.Action<APIOrdrSkin> OnHovered;
            public System.Action<string> OnOpenBrowser;

            private readonly APIOrdrSkin skin;
            private Box background;
            private Box selectedTint;
            private EyeButton eye;

            public SkinRow(APIOrdrSkin skin)
            {
                this.skin = skin;
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
                    selectedTint = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = BriefingTheme.AccentPink.Opacity(0.16f),
                        Alpha = 0,
                    },
                    background = new Box
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
                        Padding = new MarginPadding { Left = BriefingTheme.SpacingSm, Right = 26 },
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = FontAwesome.Solid.PaintBrush,
                                Size = new Vector2(9),
                                Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = string.IsNullOrWhiteSpace(skin.Name) ? skin.Skin : skin.Name,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = string.IsNullOrWhiteSpace(skin.Author) ? "" : $"· {skin.Author}",
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                                Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                            },
                        },
                    },
                    // ojito: aparece en hover, abre el preview full en el navegador.
                    eye = new EyeButton(skin.HighRes)
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Margin = new MarginPadding { Right = BriefingTheme.SpacingXs },
                        OpenInBrowser = url => OnOpenBrowser?.Invoke(url),
                    },
                };
            }

            public void SetSelected(bool selected) => selectedTint.FadeTo(selected ? 1 : 0, 120);

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeIn(80);
                if (!string.IsNullOrEmpty(skin.HighRes))
                    eye.FadeIn(80);
                OnHovered?.Invoke(skin);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeOut(120);
                eye.FadeOut(120);
                base.OnHoverLost(e);
            }

            protected override bool OnClick(ClickEvent e)
            {
                OnPicked?.Invoke(skin.Skin);
                return true;
            }
        }

        /// <summary>ojito chico que abre una URL en el navegador. arranca oculto (lo muestra el padre en hover).</summary>
        private partial class EyeButton : OsuClickableContainer
        {
            public System.Action<string> OpenInBrowser;

            private string url;
            private SpriteIcon icon;

            public EyeButton(string url)
            {
                this.url = url;
                Size = new Vector2(20);
                Alpha = 0;
            }

            public void SetUrl(string newUrl) => url = newUrl;

            [BackgroundDependencyLoader]
            private void load()
            {
                Child = icon = new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = FontAwesome.Solid.Eye,
                    Size = new Vector2(12),
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                };

                Action = () =>
                {
                    if (!string.IsNullOrEmpty(url))
                        OpenInBrowser?.Invoke(url);
                };

                TooltipText = "View on o!rdr";
            }

            protected override bool OnHover(HoverEvent e)
            {
                icon.FadeColour(Color4.White, 100);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                icon.FadeColour(Color4.White.Opacity(BriefingTheme.InkSecondary), 150);
                base.OnHoverLost(e);
            }
        }

        /// <summary>caja de preview: muestra la imagen de la skin hovereada + su nombre.</summary>
        private partial class SkinPreviewBox : CompositeDrawable
        {
            public Action<string> OpenInBrowser;
            public Func<string, CancellationToken, Task<Texture>> LoadTextureAsync;

            private Sprite image;
            private OsuSpriteText nameText;
            private OsuSpriteText hint;
            private string currentSkinId;
            private CancellationTokenSource previewCancel;

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = BriefingTheme.CornerSm;

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black.Opacity(0.25f),
                    },
                    image = new Sprite
                    {
                        RelativeSizeAxes = Axes.Both,
                        FillMode = FillMode.Fit,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Alpha = 0,
                    },
                    hint = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "hover a skin",
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                        Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                    },
                    nameText = new OsuSpriteText
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Margin = new MarginPadding(4),
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                        Colour = Color4.White,
                        Alpha = 0,
                    },
                };
            }

            public void ShowSkin(APIOrdrSkin skin)
            {
                if (skin == null || skin.Skin == currentSkinId)
                    return;

                currentSkinId = skin.Skin;

                // cancelamos la carga anterior: si hovereaste otra, la de antes no se
                // aplica ni sigue ocupando la red.
                previewCancel?.Cancel();
                previewCancel = new CancellationTokenSource();

                nameText.Text = string.IsNullOrWhiteSpace(skin.Name) ? skin.Skin : skin.Name;
                nameText.FadeIn(120);

                // "default" no tiene preview subido; mostramos un hint.
                if (string.IsNullOrEmpty(skin.Preview) || skin.Skin == "default")
                {
                    image.Texture = null;
                    image.FadeOut(80);
                    hint.Text = skin.Skin == "default" ? "danser's built-in" : "no preview";
                    hint.FadeIn(80);
                    return;
                }

                image.FadeOut(80);
                hint.Text = "loading…";
                hint.FadeIn(80);

                string targetSkin = skin.Skin;
                var token = previewCancel.Token;

                // CARGA ASINCRONICA: baja+decodifica en un hilo de fondo, NUNCA en el
                // update thread. cuando termina, volvemos al update thread con Schedule.
                // si el usuario ya hovereo otra skin, el guard de currentSkinId la descarta.
                LoadTextureAsync?.Invoke(skin.Preview, token).ContinueWith(t => Schedule(() =>
                {
                    if (IsDisposed || currentSkinId != targetSkin || token.IsCancellationRequested)
                        return;

                    Texture tex = t.Status == TaskStatus.RanToCompletion ? t.GetResultSafely() : null;
                    image.Texture = tex;
                    image.FadeTo(tex != null ? 1 : 0, 150);

                    if (tex != null)
                        hint.FadeOut(80);
                    else
                    {
                        hint.Text = "preview unavailable";
                        hint.FadeIn(80);
                    }
                }), TaskContinuationOptions.None);
            }
        }
    }
}
