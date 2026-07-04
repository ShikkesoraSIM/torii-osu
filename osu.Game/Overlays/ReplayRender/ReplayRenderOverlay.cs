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
        private FormTextBox skinTextBox;
        private FormCheckBox motionBlurCheckBox;
        private FormCheckBox shareCheckBox;
        private Container motionBlurRow;
        private RenderPillButton generateButton;
        private OsuSpriteText cooldownText;
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
                    Child = content = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                        Padding = new MarginPadding(BriefingTheme.SpacingXl),
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
                                HintText = "720p is available to everyone. Higher tiers are a supporter perk.",
                                Items = new[] { @"960x540", @"1280x720" },
                                NewFeatureId = NewFeatureRegistry.ReplayRender,
                            },
                            skinTextBox = new FormTextBox
                            {
                                Caption = "Skin",
                                HintText = "Exact o!rdr skin name — browse them at ordr.issou.best/skins. \"default\" is danser's built-in skin.",
                                PlaceholderText = @"default",
                            },
                            motionBlurRow = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Child = motionBlurCheckBox = new FormCheckBox
                                {
                                    Caption = "Motion blur",
                                    HintText = "Renders at 960fps and blends frames. Supporter perk.",
                                },
                            },
                            shareCheckBox = new FormCheckBox
                            {
                                Caption = "Share in the Torii Discord",
                                HintText = "When the video is ready, ToriiHalo posts it in the community renders channel.",
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
            });

            bodyFlow.Text = "Your replay gets rendered to a shareable MP4 by o!rdr's community renderers. "
                            + "It usually takes a few minutes depending on their queue — we'll notify you when it's ready. "
                            + "One video every 10 minutes per player.";

            // persistencia: el panel recuerda lo ultimo que elegiste.
            resolutionDropdown.Current.Value = configResolution.Value;
            skinTextBox.Current.Value = configSkin.Value;
            motionBlurCheckBox.Current.Value = configMotionBlur.Value;
            shareCheckBox.Current.Value = configShare.Value;

            resolutionDropdown.Current.BindValueChanged(v => configResolution.Value = v.NewValue);
            skinTextBox.Current.BindValueChanged(v => configSkin.Value = v.NewValue);
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

            refreshCooldown();
            Show();
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

            string skinName = string.IsNullOrWhiteSpace(skinTextBox.Current.Value) ? @"default" : skinTextBox.Current.Value.Trim();
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
                Text = $"Render queued on o!rdr — {scoreTitle}",
                CompletionText = $"Video ready! Click to watch — {scoreTitle}",
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

                    string progress = status.Progress ?? "Waiting in queue...";
                    notification.Text = $"{progress} — {scoreTitle}";
                    notification.State = ProgressNotificationState.Active;

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
