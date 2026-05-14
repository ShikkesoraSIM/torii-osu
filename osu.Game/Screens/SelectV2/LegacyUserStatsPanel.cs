// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;
using osu.Game.Skinning;
using osu.Game.Users;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.SelectV2
{
    /// <summary>
    /// Legacy stable-style user info panel. Recreates the avatar +
    /// name + rank + level-bar + stats card that stable osu! rendered
    /// in its song select (and toolbar), using the original stable
    /// chrome textures (<c>user-bg</c>, <c>user-border</c>,
    /// <c>levelbar-bg</c>, <c>levelbar</c>) loaded through the
    /// active skin's lookup so any skin shipping the assets gets the
    /// legacy panel for free.
    ///
    /// Stable-source reference
    /// -----------------------
    /// Layout numbers replicated from
    /// <c>osu-stable-source/osu!/Online/Drawable/User.cs:610–680</c>:
    /// the user panel was 330 × 86 with a -4, -4 backdrop offset,
    /// avatar centred at (23, 23), name text 14 pt at (48, -2),
    /// info text 10 pt at (48, 12), rank text 36 pt right-aligned
    /// at (204, 14) when extended, level bar at (120, 62) with a
    /// 200 × 14 background and a golden-orange fill (252, 184, 6)
    /// proportioned to the user's level progress.
    ///
    /// Why a new class instead of lazer's UserRankPanel
    /// ------------------------------------------------
    /// Lazer ships <see cref="UserRankPanel"/> which shows the same
    /// information but in the lazer-modern card layout (Argon-style
    /// rounded card, no legacy textures). The user-facing request
    /// here was explicitly for the stable design rendered through
    /// legacy skin assets, so the chrome reads as a continuation
    /// of the legacy stable-era aesthetic rather than
    /// dropping a modern Lazer card into the middle of a legacy-
    /// styled song select. <see cref="UserRankPanel"/> stays
    /// available for the login-overlay path it was designed for.
    ///
    /// Skin behaviour
    /// --------------
    /// Each texture lookup goes through <see cref="ISkinSource.GetTexture"/>.
    /// When the active skin doesn't ship a given texture (Argon,
    /// Triangles, Classic, most user-imported skins), the
    /// corresponding sprite renders nothing — the panel naturally
    /// degrades to "no chrome, no level bar" but the text /
    /// avatar still appear, so it remains useful even when no
    /// stable assets are present. When the active skin DOES ship
    /// them (any imported stable-era .osk with <c>user-bg</c> /
    /// <c>user-border</c> / <c>levelbar*</c>) the panel matches
    /// stable's appearance closely.
    /// </summary>
    public partial class LegacyUserStatsPanel : CompositeDrawable
    {
        // Layout proportions: panel tightened from 400 → 320 because
        // at 400 the rank text and level bar both end ~80 px before
        // the panel's right edge, leaving a visible dead-space gap.
        // 320 is just wide enough to fit the username + 6-char rank
        // on the same row with the level bar spanning underneath,
        // with no wasted area on the right side.
        private const float panel_width = 320;
        private const float panel_height = 90;

        // Avatar block: square with slight rounding, 60 × 60 at
        // top-left with 10 px inset, 15 px vertical offset (60 + 15
        // = 75, fits the 90-tall panel with 15 px bottom breathing
        // room for the level bar).
        private const float avatar_inset = 10;
        private const float avatar_size = 60;
        private const float avatar_y = 15;

        // Text column starts 15 px right of the avatar (avatar
        // ends at x = 10 + 60 = 70, plus 15 padding = 85).
        private const float text_column_x = 85;

        // Larger fonts than the previous attempt — readable at the
        // panel's distance from the rest of the screen.
        private const float username_font_size = 16;
        private const float stats_font_size = 12;
        private const float rank_font_size = 24;

        // Row positions: username at top, three stat lines below at
        // 15 px line height (12 pt font + 3 px leading).
        private const float username_y = 6;
        private const float perf_y = 26;
        private const float accuracy_y = 41;
        private const float level_y = 56;

        // Rank text Y position. X position is computed at draw time
        // by anchoring to Origin.TopRight at (panel_width - 12),
        // so the text right-aligns to the panel's right edge with
        // a 12 px inset regardless of rank-string length.
        private const float rank_y = 12;

        // Level bar at the bottom of the panel. Spans from the text
        // column to a 12 px right inset, matching the rank text's
        // right alignment. Rank text and level bar end at the same
        // x coordinate; rank text is at y = 12 while the bar is at
        // y = 74, so they don't overlap despite sharing right-edge
        // alignment.
        private const float level_bar_x = text_column_x;
        private const float level_bar_y = 74;
        private const float level_bar_width = panel_width - text_column_x - 12;
        private const float level_bar_height = 8;

        // Grayscale-theme level-bar tint. Stable's reference at
        // User.cs:666 uses `new Color(252, 184, 6, 255)` — a golden
        // orange — but this panel ONLY renders in the grayscale
        // theme (gated in SongSelect via OsuColour.IsGrayscaleTheme), so
        // any colour would clash with the rest of the chrome. Pure
        // white at full alpha gives the level bar visibility without
        // breaking the all-black aesthetic.
        private static readonly Color4 level_bar_fill_colour = Color4.White;

        [Resolved]
        private ISkinSource skinSource { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved(CanBeNull = true)]
        private LocalUserStatisticsProvider? statisticsProvider { get; set; }

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        private Sprite backgroundSprite = null!;
        private Sprite borderSprite = null!;
        private Sprite levelBarBgSprite = null!;
        private Sprite levelBarFillSprite = null!;
        private Container levelBarFillClip = null!;
        private UpdateableAvatar avatar = null!;
        private OsuSpriteText nameText = null!;
        // Stable's pText spriteInfo at User.cs:635 used a single text
        // box (size 150 × 33) with `\n`-delimited content
        // "Performance:Xpp\nAccuracy:Y%\nLvZ" — three visible lines.
        // OsuSpriteText doesn't auto-wrap multi-line literals so we
        // split into three individually-positioned labels stacked
        // vertically. Same visual result, easier to format / colour
        // each line independently if we want to later.
        private OsuSpriteText performanceText = null!;
        private OsuSpriteText accuracyText = null!;
        private OsuSpriteText levelText = null!;
        private OsuSpriteText rankText = null!;

        // Subtle white additive overlay that fades in when the user
        // hovers the panel — replicates stable's panel-hover gray
        // brightening (User.cs handled it with a separate hover
        // sprite swap; we get the same effect by additively
        // overlaying white at low alpha on top of the existing
        // chrome). Pinned as the LAST child of InternalChildren
        // so it covers the avatar / text / chrome uniformly.
        private Box hoverHighlight = null!;

        private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();

        public LegacyUserStatsPanel()
        {
            Size = new Vector2(panel_width, panel_height);

            // Masking the whole panel keeps the avatar's circular
            // crop and the level-bar fill clipped inside the rounded
            // chrome rectangle.
            Masking = true;
            CornerRadius = 6;

            InternalChildren = new Drawable[]
            {
                // Solid black backdrop — Box rather than Sprite so
                // the panel stays opaque even on skins that don't
                // ship user-bg.png. Stable's User.cs:104 used
                // `new Color(10, 29, 75)` (dark blue) but that's the
                // stable-default-theme tint; this panel only renders
                // in the grayscale theme (gated via
                // OsuColour.IsGrayscaleTheme in SongSelect), so we
                // use pure black for visual consistency with the
                // rest of the grayscale chrome. α=235 keeps it
                // mostly opaque while letting the song-select
                // background bleed through at the edges.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0, 0, 0, 235),
                },

                // user-bg sprite tinted BLACK at low alpha — stable's
                // User.cs:624 passes `new Color(backgroundColour,
                // 200)` to multiply the white user-bg.png texture by
                // the panel's theme colour. With backgroundColour set
                // to black here, the texture's gradient detail shows
                // through as subtle dark shading on top of the Box
                // backdrop without introducing any colour. α=80 keeps
                // the texture visible as a faint highlight without
                // washing out the solid black.
                backgroundSprite = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    FillMode = FillMode.Stretch,
                    Colour = new Color4(0, 0, 0, 80),
                },

                // user-border sprite TINTED BLACK — stable's
                // User.cs:628 passes `new Color(0, 0, 0, 255)` so
                // the white border texture becomes a hard black
                // outline framing the panel. Without the tint the
                // white border would dominate the panel.
                borderSprite = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    FillMode = FillMode.Stretch,
                    Colour = new Color4(0, 0, 0, 255),
                },

                // Avatar — square with slightly rounded corners to
                // ride the chrome-theme aesthetic. Pure square felt
                // too hard against the rounded panel chrome; a small
                // 4-px corner radius softens the avatar block to
                // match the panel's own CornerRadius (set above).
                // Wrapped in a Container so the CornerRadius +
                // Masking apply to the avatar texture even if
                // UpdateableAvatar's internal layout changes.
                new Container
                {
                    Size = new Vector2(avatar_size),
                    Position = new Vector2(avatar_inset, avatar_y),
                    Masking = true,
                    CornerRadius = 4,
                    Child = avatar = new UpdateableAvatar
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                },

                // Username — top of the text column at username_font_size.
                nameText = new OsuSpriteText
                {
                    Position = new Vector2(text_column_x, username_y),
                    Font = OsuFont.GetFont(size: username_font_size, weight: FontWeight.Bold),
                    Colour = Color4.White,
                },

                // Three stat lines stacked tightly under the username.
                // Matches the stable User.cs:536 format string
                // `Performance:{pp}pp\nAccuracy:{acc:0.00}%\nLv{level}`.
                performanceText = new OsuSpriteText
                {
                    Position = new Vector2(text_column_x, perf_y),
                    Font = OsuFont.GetFont(size: stats_font_size),
                    Colour = Color4.White,
                },
                accuracyText = new OsuSpriteText
                {
                    Position = new Vector2(text_column_x, accuracy_y),
                    Font = OsuFont.GetFont(size: stats_font_size),
                    Colour = Color4.White,
                },
                levelText = new OsuSpriteText
                {
                    Position = new Vector2(text_column_x, level_y),
                    Font = OsuFont.GetFont(size: stats_font_size),
                    Colour = Color4.White,
                },

                // Big rank number on the right side of the panel —
                // right-aligned via Origin.TopRight so a wider rank
                // (e.g. "#88,888") and a narrower one (e.g. "#42")
                // both anchor to the same right edge. Position uses
                // `panel_width - 12` for a 12 px inset matching the
                // 10 px avatar inset on the opposite side, and
                // rank_y (12) to baseline-align with the username
                // visually.
                rankText = new OsuSpriteText
                {
                    Position = new Vector2(panel_width - 12, rank_y),
                    Origin = Anchor.TopRight,
                    Font = OsuFont.GetFont(size: rank_font_size, weight: FontWeight.Bold),
                    Colour = Color4.White,
                },

                // Level bar — wraps a solid Box (fallback that
                // always renders) + a Sprite that overlays
                // levelbar-bg.png when the active skin ships it.
                // Stable rendered just the Sprite (User.cs:659) but
                // skins without the legacy texture would show no
                // bar at all without the Box backing.
                new Container
                {
                    Position = new Vector2(level_bar_x, level_bar_y),
                    Size = new Vector2(level_bar_width, level_bar_height),
                    Children = new Drawable[]
                    {
                        // Always-visible dark backdrop for the bar.
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(0, 0, 0, 120),
                        },
                        // Stable's `levelbar-bg` texture sprite with
                        // 40 % additive overlay per User.cs:672-673.
                        // Invisible when the active skin doesn't
                        // ship the texture.
                        levelBarBgSprite = new Sprite
                        {
                            RelativeSizeAxes = Axes.Both,
                            FillMode = FillMode.Stretch,
                            Blending = BlendingParameters.Additive,
                            Alpha = 0.4f,
                        },
                    },
                },

                // Level bar FILL — clipped Container whose width
                // animates 0..200 px based on the user's level
                // progress. Inside: a solid Box (fallback colour,
                // always visible) + a Sprite (overlays levelbar.png
                // when skin ships it). Both use the golden-orange
                // tint stable applied at User.cs:666.
                levelBarFillClip = new Container
                {
                    Position = new Vector2(level_bar_x, level_bar_y),
                    Size = new Vector2(level_bar_width, level_bar_height),
                    Masking = true,
                    Children = new Drawable[]
                    {
                        // Solid golden fill — always visible. Stable
                        // used the additive-blended sprite alone but
                        // for skins without the texture we need a
                        // baseline opaque fill so the level bar
                        // doesn't disappear.
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = level_bar_fill_colour,
                        },
                        // levelbar.png Sprite at 70 % additive
                        // overlay per User.cs:675-676. Adds the
                        // legacy chrome shimmer on top of the solid
                        // base when the skin ships it.
                        levelBarFillSprite = new Sprite
                        {
                            RelativeSizeAxes = Axes.Both,
                            FillMode = FillMode.Stretch,
                            Blending = BlendingParameters.Additive,
                            Alpha = 0.7f,
                            Colour = level_bar_fill_colour,
                        },
                    },
                },

                // Hover-state brightening overlay. Additive blending
                // means alpha 0.08 reads as a gentle gray lift on top
                // of the existing black chrome without washing out
                // the text. Faded in/out from OnHover/OnHoverLost.
                hoverHighlight = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.White,
                    Alpha = 0,
                    Blending = BlendingParameters.Additive,
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            hoverHighlight.FadeTo(0.08f, 200, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hoverHighlight.FadeOut(200, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            localUser.BindTo(api.LocalUser);
            localUser.BindValueChanged(_ => updateUserDisplay(), true);

            if (statisticsProvider != null)
                statisticsProvider.StatisticsUpdated += onStatisticsUpdated;

            ruleset.BindValueChanged(_ => updateStatsDisplay(), true);

            applyTextures();
            skinSource.SourceChanged += applyTextures;
        }

        private void onStatisticsUpdated(UserStatisticsUpdate update) => Schedule(updateStatsDisplay);

        private void updateUserDisplay()
        {
            avatar.User = localUser.Value;
            nameText.Text = localUser.Value?.Username ?? @"Guest";
            updateStatsDisplay();
        }

        private void updateStatsDisplay()
        {
            var stats = statisticsProvider?.GetStatisticsFor(ruleset.Value);

            if (stats == null)
            {
                rankText.Text = string.Empty;
                performanceText.Text = @"Performance: --pp";
                accuracyText.Text = @"Accuracy: --";
                levelText.Text = @"Lv --";
                levelBarFillClip.Width = 0;
                return;
            }

            // Stable's RankText (User.cs:565): empty when Rank == 0,
            // else "#" + Rank with thousand-separator.
            rankText.Text = stats.GlobalRank.HasValue && stats.GlobalRank.Value > 0
                ? $@"#{stats.GlobalRank.Value:N0}"
                : string.Empty;

            // Stable's spriteInfo format (User.cs:536):
            //   "Performance:{0}pp\nAccuracy:{1:0.00}%\nLv{3}"
            // We display the same lines separately so each can
            // re-style independently. Format strings replicate the
            // upstream exactly — Performance with thousand-separator,
            // Accuracy with two decimal places, Level as plain int.
            performanceText.Text = stats.PP.HasValue
                ? $@"Performance: {stats.PP.Value:N0}pp"
                : @"Performance: --pp";
            accuracyText.Text = $@"Accuracy: {stats.Accuracy:0.00}%";
            levelText.Text = $@"Lv{stats.Level.Current}";

            // Level bar progress is 0..99 stored in Level.Progress —
            // map to a 0..1 width fraction of the bar.
            levelBarFillClip.Width = level_bar_width * (stats.Level.Progress / 100f);
        }

        private void applyTextures()
        {
            Schedule(() =>
            {
                backgroundSprite.Texture = skinSource.GetTexture(@"user-bg");
                borderSprite.Texture = skinSource.GetTexture(@"user-border");
                levelBarBgSprite.Texture = skinSource.GetTexture(@"levelbar-bg");
                levelBarFillSprite.Texture = skinSource.GetTexture(@"levelbar");
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            if (statisticsProvider != null)
                statisticsProvider.StatisticsUpdated -= onStatisticsUpdated;

            if (skinSource != null)
                skinSource.SourceChanged -= applyTextures;

            base.Dispose(isDisposing);
        }
    }
}
