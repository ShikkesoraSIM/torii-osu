// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// torii: todos tus presets en un lugar. La lista a la izquierda, y del lado
    /// derecho lo que ese preset le pide al modelo, en castellano de andar por casa.
    /// Desde aca se renombran, se duplican y se borran, que es lo que uno quiere hacer
    /// cuando junto quince y ya no se acuerda cual era cual.
    /// </summary>
    public partial class MapperatorinatorPresetsScreen : OsuScreen
    {
        public override bool HideOverlaysOnEnter => false;

        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Pink);

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private INotificationOverlay? notifications { get; set; }

        [Resolved(canBeNull: true)]
        private IDialogOverlay? dialogOverlay { get; set; }

        private FillFlowContainer list = null!;
        private FillFlowContainer details = null!;
        private OsuSpriteText emptyText = null!;
        private LoadingLayer loading = null!;

        private readonly List<APIMapperatorinatorPreset> presets = new List<APIMapperatorinatorPreset>();
        private APIMapperatorinatorPreset? selected;

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background5,
                },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 50, Top = 40, Bottom = 80 },
                    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize), new Dimension() },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 4),
                                Padding = new MarginPadding { Bottom = 16 },
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Text = @"Mapperatorinator presets",
                                        Font = OsuFont.GetFont(size: 32, weight: FontWeight.SemiBold),
                                    },
                                    new OsuSpriteText
                                    {
                                        Text = @"saved combinations of generation settings. they live on your account, so they follow you to any machine.",
                                        Font = OsuFont.Default.With(size: 14),
                                        Colour = Colour4.White.Opacity(0.6f),
                                    },
                                },
                            },
                        },
                        new Drawable[]
                        {
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ColumnDimensions = new[] { new Dimension(GridSizeMode.Absolute, 340), new Dimension(GridSizeMode.Absolute, 20), new Dimension() },
                                Content = new[]
                                {
                                    new Drawable[]
                                    {
                                        new OsuScrollContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            ScrollbarOverlapsContent = false,
                                            Child = list = new FillFlowContainer
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Direction = FillDirection.Vertical,
                                                Spacing = new Vector2(0, 4),
                                            },
                                        },
                                        Empty(),
                                        new OsuScrollContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            ScrollbarOverlapsContent = false,
                                            Child = details = new FillFlowContainer
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Direction = FillDirection.Vertical,
                                                Spacing = new Vector2(0, 8),
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
                emptyText = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = @"No presets yet. Generate a map you like, then right-click it and save its settings.",
                    Font = OsuFont.Default.With(size: 16),
                    Colour = Colour4.White.Opacity(0.6f),
                    Alpha = 0,
                },
                loading = new LoadingLayer(true),
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            refresh();
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // se vuelve aca despues de editar un preset: la lista tiene que mostrar lo
            // que se acaba de guardar, no lo que habia antes de entrar.
            refresh();
        }

        private void refresh(int? selectId = null)
        {
            loading.Show();

            var request = new GetMapperatorinatorPresetsRequest();

            request.Success += response => Schedule(() =>
            {
                loading.Hide();
                presets.Clear();
                presets.AddRange(response.Presets);
                rebuildList();

                var pick = presets.FirstOrDefault(p => p.Id == selectId) ?? presets.FirstOrDefault(p => p.Id == selected?.Id) ?? presets.FirstOrDefault();
                select(pick);
            });

            request.Failure += e => Schedule(() =>
            {
                loading.Hide();
                notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't load your presets: {e.Message}" });
            });

            api.Queue(request);
        }

        private void rebuildList()
        {
            list.Clear();
            emptyText.Alpha = presets.Count == 0 ? 1 : 0;

            foreach (var preset in presets)
            {
                var entry = preset;

                list.Add(new PresetRow(entry, () => select(entry))
                {
                    Selected = entry.Id == selected?.Id,
                });
            }
        }

        private void select(APIMapperatorinatorPreset? preset)
        {
            selected = preset;

            foreach (var row in list.Children.OfType<PresetRow>())
                row.Selected = row.Preset.Id == preset?.Id;

            details.Clear();

            if (preset == null)
                return;

            var sidecar = MapperatorinatorSidecar.Deserialize(preset.Settings);

            details.Add(new OsuSpriteText
            {
                Text = preset.Name,
                Font = OsuFont.GetFont(size: 24, weight: FontWeight.SemiBold),
            });

            details.Add(new OsuSpriteText
            {
                Text = preset.OriginUsername != null
                    ? $"last saved {preset.UpdatedAt.LocalDateTime:d MMM yyyy, HH:mm} · taken from {preset.OriginUsername}"
                    : $"last saved {preset.UpdatedAt.LocalDateTime:d MMM yyyy, HH:mm}",
                Font = OsuFont.Default.With(size: 13),
                Colour = Colour4.White.Opacity(0.6f),
                Margin = new MarginPadding { Bottom = 6 },
            });

            // lo lindo de compartir un estilo: ver que a alguien le sirvio.
            if (preset.Forks > 0)
                details.Add(new ForkCount(preset));

            if (sidecar == null)
            {
                details.Add(new OsuSpriteText
                {
                    Text = @"These settings can't be read (saved by a newer version?).",
                    Font = OsuFont.Default.With(size: 14),
                    Colour = Colour4.Orange,
                });
            }
            else
            {
                foreach ((string label, string value) in describe(sidecar))
                {
                    details.Add(new OsuSpriteText
                    {
                        Text = $"{label}: {value}",
                        Font = OsuFont.Default.With(size: 15),
                    });
                }
            }

            if (sidecar != null && (sidecar.Descriptors.Count > 0 || sidecar.NegativeDescriptors.Count > 0))
            {
                details.Add(new OsuSpriteText
                {
                    Text = @"Styles it asks for (green) and avoids (red)",
                    Font = OsuFont.Default.With(size: 14),
                    Colour = Colour4.White.Opacity(0.6f),
                    Margin = new MarginPadding { Top = 8 },
                });

                var picker = new DescriptorPicker
                {
                    ReadOnly = true,
                    GamemodeId = sidecar.Gamemode,
                    ClickedWhileReadOnly = () => dialogOverlay?.Push(new ReadOnlyStylesDialog(() => edit(preset))),
                };

                picker.SetStates(sidecar.Descriptors, sidecar.NegativeDescriptors);
                details.Add(picker);
            }

            details.Add(new RoundedButton
            {
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Text = @"Edit these settings",
                Margin = new MarginPadding { Top = 14 },
                Action = () => edit(preset),
            });

            details.Add(new RoundedButton
            {
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Text = @"Rename",
                Action = () => dialogOverlay?.Push(new PresetNameDialog(preset.Name, name => rename(preset, name))),
            });

            details.Add(new RoundedButton
            {
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Text = @"Duplicate",
                Action = () => dialogOverlay?.Push(new PresetNameDialog($"{preset.Name} copy", name => save(name, preset.Settings))),
            });

            details.Add(new RoundedButton
            {
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Text = @"Delete",
                Action = () => dialogOverlay?.Push(new DeletePresetDialog(preset.Name, () => delete(preset))),
            });
        }

        /// <summary>What this preset actually asks the model for, in plain words.</summary>
        private static IEnumerable<(string label, string value)> describe(MapperatorinatorSidecar sidecar)
        {
            yield return (@"Model", sidecar.Model);
            yield return (@"Game mode", ((MapperatorinatorGamemode)sidecar.Gamemode).ToString());

            if (sidecar.Difficulty != null)
                yield return (@"Star rating", sidecar.Difficulty.Value.ToString(@"0.#", CultureInfo.InvariantCulture));

            if (sidecar.Year != null)
                yield return (@"Mapping year", sidecar.Year.Value.ToString(CultureInfo.InvariantCulture));

            if (sidecar.MapperId != null)
                yield return (@"Mapper to imitate", $"user {sidecar.MapperId.Value}");

            string stats = string.Join(@", ", new[]
            {
                sidecar.CircleSize != null ? $"CS {sidecar.CircleSize:0.#}" : null,
                sidecar.ApproachRate != null ? $"AR {sidecar.ApproachRate:0.#}" : null,
                sidecar.OverallDifficulty != null ? $"OD {sidecar.OverallDifficulty:0.#}" : null,
                sidecar.HpDrainRate != null ? $"HP {sidecar.HpDrainRate:0.#}" : null,
            }.Where(s => s != null));

            if (stats.Length > 0)
                yield return (@"Difficulty settings", stats);

            if (sidecar.Keycount != null)
                yield return (@"Keys", sidecar.Keycount.Value.ToString(CultureInfo.InvariantCulture));

            yield return (@"Hitsounds", sidecar.Hitsounded ? @"generated" : @"none");

            if (sidecar.SuperTiming)
                yield return (@"Timing", @"super timing (slower, for variable BPM)");

            // los estilos no van aca: se dibujan como pildoras, igual que en el generador.
        }

        /// <summary>Abre el generador cargado con este preset para guardarlo encima.</summary>
        private void edit(APIMapperatorinatorPreset preset) => this.Push(new MapperatorinatorScreen(preset));

        private void save(string name, string settings)
        {
            var request = new SaveMapperatorinatorPresetRequest(name, settings);

            request.Success += preset => Schedule(() =>
            {
                notifications?.Post(new SimpleNotification { Text = $"Saved \"{preset.Name}\"." });
                refresh(preset.Id);
            });

            request.Failure += e => Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't save it: {e.Message}" }));

            api.Queue(request);
        }

        /// <summary>Renaming is saving under the new name and dropping the old row.</summary>
        private void rename(APIMapperatorinatorPreset preset, string name)
        {
            if (name == preset.Name)
                return;

            var request = new SaveMapperatorinatorPresetRequest(name, preset.Settings);

            request.Success += created => Schedule(() =>
            {
                var drop = new DeleteMapperatorinatorPresetRequest(preset.Id);
                drop.Success += () => Schedule(() => refresh(created.Id));
                drop.Failure += e => Schedule(() =>
                {
                    Logger.Log($"[mapperatorinator] renamed but couldn't drop the old preset: {e.Message}");
                    refresh(created.Id);
                });

                api.Queue(drop);
            });

            request.Failure += e => Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't rename it: {e.Message}" }));

            api.Queue(request);
        }

        private void delete(APIMapperatorinatorPreset preset)
        {
            var request = new DeleteMapperatorinatorPresetRequest(preset.Id);

            request.Success += () => Schedule(() =>
            {
                if (selected?.Id == preset.Id)
                    selected = null;

                notifications?.Post(new SimpleNotification { Text = $"Deleted \"{preset.Name}\"." });
                refresh();
            });

            request.Failure += e => Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't delete it: {e.Message}" }));

            api.Queue(request);
        }

        /// <summary>
        /// "lo agarraron N personas", y al pasar el mouse, quienes. No es una metrica:
        /// es la parte que da gusto de que alguien haya usado tu estilo.
        /// </summary>
        private partial class ForkCount : CompositeDrawable, IHasTooltip
        {
            private readonly APIMapperatorinatorPreset preset;

            public LocalisableString TooltipText => preset.ForkedBy.Count > 0
                ? $"taken by {string.Join(", ", preset.ForkedBy)}"
                : @"taken by someone who's since left";

            public ForkCount(APIMapperatorinatorPreset preset)
            {
                this.preset = preset;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                AutoSizeAxes = Axes.Both;
                Margin = new MarginPadding { Bottom = 4 };

                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(5, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Icon = FontAwesome.Solid.Star,
                            Size = new Vector2(13),
                            Colour = Colour4.FromHex(@"ffd75e"),
                            // mismo anclaje que el texto: un flow horizontal con anclajes
                            // distintos entre hijos se cae.
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                        new OsuSpriteText
                        {
                            Text = preset.Forks == 1 ? @"1 person took this preset" : $"{preset.Forks} people took this preset",
                            Font = OsuFont.Default.With(size: 14, weight: FontWeight.SemiBold),
                            Colour = Colour4.FromHex(@"ffd75e"),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                    },
                };
            }
        }

        private partial class PresetRow : OsuClickableContainer
        {
            public readonly APIMapperatorinatorPreset Preset;

            private Box background = null!;

            private bool selected;

            public bool Selected
            {
                get => selected;
                set
                {
                    selected = value;
                    if (IsLoaded)
                        updateState();
                }
            }

            public PresetRow(APIMapperatorinatorPreset preset, Action select)
                : base(HoverSampleSet.Default)
            {
                Preset = preset;
                Action = select;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colours)
            {
                RelativeSizeAxes = Axes.X;
                Height = 42;
                Masking = true;
                CornerRadius = 6;

                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colours.Background4,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = 12 },
                        Text = Preset.Name,
                        Font = OsuFont.Default.With(size: 15, weight: FontWeight.SemiBold),
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                updateState();
            }

            private void updateState() => background.FadeColour(selected ? Colour4.FromHex(@"4aa8ff") : Colour4.FromHex(@"2a2a32"), 120, Easing.OutQuint);
        }

        /// <summary>
        /// Las pildoras aca son para mirar. Si alguien las clickea es porque quiere
        /// cambiar el preset, asi que se le ofrece justo eso en vez de no hacer nada.
        /// </summary>
        private partial class ReadOnlyStylesDialog : Overlays.Dialog.PopupDialog
        {
            public ReadOnlyStylesDialog(Action edit)
            {
                Icon = FontAwesome.Solid.Info;
                HeaderText = @"These are just a preview";
                BodyText = @"Looks like you want to change this preset. Use ""Edit these settings"" and pick the styles there.";

                Buttons = new Overlays.Dialog.PopupDialogButton[]
                {
                    new Overlays.Dialog.PopupDialogOkButton
                    {
                        Text = @"Take me there",
                        Action = edit,
                    },
                    new Overlays.Dialog.PopupDialogCancelButton
                    {
                        Text = @"Just looking",
                    },
                };
            }
        }

        private partial class DeletePresetDialog : Overlays.Dialog.PopupDialog
        {
            public DeletePresetDialog(string name, Action confirm)
            {
                Icon = FontAwesome.Solid.TrashAlt;
                HeaderText = $"Delete \"{name}\"?";
                BodyText = @"The maps you already made with it stay exactly as they are.";

                Buttons = new Overlays.Dialog.PopupDialogButton[]
                {
                    new Overlays.Dialog.PopupDialogDangerousButton
                    {
                        Text = @"Delete it",
                        Action = confirm,
                    },
                    new Overlays.Dialog.PopupDialogCancelButton
                    {
                        Text = @"Keep it",
                    },
                };
            }
        }
    }
}
