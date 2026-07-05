// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.ScoreNotes;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.ToriiBriefing;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ScoreNotes
{
    /// <summary>
    /// panel glass (estilo briefing) para agregarle una nota a una play propia:
    /// texto corto + imagen opcional (el server la achica a un thumbnail liviano).
    /// vive en OsuGame como los otros overlays torii. si el score ya tenia nota,
    /// la precarga para editar, y deja borrarla.
    /// </summary>
    public partial class ScoreNoteOverlay : VisibilityContainer, IKeyBindingHandler<GlobalAction>
    {
        private const int max_text_length = 280;
        private const long max_image_bytes = 6 * 1024 * 1024;

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Blue);

        [Resolved]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private INotificationOverlay notifications { get; set; }

        [Resolved(canBeNull: true)]
        private ScoreNoteStore noteStore { get; set; }

        private Container panel;
        private Container panelScaleWrapper;
        private FormTextBox noteText;
        private FormFileSelector fileSelector;
        private NotePillButton saveButton;
        private OsuSpriteText deleteHint;
        private Sample samplePopIn;
        private Sample samplePopOut;

        private long scoreId;
        private bool hadExistingNote;
        private bool submitting;

        public ScoreNoteOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => State.Value == Visibility.Visible;
        protected override bool OnClick(ClickEvent e) => State.Value == Visibility.Visible;
        protected override bool OnMouseDown(MouseDownEvent e) => State.Value == Visibility.Visible;

        protected override void Update()
        {
            base.Update();

            // fit-scale (mismo patron que el panel de render): el panel siempre entra
            // en pantalla, en ventana chica / UI scale alto se achica lo justo.
            if (panelScaleWrapper == null)
                return;

            Vector2 natural = panelScaleWrapper.DrawSize;
            if (natural.X <= 0 || natural.Y <= 0 || DrawHeight <= 0 || DrawWidth <= 0)
                return;

            float fit = Math.Min(1f, Math.Min(DrawHeight * 0.95f / natural.Y, DrawWidth * 0.95f / natural.X));
            panelScaleWrapper.Scale = new Vector2(fit);
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            samplePopIn = audio?.Samples.Get(@"UI/overlay-big-pop-in");
            samplePopOut = audio?.Samples.Get(@"UI/overlay-big-pop-out");

            FillFlowContainer content;

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.6f,
                },
                // PopoverContainer propio: el file selector abre su browser en un
                // popover y necesita un ancestro que lo hostee, garantizado aca.
                new PopoverContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = panelScaleWrapper = new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Child = panel = new BriefingGlass
                        {
                            Width = 520,
                            AutoSizeAxes = Axes.Y,
                            CornerSize = BriefingTheme.CornerLg,
                            SpecularStrength = 0.18f,
                            SpecularHeight = 70,
                            ShadowOpacity = 0.4f,
                            ShadowRadius = 30,
                            RelativeContentSize = Axes.X,
                            Child = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Children = new Drawable[]
                                {
                                    content = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                                        Padding = new MarginPadding(BriefingTheme.SpacingXl),
                                    },
                                    new CloseButton
                                    {
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                        Margin = new MarginPadding(BriefingTheme.SpacingMd),
                                        Action = Hide,
                                    },
                                },
                            },
                        },
                    },
                },
            };

            // regla fillflow: flow vertical -> todos los hijos Top-Y.
            content.AddRange(new Drawable[]
            {
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = FontAwesome.Solid.ToriiGate,
                            Size = new Vector2(BriefingTheme.TypeBody),
                            Colour = BriefingTheme.AccentPink,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "TORII",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                            Colour = BriefingTheme.AccentPink,
                        },
                    },
                },
                new OsuSpriteText
                {
                    Text = "Note this play",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                },
                new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    Text = "Leave a note on this score. Anyone browsing the leaderboard can hover it "
                           + "and read what happened — choked, one-handed it, whatever needs saying.",
                },
                new BriefingGlass
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    CornerSize = BriefingTheme.CornerMd,
                    SurfaceLift = 1.25f,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                        Padding = new MarginPadding(BriefingTheme.SpacingMd),
                        Children = new Drawable[]
                        {
                            noteText = new FormTextBox
                            {
                                Caption = "Note",
                                HintText = $"Up to {max_text_length} characters.",
                                PlaceholderText = "what happened in this play…",
                            },
                            fileSelector = new FormFileSelector(".jpg", ".jpeg", ".png", ".gif", ".webp")
                            {
                                Caption = "Image (optional)",
                                HintText = "Attached images get shrunk to a small thumbnail server-side.",
                                // el placeholder llena la linea de texto y de paso alinea el
                                // caption arriba (como el de Note); el drag & drop ya viene
                                // integrado en FormFileSelector (RegisterImportHandler).
                                PlaceholderText = "click to browse, or drag an image into this box",
                            },
                        },
                    },
                },
                deleteHint = new OsuSpriteText
                {
                    Text = "This play already has a note — saving replaces it. Right-click the save button to delete it.",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                    Colour = BriefingTheme.AccentAmber,
                    Alpha = 0,
                },
                saveButton = new NotePillButton
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Width = 340,
                    Height = 44,
                    LabelText = "Save note",
                    Action = submit,
                    RightClickAction = deleteNote,
                },
            });
        }

        /// <summary>abre el panel para un score propio (desde la results screen).</summary>
        public void ShowFor(ScoreInfo score)
        {
            if (score == null || score.OnlineID <= 0)
                return;

            scoreId = score.OnlineID;
            hadExistingNote = false;
            noteText.Current.Value = string.Empty;
            fileSelector.Current.Value = null;
            deleteHint.Alpha = 0;

            // precargar la nota existente (si hay) para editarla.
            noteStore?.Lookup(scoreId, n =>
            {
                if (IsDisposed || scoreId != n.ScoreId) return;

                hadExistingNote = true;
                noteText.Current.Value = n.Text;
                deleteHint.Alpha = 1;
            });

            Show();
        }

        private void submit()
        {
            if (submitting || scoreId <= 0)
                return;

            string text = (noteText.Current.Value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Write something first — the note can't be empty." });
                return;
            }

            if (text.Length > max_text_length)
                text = text.Substring(0, max_text_length);

            var file = fileSelector.Current.Value;

            submitting = true;
            saveButton.Enabled.Value = false;

            long targetScore = scoreId;

            // leer el archivo en background (puede ser de varios MB; nada de IO en el
            // update thread) y despues encolar el request desde el scheduler.
            Task.Run(() =>
            {
                byte[] bytes = null;
                string error = null;

                if (file != null)
                {
                    try
                    {
                        if (file.Length > max_image_bytes)
                            error = "That image is too big (max 6MB).";
                        else
                            bytes = File.ReadAllBytes(file.FullName);
                    }
                    catch (Exception)
                    {
                        error = "Couldn't read that image file.";
                    }
                }

                Schedule(() =>
                {
                    if (IsDisposed) return;

                    if (error != null)
                    {
                        submitting = false;
                        saveButton.Enabled.Value = true;
                        notifications?.Post(new SimpleErrorNotification { Text = error });
                        return;
                    }

                    sendRequest(targetScore, text, bytes);
                });
            });
        }

        private void sendRequest(long targetScore, string text, byte[] imageBytes)
        {
            var req = new SubmitScoreNoteRequest(targetScore, text, imageBytes);

            req.Success += resp => Schedule(() =>
            {
                if (IsDisposed) return;

                submitting = false;
                saveButton.Enabled.Value = true;

                noteStore?.SetLocal(resp);
                notifications?.Post(new SimpleNotification
                {
                    Text = "Note saved. It'll show up on the leaderboard.",
                    Icon = FontAwesome.Solid.CommentDots,
                });
                Hide();
            });

            req.Failure += e => Schedule(() =>
            {
                if (IsDisposed) return;

                submitting = false;
                saveButton.Enabled.Value = true;
                notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't save the note: {e.Message}" });
            });

            api?.Queue(req);
        }

        private void deleteNote()
        {
            if (submitting || scoreId <= 0 || !hadExistingNote)
                return;

            long targetScore = scoreId;
            var req = new DeleteScoreNoteRequest(targetScore);

            req.Success += () => Schedule(() =>
            {
                if (IsDisposed) return;

                noteStore?.RemoveLocal(targetScore);
                notifications?.Post(new SimpleNotification
                {
                    Text = "Note deleted.",
                    Icon = FontAwesome.Solid.TrashAlt,
                });
                Hide();
            });
            req.Failure += e => Schedule(() =>
            {
                if (IsDisposed) return;
                notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't delete the note: {e.Message}" });
            });

            api?.Queue(req);
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat)
                return false;

            if (e.Action == GlobalAction.Back && State.Value == Visibility.Visible)
            {
                Hide();
                return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        protected override void PopIn()
        {
            samplePopIn?.Play();
            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            panel.MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            samplePopOut?.Play();
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            panel.MoveToY(10, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        /// <summary>pill CTA azul; click derecho = borrar (cuando ya habia nota).</summary>
        private partial class NotePillButton : OsuClickableContainer
        {
            public string LabelText { private get; init; } = "Save note";
            public Action RightClickAction;

            private Box background;

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = 22;
                CornerExponent = BriefingTheme.SquircleExponent;

                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(64, 122, 214, 255),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = LabelText,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                    },
                };

                Enabled.BindValueChanged(e => this.FadeTo(e.NewValue ? 1f : 0.4f, 120, Easing.OutQuint), true);
            }

            protected override bool OnClick(ClickEvent e)
            {
                if (e.Button == osuTK.Input.MouseButton.Right)
                {
                    RightClickAction?.Invoke();
                    return true;
                }

                return base.OnClick(e);
            }

            protected override bool OnMouseDown(MouseDownEvent e) => true;

            protected override bool OnHover(HoverEvent e)
            {
                if (Enabled.Value)
                    background.FadeColour(new Color4(88, 146, 235, 255), 120, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(new Color4(64, 122, 214, 255), 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }

        /// <summary>boton circular de cerrar (X), igual al del panel de render.</summary>
        private partial class CloseButton : OsuClickableContainer
        {
            private Box background;
            private SpriteIcon icon;

            public CloseButton()
            {
                Size = new Vector2(28);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = 14;

                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White.Opacity(0.08f),
                    },
                    icon = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Solid.Times,
                        Size = new Vector2(12),
                        Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(Color4.White.Opacity(0.16f), 120, Easing.OutQuint);
                icon.FadeColour(Color4.White, 120, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(Color4.White.Opacity(0.08f), 200, Easing.OutQuint);
                icon.FadeColour(Color4.White.Opacity(BriefingTheme.InkSecondary), 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
