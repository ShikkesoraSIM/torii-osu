// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Server;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// Expanded panel surfaced from <see cref="ToriiServerPulseButton"/>.
    /// Anchored below the button, carries four pages of live activity
    /// data the user can swipe / arrow-key / auto-rotate through:
    ///
    ///   Page 0 — Overview     · the at-a-glance stats (3 numbers + sparkline)
    ///   Page 1 — Hot Maps     · top 5 most-played beatmaps right now (covers + plays)
    ///   Page 2 — Live Plays   · most-recent in-flight plays (avatar + map + Xs ago)
    ///   Page 3 — Mode Split   · per-ruleset breakdown of who's playing what
    ///
    /// Page transitions
    /// ----------------
    /// Three input vectors all funnel through <see cref="goToPage"/>:
    ///   - Click the ‹ / › arrows in the page strip.
    ///   - Click on a dot in the page-indicator row.
    ///   - Auto-advance every <see cref="auto_scroll_interval_ms"/> when
    ///     the user is not hovering the popover (so reading time isn't
    ///     interrupted).
    ///   - Drag horizontally on the page area to swipe.
    ///
    /// Position is animated with a brief OutQuint slide so the page
    /// motion reads as deliberate. Last-viewed page persists across
    /// popover close/open via the provider's
    /// <see cref="ToriiServerPulseProvider.LastViewedCarouselPage"/>
    /// bindable — so the user's chosen page is what they see next time.
    ///
    /// Click-outside dismissal
    /// -----------------------
    /// We don't capture global click-outside ourselves (would interfere
    /// with the rest of the toolbar). Dismiss by clicking the button
    /// again or hitting Escape (handled in OnKeyDown).
    /// </summary>
    public partial class ToriiServerPulsePopover : VisibilityContainer
    {
        // ─── Brand palette ───────────────────────────────────────────
        // Vermillion mirrors ToriiClientBadge + cursor preview pill.
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);
        private static readonly Color4 torii_red_dim = new Color4(204, 41, 41, 110);
        private static readonly Color4 panel_bg = new Color4(14, 12, 18, 245);
        private static readonly Color4 muted_white = new Color4(255, 255, 255, 130);

        // ─── Geometry ────────────────────────────────────────────────
        private const float panel_width = 380f;
        private const float panel_corner_radius = 16f;
        private const float page_height = 280f;

        // Horizontal padding on the body's vertical FillFlow. Header,
        // tab strip, and footer all live INSIDE this padded flow. The
        // page carousel ALSO lives inside it, so each page's effective
        // visible width is panel_width - 2 * panel_horizontal_padding.
        // page_step is what the carousel slides by per page — making
        // this match the visible width is what stops the right edge of
        // each page from being clipped by the body's padding.
        private const float panel_horizontal_padding = 18f;
        private const float page_step = panel_width - 2 * panel_horizontal_padding;

        // Tab strip / arrow / dot row sizing.
        private const float tab_strip_height = 26f;

        // ─── Pages ───────────────────────────────────────────────────
        private const int page_count = 4;
        private const int page_overview = 0;
        private const int page_hot_maps = 1;
        private const int page_live_plays = 2;
        private const int page_mode_split = 3;

        // ─── Auto-scroll cadence ─────────────────────────────────────
        // 10s feels right: long enough to read a page comfortably, short
        // enough that all four cycle within ~40s so a casual viewer sees
        // everything without having to interact. Pause-on-hover keeps it
        // out of the user's way when they're actually reading.
        private const int auto_scroll_interval_ms = 10_000;

        /// <summary>
        /// The button this popover is anchored to. Set by the button
        /// when it instantiates the popover. Used solely for relative
        /// positioning — we don't read any other state off it.
        /// </summary>
        public Drawable? AnchoredAt { get; set; }

        [Resolved(canBeNull: true)]
        private ToriiServerPulseProvider? pulse { get; set; }

        // Visual state owned by us
        private Container body = null!;

        private StatusPip statusPip = null!;
        private OsuSpriteText footerText = null!;

        // Carousel scaffold
        private Container pagesViewport = null!;       // masked, fixed size
        private Container pagesStrip = null!;          // moves left/right; holds 4 pages side by side
        private OsuSpriteText pageTitleText = null!;
        private FillFlowContainer pageDotsFlow = null!;
        private CarouselArrow leftArrow = null!;
        private CarouselArrow rightArrow = null!;

        // Pages (each has its own internal state + bindable subscriptions)
        private OverviewPage overviewPage = null!;
        private HotMapsPage hotMapsPage = null!;
        private LivePlaysPage livePlaysPage = null!;
        private ModeSplitPage modeSplitPage = null!;

        // Carousel runtime state
        private int currentPage;
        private bool isHoveringPopover;
        private ScheduledDelegate? autoAdvanceDelegate;
        private ScheduledDelegate? footerTickDelegate;

        // Swipe / drag state
        private float dragStartStripX;
        private bool isDragging;

        public ToriiServerPulsePopover()
        {
            // Auto-size off so the slide animation has stable geometry.
            Width = panel_width;
            AutoSizeAxes = Axes.Y;
            Alpha = 0;
            AlwaysPresent = true;

            // Position is finalised in Update() relative to AnchoredAt.
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopCentre;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Construct pages first so the carousel scaffold has children
            // ready to position.
            overviewPage = new OverviewPage();
            hotMapsPage = new HotMapsPage();
            livePlaysPage = new LivePlaysPage();
            modeSplitPage = new ModeSplitPage();

            Child = body = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = panel_corner_radius,
                CornerExponent = 2.6f,
                MaskingSmoothness = 1.6f,
                BorderThickness = 1f,
                BorderColour = torii_red.Opacity(0.5f),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Radius = 22,
                    Roundness = 16,
                    Colour = new Color4(8, 0, 4, 200),
                    Offset = new Vector2(0, 6),
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = panel_bg,
                    },
                    // Faint vermillion gradient wash from top so the
                    // panel feels "branded" without competing with the
                    // content for attention.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Blending = BlendingParameters.Additive,
                        Colour = ColourInfo.GradientVertical(
                            torii_red.Opacity(0.10f),
                            torii_red.Opacity(0.0f)),
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Top = 14, Bottom = 14, Horizontal = panel_horizontal_padding },
                        Spacing = new Vector2(0, 10),
                        Children = new Drawable[]
                        {
                            buildHeader(),
                            buildTabStrip(),
                            buildPagesViewport(),
                            buildFooter(),
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Tick the footer every second so the "updated Xs ago"
            // freshness counter increments smoothly while visible.
            footerTickDelegate = Scheduler.AddDelayed(() =>
            {
                if (State.Value == Visibility.Visible)
                    updateFooter();
            }, 1000, true);

            // Bindings AFTER children have loaded — see comment in earlier
            // version of this file for why immediate-fire BindValueChanged
            // from BackgroundDependencyLoader was crashing the popover.
            if (pulse != null)
            {
                // Overview page (3 stats + sparkline)
                pulse.CurrentlyPlaying.BindValueChanged(v => overviewPage.SetPlaying(v.NewValue), true);
                pulse.PlaysLastMinute.BindValueChanged(v => overviewPage.SetPlaysPerMinute(v.NewValue), true);
                pulse.OnlineUsers.BindValueChanged(v => overviewPage.SetOnline(v.NewValue), true);
                pulse.Sparkline.BindValueChanged(v => overviewPage.SetSparkline(v.NewValue), true);
                pulse.TopMap.BindValueChanged(v => overviewPage.SetTopMap(v.NewValue), true);

                // Hot maps page (top 5)
                pulse.TopMaps.BindValueChanged(v => hotMapsPage.SetMaps(v.NewValue), true);

                // Live plays page
                pulse.RecentPlays.BindValueChanged(v => livePlaysPage.SetPlays(v.NewValue), true);

                // Mode split page
                pulse.CurrentlyPlaying.BindValueChanged(v => modeSplitPage.SetTotal(v.NewValue), true);
                pulse.ModeBreakdown.BindValueChanged(v => modeSplitPage.SetBreakdown(v.NewValue), true);

                // Header status pip + footer
                pulse.LastUpdated.BindValueChanged(_ => updateFooter(), true);
                pulse.ConnectionState.BindValueChanged(v => statusPip.SetState(v.NewValue), true);

                // Last-viewed page persistence — restore on first load.
                int restored = Math.Clamp(pulse.LastViewedCarouselPage.Value, 0, page_count - 1);
                if (restored != currentPage)
                    goToPage(restored, animated: false);
            }
            else
            {
                Logger.Log("[ToriiServerPulse] popover loaded with no provider; bindings skipped.", LoggingTarget.Runtime, LogLevel.Verbose);
            }

            // Initial display catch-up + start auto-rotate.
            updatePageStripDisplay();
            armAutoAdvance();
        }

        // ─── Header / footer / tab strip / pages viewport ────────────

        private Drawable buildHeader()
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 22,
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            new osu.Game.Graphics.UserInterface.ToriiGateGlyph
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(13, 13),
                                Colour = torii_red,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = @"TORII SERVER PULSE",
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                                Spacing = new Vector2(1.4f, 0),
                                Colour = torii_red,
                            },
                        },
                    },
                    statusPip = new StatusPip
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                    },
                },
            };
        }

        private Drawable buildTabStrip()
        {
            // Three columns: ‹ arrow | dots + title (centred) | › arrow.
            // The centre column auto-sizes around the dot row + title so
            // the spacing reads cleanly even when the title is narrow.
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = tab_strip_height,
                Children = new Drawable[]
                {
                    leftArrow = new CarouselArrow(direction: -1, onClick: () =>
                    {
                        goToPage(currentPage - 1);
                    })
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                    rightArrow = new CarouselArrow(direction: +1, onClick: () =>
                    {
                        goToPage(currentPage + 1);
                    })
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 1),
                        Children = new Drawable[]
                        {
                            pageTitleText = new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                Spacing = new Vector2(0.9f, 0),
                                Colour = Color4.White,
                                Text = "OVERVIEW",
                            },
                            pageDotsFlow = new FillFlowContainer
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(5, 0),
                            },
                        },
                    },
                },
            };
        }

        private Drawable buildPagesViewport()
        {
            // Build the dot indicators inside the strip on first layout.
            // (Done lazily here so pageDotsFlow is non-null when we add.)
            for (int i = 0; i < page_count; i++)
            {
                int captured = i;
                pageDotsFlow.Add(new PageDot(captured, () => goToPage(captured)));
            }

            // Page strip is wider than the viewport (page_step × page_count)
            // and slides horizontally to bring the active page into view.
            // We use page_step (= panel_width - 2 * padding) instead of
            // raw panel_width because the strip lives INSIDE the body's
            // padded FillFlow — so its viewport's effective width is
            // already inset by the padding. Sizing pages to page_step
            // keeps every page snapped to the visible area.
            pagesStrip = new Container
            {
                RelativeSizeAxes = Axes.Y,
                Width = page_step * page_count,
                Children = new Drawable[]
                {
                    pagedDrawable(overviewPage,   page_overview),
                    pagedDrawable(hotMapsPage,    page_hot_maps),
                    pagedDrawable(livePlaysPage,  page_live_plays),
                    pagedDrawable(modeSplitPage,  page_mode_split),
                },
            };

            return pagesViewport = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = page_height,
                Masking = true,
                Children = new Drawable[]
                {
                    pagesStrip,
                    new SwipeCatcher(this)
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                },
            };
        }

        // Wraps a page in a fixed-width container at the right horizontal
        // offset for its slot in the carousel strip. Width matches
        // page_step so every page snaps to the masked viewport's
        // visible area exactly.
        private static Drawable pagedDrawable(Drawable page, int index)
        {
            return new Container
            {
                X = page_step * index,
                RelativeSizeAxes = Axes.Y,
                Width = page_step,
                Child = page,
            };
        }

        private Drawable buildFooter()
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 14,
                Child = footerText = new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Regular),
                    Text = "—",
                    Colour = muted_white,
                },
            };
        }

        // ─── Footer ticker / page strip update ───────────────────────

        private void updateFooter()
        {
            DateTimeOffset? captured = pulse?.LastUpdated.Value;

            if (captured == null)
            {
                footerText.Text = pulse?.ConnectionState.Value == ToriiServerPulseConnectionState.Disabled
                    ? "widget disabled · enable in Settings → Torii"
                    : "waiting for first snapshot…";
                return;
            }

            int seconds = (int)Math.Max(0, (DateTimeOffset.UtcNow - captured.Value).TotalSeconds);
            string ago = seconds < 1 ? "just now"
                : seconds < 60 ? $"{seconds}s ago"
                : seconds < 3600 ? $"{seconds / 60}m ago"
                : "stale (>1h)";

            footerText.Text = $"updated {ago}";
        }

        private void updatePageStripDisplay()
        {
            pageTitleText.Text = pageTitle(currentPage);

            int idx = 0;
            foreach (var d in pageDotsFlow.Children)
            {
                if (d is PageDot dot)
                    dot.SetActive(idx == currentPage);
                idx++;
            }
        }

        private static string pageTitle(int index) => index switch
        {
            page_overview => "OVERVIEW",
            page_hot_maps => "HOT MAPS",
            page_live_plays => "LIVE PLAYS",
            page_mode_split => "MODE SPLIT",
            _ => "—",
        };

        // ─── Page navigation ─────────────────────────────────────────

        private void goToPage(int index, bool animated = true)
        {
            // Wrap-around so ‹ from page 0 lands on the last page and
            // › from the last page lands on page 0. Modulo with positive
            // adjustment because C# % preserves sign of the dividend.
            currentPage = (index % page_count + page_count) % page_count;

            float targetX = -page_step * currentPage;

            pagesStrip.ClearTransforms(targetMember: nameof(Position));
            if (animated)
                pagesStrip.MoveToX(targetX, 380, Easing.OutQuint);
            else
                pagesStrip.X = targetX;

            updatePageStripDisplay();

            if (pulse != null)
                pulse.LastViewedCarouselPage.Value = currentPage;

            // Reset the auto-advance timer so the user gets the full
            // dwell time on the page they just chose, instead of the
            // remainder of the previous tick.
            armAutoAdvance();
        }

        private void armAutoAdvance()
        {
            autoAdvanceDelegate?.Cancel();
            if (isHoveringPopover) return;
            autoAdvanceDelegate = Scheduler.AddDelayed(() =>
            {
                if (State.Value != Visibility.Visible) return;
                if (isHoveringPopover) return;
                goToPage(currentPage + 1);
            }, auto_scroll_interval_ms);
        }

        // ─── Hover / show / hide ─────────────────────────────────────

        protected override bool OnHover(HoverEvent e)
        {
            isHoveringPopover = true;
            autoAdvanceDelegate?.Cancel();
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            isHoveringPopover = false;
            armAutoAdvance();
            base.OnHoverLost(e);
        }

        protected override void PopIn()
        {
            this.MoveToY(8).FadeTo(0);
            this.MoveToY(0, 220, Easing.OutQuint);
            this.FadeIn(200, Easing.OutQuint);

            updateFooter();
            armAutoAdvance();
            armOutsideClickCatcher();
        }

        protected override void PopOut()
        {
            this.MoveToY(-6, 180, Easing.OutQuint);
            this.FadeOut(180, Easing.OutQuint);
            autoAdvanceDelegate?.Cancel();
            disposeOutsideClickCatcher();
        }

        private OutsideClickCatcher? outsideClickCatcher;

        private void armOutsideClickCatcher()
        {
            disposeOutsideClickCatcher();

            if (Parent is not CompositeDrawable parent) return;

            // The catcher is added as a SIBLING of this popover (under
            // the same parent — the toolbar pulse button). It draws
            // BEHIND the popover (Depth = 1), accepts input only when
            // the click is OUTSIDE the popover's screen-space bounds
            // (its ReceivePositionalInputAt skips inside-clicks so
            // they reach the popover normally), and dismisses on any
            // outside mouse-down. The click is allowed to pass through
            // to whatever's underneath — that matches the "click on
            // toolbar to dismiss this AND activate the thing I clicked
            // on" affordance users expect from the rest of lazer.
            outsideClickCatcher = new OutsideClickCatcher(this, () =>
            {
                if (State.Value == Visibility.Visible)
                    Hide();
            })
            {
                Depth = 1,
            };

            // AddInternal isn't accessible on `parent` from here (it's
            // protected). The popover is the immediate child of the
            // pulse button which exposed AddInternal to add us; we
            // mirror that by exposing a small method on the button via
            // an interface, OR (simpler) just append the catcher to
            // ourselves with a transparent draw quad and override
            // ReceivePositionalInputAt to span the screen.
            AddInternal(outsideClickCatcher);
        }

        private void disposeOutsideClickCatcher()
        {
            if (outsideClickCatcher != null)
            {
                RemoveInternal(outsideClickCatcher, true);
                outsideClickCatcher = null;
            }
        }

        // ─── Anchor positioning ──────────────────────────────────────

        protected override void Update()
        {
            base.Update();

            // Anchor the popover to the button's bottom-centre on every
            // frame. Toolbar siblings can resize, so a one-shot
            // positioning at construction would drift visually.
            if (AnchoredAt == null) return;
            if (Parent == null) return;

            var anchorRect = AnchoredAt.ScreenSpaceDrawQuad;
            Vector2 localTopCentre = Parent.ToLocalSpace(new Vector2(
                anchorRect.BottomLeft.X + anchorRect.Width / 2f,
                anchorRect.BottomLeft.Y));

            // 8px gap below the button for a breath of air.
            Position = new Vector2(localTopCentre.X, localTopCentre.Y + 8);

            // Auto-hide if our anchoring button (or any of its
            // ancestors) has been faded out. The classic Toolbar fades
            // its child cluster on collapse; we walk the parent chain
            // and multiply alphas so a fade anywhere up the tree
            // closes us. Avoids the "toolbar hides but popover stays
            // open invisibly" reported upstream.
            if (State.Value == Visibility.Visible)
            {
                float effectiveAlpha = AnchoredAt.Alpha;
                Drawable? walker = AnchoredAt.Parent as Drawable;
                int safety = 32; // bound the walk in case of unexpected loops
                while (walker != null && effectiveAlpha > 0.01f && safety-- > 0)
                {
                    effectiveAlpha *= walker.Alpha;
                    walker = walker.Parent as Drawable;
                }

                if (effectiveAlpha < 0.5f)
                    Hide();
            }
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (State.Value != Visibility.Visible)
                return base.OnKeyDown(e);

            switch (e.Key)
            {
                case osuTK.Input.Key.Escape:
                    Hide();
                    return true;
                case osuTK.Input.Key.Left:
                    goToPage(currentPage - 1);
                    return true;
                case osuTK.Input.Key.Right:
                    goToPage(currentPage + 1);
                    return true;
            }

            return base.OnKeyDown(e);
        }

        // Click handlers — block events that hit the popover from
        // propagating through to whatever's behind. Without these,
        // upstream feedback was: "clicking on popover resulting in
        // clicks behind it" — a click on a button row would also
        // activate the toolbar overlay underneath, etc.
        protected override bool OnMouseDown(MouseDownEvent e) => true;
        protected override bool OnClick(ClickEvent e) => true;
        protected override bool OnDragStart(DragStartEvent e) => true;

        protected override void Dispose(bool isDisposing)
        {
            footerTickDelegate?.Cancel();
            autoAdvanceDelegate?.Cancel();
            base.Dispose(isDisposing);
        }

        // ─────────────────────────────────────────────────────────────
        // OutsideClickCatcher
        // ─────────────────────────────────────────────────────────────
        // Transparent zero-rendered drawable that lives as a sibling of
        // the popover (added via AddInternal, Depth=1 so it's drawn
        // BEHIND the popover) and accepts mouse input ANYWHERE on
        // screen. ReceivePositionalInputAt is the trick: by overriding
        // it to a custom predicate (true iff outside the popover's
        // screen-space draw quad), we let inside-popover clicks bypass
        // the catcher and reach the popover normally, while
        // outside-popover clicks dismiss the popover.
        //
        // OnMouseDown returns false so the click is allowed to continue
        // to whatever drawable sits beneath at that screen position
        // (e.g. a toolbar button the user explicitly meant to click,
        // dismissing this and activating that in one gesture — exactly
        // what users expect from the rest of lazer's overlay UX).
        private partial class OutsideClickCatcher : Drawable
        {
            private readonly ToriiServerPulsePopover popover;
            private readonly Action onOutsideClick;

            public OutsideClickCatcher(ToriiServerPulsePopover popover, Action onOutsideClick)
            {
                this.popover = popover;
                this.onOutsideClick = onOutsideClick;

                // Always-present so we receive input even when our
                // own draw quad is zero-sized; we override the hit
                // test against screen-space coords directly.
                AlwaysPresent = true;
            }

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
            {
                // Catch ONLY outside-clicks. Inside-clicks return false
                // so input falls through to the popover (which sits on
                // top of us in draw order via Depth = 1 here vs the
                // popover's default depth 0).
                if (popover == null || popover.State.Value != Visibility.Visible)
                    return false;

                if (popover.ScreenSpaceDrawQuad.Contains(screenSpacePos))
                    return false;

                // Critical: ALSO ignore clicks on the anchor button
                // (the toolbar pill that owns the popover). Otherwise
                // this catcher fires on the button click → Hide() →
                // click propagates to the button → togglePopover sees
                // State.Hidden → Show() → popover re-opens with the
                // pop-in animation. Net effect: clicking the button
                // while open never closed it, every click looked like a
                // re-open. By excluding the anchor's screen-space rect
                // here, the button's click reaches togglePopover with
                // State still Visible, which then routes to Hide() as
                // expected.
                if (popover.AnchoredAt?.ScreenSpaceDrawQuad.Contains(screenSpacePos) == true)
                    return false;

                return true;
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                onOutsideClick();
                // Return false so the click ALSO propagates to whatever
                // is behind us — the user clicking on a toolbar item
                // dismisses the popover AND activates that item.
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // SwipeCatcher
        //
        // Transparent overlay that captures drag gestures over the
        // pages viewport. We can't put the drag handling on the pages
        // themselves because each page has its own interactive elements
        // (clickable rows, etc.) and we don't want clicks on those to
        // start an accidental swipe — the catcher is a top-level layer
        // that only receives input the pages haven't already consumed.
        //
        // Drag mechanics:
        //   - On drag start, snapshot the strip's X.
        //   - On drag, follow the cursor's X delta directly so the strip
        //     moves under the user's finger.
        //   - On drag end, snap to the nearest page based on which
        //     page boundary we're closest to. If the velocity is
        //     significant past a half-page we snap forward; else we
        //     snap to the originating page.
        // ─────────────────────────────────────────────────────────────
        private partial class SwipeCatcher : Drawable
        {
            private readonly ToriiServerPulsePopover popover;

            public SwipeCatcher(ToriiServerPulsePopover popover)
            {
                this.popover = popover;
            }

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

            protected override bool OnDragStart(DragStartEvent e)
            {
                popover.dragStartStripX = popover.pagesStrip.X;
                popover.isDragging = true;
                popover.autoAdvanceDelegate?.Cancel();
                return true;
            }

            protected override void OnDrag(DragEvent e)
            {
                // Follow the cursor's screen-space X delta. ToLocalSpace
                // would incur extra math; the drag delta is already in
                // local-equivalent units.
                popover.pagesStrip.X = popover.dragStartStripX + (e.MousePosition.X - e.MouseDownPosition.X);
            }

            protected override void OnDragEnd(DragEndEvent e)
            {
                popover.isDragging = false;

                // Compute which page the strip is closest to. Using
                // page_step (the actual carousel step size) rather than
                // panel_width so the snap math matches the slide
                // geometry — getting these out of sync would land the
                // user between pages on swipe-end.
                float currentX = popover.pagesStrip.X;
                int nearest = (int)Math.Round(-currentX / page_step);
                nearest = Math.Clamp(nearest, 0, page_count - 1);
                popover.goToPage(nearest);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // CarouselArrow
        //
        // ‹ / › chevrons in the tab strip. Hover and click feedback
        // mirror the Torii toolbar action buttons (subtle scale + glow).
        // ─────────────────────────────────────────────────────────────
        private partial class CarouselArrow : OsuClickableContainer
        {
            private SpriteIcon icon = null!;
            private Box hoverBox = null!;

            public CarouselArrow(int direction, Action onClick)
            {
                Action = onClick;
                Size = new Vector2(22, tab_strip_height);

                _direction = direction;
            }

            private readonly int _direction;

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    hoverBox = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = torii_red.Opacity(0.18f),
                        Alpha = 0,
                    },
                    icon = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = _direction < 0
                            ? FontAwesome.Solid.ChevronLeft
                            : FontAwesome.Solid.ChevronRight,
                        Size = new Vector2(11),
                        Colour = muted_white,
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                hoverBox.FadeIn(160, Easing.OutQuint);
                icon.FadeColour(Color4.White, 160, Easing.OutQuint);
                this.ScaleTo(1.1f, 200, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hoverBox.FadeOut(220, Easing.OutQuint);
                icon.FadeColour(muted_white, 220, Easing.OutQuint);
                this.ScaleTo(1f, 220, Easing.OutQuint);
                base.OnHoverLost(e);
            }

            protected override bool OnClick(ClickEvent e)
            {
                icon.ScaleTo(0.85f, 80, Easing.OutQuint).Then().ScaleTo(1f, 240, Easing.OutBack);
                return base.OnClick(e);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // PageDot
        //
        // Small dot in the tab-strip indicator row. Active dot is
        // vermillion + scaled up; inactive dots are muted grey + smaller.
        // Clickable for direct nav to that page.
        // ─────────────────────────────────────────────────────────────
        private partial class PageDot : OsuClickableContainer
        {
            private Box fill = null!;
            private bool active;

            public PageDot(int pageIndex, Action onClick)
            {
                Action = onClick;
                Size = new Vector2(7, 7);
                _pageIndex = pageIndex;
            }

            private readonly int _pageIndex;

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = 3.5f;
                Child = fill = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = muted_white.Opacity(0.45f),
                };
            }

            public void SetActive(bool isActive)
            {
                if (active == isActive) return;
                active = isActive;

                fill.FadeColour(isActive ? torii_red : muted_white.Opacity(0.45f), 220, Easing.OutQuint);
                this.ScaleTo(isActive ? 1.25f : 1f, 220, Easing.OutBack);
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!active)
                    fill.FadeColour(Color4.White.Opacity(0.7f), 160, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!active)
                    fill.FadeColour(muted_white.Opacity(0.45f), 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // StatusPip
        //
        // Connection state indicator in the popover header. Green =
        // LIVE, yellow = SYNCING, red = OFFLINE, grey = IDLE/DISABLED.
        // ─────────────────────────────────────────────────────────────
        private partial class StatusPip : CompositeDrawable
        {
            private CircularContainer dot = null!;
            private Box dotFill = null!;
            private OsuSpriteText label = null!;

            public StatusPip()
            {
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(5, 0),
                    Children = new Drawable[]
                    {
                        dot = new CircularContainer
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(7, 7),
                            Masking = true,
                            Child = dotFill = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(120, 180, 120, 255),
                            },
                        },
                        label = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                            Spacing = new Vector2(0.8f, 0),
                            Text = "—",
                            Colour = muted_white,
                        },
                    },
                };
            }

            public void SetState(ToriiServerPulseConnectionState state)
            {
                if (label == null || dotFill == null) return;

                Color4 colour;
                string text;
                switch (state)
                {
                    case ToriiServerPulseConnectionState.Connected:
                        colour = new Color4(110, 220, 130, 255);
                        text = "LIVE";
                        break;
                    case ToriiServerPulseConnectionState.Connecting:
                        colour = new Color4(255, 220, 110, 255);
                        text = "SYNCING";
                        break;
                    case ToriiServerPulseConnectionState.Offline:
                        colour = new Color4(255, 110, 110, 255);
                        text = "OFFLINE";
                        break;
                    case ToriiServerPulseConnectionState.Idle:
                        colour = muted_white;
                        text = "IDLE";
                        break;
                    case ToriiServerPulseConnectionState.Disabled:
                        colour = muted_white;
                        text = "DISABLED";
                        break;
                    default:
                        colour = muted_white;
                        text = "—";
                        break;
                }

                dotFill.FadeColour(colour, 220, Easing.OutQuint);
                label.Text = text;
                label.FadeColour(colour.Opacity(0.85f), 220, Easing.OutQuint);
            }
        }
    }
}
