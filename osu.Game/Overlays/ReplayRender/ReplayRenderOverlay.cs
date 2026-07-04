// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Text.RegularExpressions;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.ToriiBriefing;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ReplayRender
{
    /// <summary>
    /// panel glass (estilo briefing) para renderear un replay a video via o!rdr.
    /// se abre desde el boton de la results screen. explica la feature, deja elegir
    /// resolucion/skin/compartir, muestra el cooldown (1 video cada 10 min, regla de
    /// o!rdr) y al generar cierra y sigue el progreso con una ProgressNotification.
    ///
    /// vive en OsuGame (no en la screen): el polling sobrevive a salir de la results
    /// screen y no hay riesgo de async-after-disposal contra drawables de screen.
    /// la key de o!rdr NUNCA esta en el cliente; hablamos solo con nuestro server.
    /// </summary>
    public partial class ReplayRenderOverlay : VisibilityContainer, IKeyBindingHandler<GlobalAction>
    {
        // los FormXX necesitan un OverlayColourProvider en scope (mismo truco que
        // ToriiFeatureHintOverlay, que tambien vive en OsuGame).
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Red);

        [Resolved]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private INotificationOverlay notifications { get; set; }

        [Resolved(canBeNull: true)]
        private OsuGame game { get; set; }

        private Container panel;
        private OsuTextFlowContainer bodyFlow;
        private FormDropdown<string> resolutionDropdown;
        private OrdrSkinSelector skinSelector;
        private FormCheckBox motionBlurCheckBox;
        private FormCheckBox shareCheckBox;
        private Container motionBlurRow;
        private RenderPillButton generateButton;
        private OsuSpriteText cooldownText;
        private FillFlowContainer recentSection;
        private FillFlowContainer recentFlow;
        private Sample samplePopIn;
        private Sample samplePopOut;

        private Bindable<string> configResolution;
        private Bindable<string> configSkin;
        private Bindable<bool> configMotionBlur;
        private Bindable<bool> configShare;

        private long scoreId;
        private string scoreTitle = "";
        private bool submitting;
        private int cooldownRemaining;

        private static readonly Regex progress_percent = new Regex(@"(\d{1,3})\s*%", RegexOptions.Compiled);

        public ReplayRenderOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;
        protected override bool OnClick(ClickEvent e) => true;
        protected override bool OnMouseDown(MouseDownEvent e) => true;

        [BackgroundDependencyLoader]
        private void load(AudioManager audio, OsuConfigManager config)
        {
            samplePopIn = audio?.Samples.Get(@"UI/overlay-big-pop-in");
            samplePopOut = audio?.Samples.Get(@"UI/overlay-big-pop-out");

            configResolution = config.GetBindable<string>(OsuSetting.ToriiRenderResolution);
            configSkin = config.GetBindable<string>(OsuSetting.ToriiRenderSkin);
            configMotionBlur = config.GetBindable<bool>(OsuSetting.ToriiRenderMotionBlur);
            configShare = config.GetBindable<bool>(OsuSetting.ToriiRenderShare);

            FillFlowContainer content;

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.6f,
                },
                panel = new BriefingGlass
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 560,
                    AutoSizeAxes = Axes.Y,
                    CornerSize = BriefingTheme.CornerLg,
                    SpecularStrength = 0.18f,
                    SpecularHeight = 70,
                    ShadowOpacity = 0.4f,
                    ShadowRadius = 30,
                    RelativeContentSize = Axes.X,
                    // el contenido va en un Container (posicion absoluta) asi el boton de
                    // cerrar flota en la esquina sup-derecha sin meterse en el FillFlow
                    // vertical (que crashea con anchors mezclados).
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
            };

            // regla FillFlow: flow vertical -> todos los hijos con anchor Top-Y
            // (TopLeft/TopCentre ok, nunca Centre-Y). ya nos mordio dos veces.
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
                            Text = "TORII × O!RDR",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                            Colour = BriefingTheme.AccentPink,
                        },
                    },
                },
                new OsuSpriteText
                {
                    Text = "Render replay to video",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                },
                bodyFlow = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
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
                            resolutionDropdown = new FormDropdown<string>
                            {
                                Caption = "Resolution",
                                HintText = "720p for everyone. 1080p comes with supporter.",
                                Items = new[] { @"960x540", @"1280x720" },
                                NewFeatureId = NewFeatureRegistry.ReplayRender,
                            },
                            skinSelector = new OrdrSkinSelector(),
                            motionBlurRow = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Child = motionBlurCheckBox = new FormCheckBox
                                {
                                    Caption = "Motion blur",
                                    HintText = "Smooths fast motion (960fps). Supporter only.",
                                },
                            },
                            shareCheckBox = new FormCheckBox
                            {
                                Caption = "Also share it in Discord",
                                HintText = "Off keeps it to you — the link still lands here and in your recent renders. "
                                           + "On also drops it in the community renders channel.",
                            },
                        },
                    },
                },
                cooldownText = new OsuSpriteText
                {
                    Text = "",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                    Colour = BriefingTheme.AccentAmber,
                    Alpha = 0,
                },
                generateButton = new RenderPillButton
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Width = 340,
                    Height = 44,
                    LabelText = "Generate video",
                    Action = submit,
                },
                // renders recientes: para reabrir un video aunque no lo hayas
                // compartido. oculto si no hay ninguno.
                recentSection = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = "YOUR RECENT RENDERS",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                            Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                        },
                        recentFlow = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 2),
                        },
                    },
                },
            });

            bodyFlow.Text = "o!rdr turns your replay into a video you can share or keep. It renders on "
                            + "volunteer machines, so it lands anywhere from a minute to a few, depending on how "
                            + "busy they are. You'll see it come together right here.";

            // persistencia: el panel recuerda lo ultimo que elegiste.
            resolutionDropdown.Current.Value = configResolution.Value;
            skinSelector.Current.Value = configSkin.Value;
            motionBlurCheckBox.Current.Value = configMotionBlur.Value;
            shareCheckBox.Current.Value = configShare.Value;

            resolutionDropdown.Current.BindValueChanged(v => configResolution.Value = v.NewValue);
            skinSelector.Current.BindValueChanged(v => configSkin.Value = v.NewValue);
            motionBlurCheckBox.Current.BindValueChanged(v => configMotionBlur.Value = v.NewValue);
            shareCheckBox.Current.BindValueChanged(v => configShare.Value = v.NewValue);
        }

        /// <summary>abre el panel para un score puntual (llamado desde la results screen).</summary>
        public void ShowFor(ScoreInfo score)
        {
            if (score == null)
                return;

            scoreId = score.OnlineID;
            scoreTitle = score.BeatmapInfo?.ToString() ?? "your replay";

            // opciones supporter: no mostrar lo que no se puede usar (el server igual lo rechaza).
            bool supporter = api?.LocalUser?.Value?.IsSupporter == true;
            var resolutions = supporter
                ? new[] { @"960x540", @"1280x720", @"1920x1080" }
                : new[] { @"960x540", @"1280x720" };
            resolutionDropdown.Items = resolutions;
            if (!supporter && configResolution.Value == @"1920x1080")
                resolutionDropdown.Current.Value = @"1280x720";
            motionBlurRow.Alpha = supporter ? 1 : 0;
            motionBlurRow.AlwaysPresent = false;

            skinSelector.Prime();
            refreshCooldown();
            loadRecentRenders();
            Show();
        }

        private void loadRecentRenders()
        {
            recentSection.Alpha = 0;

            var req = new GetMyReplayRendersRequest();
            req.Success += resp => Schedule(() =>
            {
                if (IsDisposed) return;

                recentFlow.Clear();
                int shown = 0;
                foreach (var r in resp.Renders)
                {
                    // solo los que tienen video ya listo son reabribles.
                    if (string.IsNullOrEmpty(r.VideoUrl))
                        continue;

                    string url = r.VideoUrl;
                    string title = string.IsNullOrWhiteSpace(r.BeatmapTitle) ? "replay" : r.BeatmapTitle;
                    recentFlow.Add(new RecentRenderRow(title, () => game?.OpenUrlExternally(url)));

                    if (++shown >= 4)
                        break;
                }

                recentSection.Alpha = shown > 0 ? 1 : 0;
            });
            req.Failure += _ => { };
            api?.Queue(req);
        }

        private void refreshCooldown()
        {
            cooldownRemaining = 0;
            updateCooldownDisplay();

            var req = new GetReplayRenderCooldownRequest();
            req.Success += resp => Schedule(() =>
            {
                if (IsDisposed) return;

                cooldownRemaining = resp.SecondsRemaining;
                updateCooldownDisplay();
                if (cooldownRemaining > 0)
                    tickCooldown();
            });
            req.Failure += _ => { }; // sin cooldown info no bloqueamos; el server decide igual
            api?.Queue(req);
        }

        private void tickCooldown()
        {
            Scheduler.AddDelayed(() =>
            {
                if (IsDisposed || State.Value != Visibility.Visible) return;

                cooldownRemaining = Math.Max(0, cooldownRemaining - 1);
                updateCooldownDisplay();
                if (cooldownRemaining > 0)
                    tickCooldown();
            }, 1000);
        }

        private void updateCooldownDisplay()
        {
            if (cooldownRemaining > 0)
            {
                cooldownText.Text = $"Next render available in {cooldownRemaining / 60}:{cooldownRemaining % 60:00}";
                cooldownText.FadeIn(120);
                generateButton.Enabled.Value = false;
            }
            else
            {
                cooldownText.FadeOut(120);
                generateButton.Enabled.Value = !submitting;
            }
        }

        private void submit()
        {
            if (submitting || scoreId <= 0)
                return;

            submitting = true;
            generateButton.Enabled.Value = false;

            string skinName = string.IsNullOrWhiteSpace(skinSelector.Current.Value) ? @"default" : skinSelector.Current.Value.Trim();
            bool supporter = api?.LocalUser?.Value?.IsSupporter == true;

            var req = new SubmitReplayRenderRequest(
                scoreId,
                resolutionDropdown.Current.Value,
                skinName,
                supporter && motionBlurCheckBox.Current.Value,
                shareCheckBox.Current.Value);

            req.Success += resp => Schedule(() =>
            {
                if (IsDisposed) return;

                submitting = false;
                Hide();
                beginTracking(resp.RenderId);
            });

            req.Failure += e => Schedule(() =>
            {
                if (IsDisposed) return;

                submitting = false;
                // el server manda el motivo posta (cooldown restante, mapa >15min, skin
                // inexistente...). lo mostramos y dejamos el panel abierto para ajustar.
                notifications?.Post(new SimpleErrorNotification
                {
                    Text = $"Couldn't start the render: {e.Message}",
                });
                refreshCooldown();
            });

            api?.Queue(req);
        }

        // ── seguimiento post-submit: ProgressNotification + poll cada 5s ──

        private void beginTracking(long renderId)
        {
            var notification = new ProgressNotification
            {
                Text = $"In o!rdr's queue — {scoreTitle}",
                CompletionText = $"Video's ready — click to watch: {scoreTitle}",
                State = ProgressNotificationState.Queued,
            };

            notifications?.Post(notification);
            pollRender(renderId, notification);
        }

        private void pollRender(long renderId, ProgressNotification notification)
        {
            Scheduler.AddDelayed(() =>
            {
                if (IsDisposed) return;

                // si el user cancelo el toast, dejamos de pollear (el render sigue en
                // o!rdr igual; si era compartido el bot lo postea cuando termine).
                if (notification.State == ProgressNotificationState.Cancelled)
                    return;

                var req = new GetReplayRenderStatusRequest(renderId);

                req.Success += status => Schedule(() =>
                {
                    if (IsDisposed) return;

                    if (status.IsDone)
                    {
                        string url = status.VideoUrl;
                        notification.CompletionClickAction = () =>
                        {
                            game?.OpenUrlExternally(url);
                            return true;
                        };
                        notification.State = ProgressNotificationState.Completed;
                        return;
                    }

                    if (status.IsFailed)
                    {
                        notification.State = ProgressNotificationState.Cancelled;
                        notifications?.Post(new SimpleErrorNotification
                        {
                            Text = $"Render failed: {status.ErrorMessage ?? "the render service gave up on this replay."}",
                        });
                        return;
                    }

                    // o!rdr manda "progress" como texto libre ("Rendering 45%", "Uploading...",
                    // "In queue"...). lo mostramos tal cual + el host que renderiza cuando hay.
                    string progress = string.IsNullOrWhiteSpace(status.Progress) ? "Waiting in the queue" : status.Progress.Trim();
                    bool inQueue = progress.Contains("queue", StringComparison.OrdinalIgnoreCase);

                    string host = string.IsNullOrWhiteSpace(status.Renderer) ? null : status.Renderer.Trim();
                    string line = host != null && !inQueue ? $"{progress} · {host}" : progress;
                    notification.Text = $"{line} — {scoreTitle}";
                    notification.State = inQueue ? ProgressNotificationState.Queued : ProgressNotificationState.Active;

                    var match = progress_percent.Match(progress);
                    if (match.Success && float.TryParse(match.Groups[1].Value, out float pct))
                        notification.Progress = Math.Clamp(pct / 100f, 0f, 1f);

                    pollRender(renderId, notification);
                });

                req.Failure += _ => Schedule(() =>
                {
                    if (IsDisposed) return;
                    // fallo transitorio de red/server: seguimos intentando.
                    pollRender(renderId, notification);
                });

                api?.Queue(req);
            }, 5000);
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
            panel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack)
                 .MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            samplePopOut?.Play();

            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            panel.ScaleTo(0.97f, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        /// <summary>boton circular de cerrar (X) en la esquina del panel.</summary>
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

        /// <summary>fila de un render pasado; click abre el video.</summary>
        private partial class RecentRenderRow : OsuClickableContainer
        {
            private readonly string title;
            private Box hover;

            public RecentRenderRow(string title, Action action)
            {
                this.title = title;
                Action = action;
                RelativeSizeAxes = Axes.X;
                Height = 24;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = BriefingTheme.CornerSm;

                Children = new Drawable[]
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
                        Padding = new MarginPadding { Left = BriefingTheme.SpacingSm, Right = BriefingTheme.SpacingSm },
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = FontAwesome.Solid.PlayCircle,
                                Size = new Vector2(11),
                                Colour = BriefingTheme.AccentPink,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = title,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                                Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                            },
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                hover.FadeIn(80);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hover.FadeOut(120);
                base.OnHoverLost(e);
            }
        }

        /// <summary>pill CTA estilo briefing, con enabled/disabled para el cooldown.</summary>
        private partial class RenderPillButton : OsuClickableContainer
        {
            public string LabelText { private get; init; } = "Generate video";

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
                        Colour = new Color4(220, 68, 92, 255),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = LabelText,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                    },
                };

                Enabled.BindValueChanged(e =>
                {
                    this.FadeTo(e.NewValue ? 1f : 0.4f, 120, Easing.OutQuint);
                }, true);
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (Enabled.Value)
                    background.FadeColour(new Color4(240, 92, 116, 255), 120, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(new Color4(220, 68, 92, 255), 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
