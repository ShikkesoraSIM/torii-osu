// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics.Colour;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Online.Rooms;
using osu.Game.Overlays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Utils;
using osuTK.Graphics;

namespace osu.Game.Graphics
{
    public class OsuColour
    {
        public static Color4 Gray(float amt) => new Color4(amt, amt, amt, 1f);
        public static Color4 Gray(byte amt) => new Color4(amt, amt, amt, 255);

        // Torii: cosmetic UI theme toggle. Read by `fromHex()` below to
        // decide which palette layer to apply on top of the chrome
        // colours. Set ONCE at app startup by `SetThemeFromConfig`,
        // BEFORE any `new OsuColour(...)` runs (OsuGameBase.load() does
        // this immediately after constructing the config manager).
        // Changing the value mid-run is unsafe: the chrome palette is
        // captured into every drawable at its construction time, so a
        // flip post-load would leave already-loaded UI on the old
        // palette and only re-themed surfaces would pick up the new
        // one. The settings dropdown enforces a restart-confirm dialog
        // precisely to avoid that split state.
        //
        // Static rather than instance because OsuColour is resolved as
        // a DI singleton — every consumer reads the same fields off
        // the same instance, and the theme must be known BEFORE that
        // instance's field initialisers run. A static flag is the
        // simplest way to inject "the theme" into those initialisers
        // without rewriting every property as a method or making
        // OsuColour depend on the config manager.
        private static UIThemeOption activeTheme = UIThemeOption.Torii;

        /// <summary>
        /// Configure the cosmetic chrome palette before any
        /// <see cref="OsuColour"/> instance is constructed. Called by
        /// <c>OsuGameBase.load()</c> right after the config manager is
        /// ready and before the DI container resolves OsuColour.
        ///
        /// Idempotent. Safe to call from tests with whatever theme
        /// the test scene wants — the static state survives across
        /// instances but is local to the test process.
        /// </summary>
        public static void SetThemeFromConfig(UIThemeOption theme)
        {
            activeTheme = theme;
        }

        /// <summary>
        /// True when the active <see cref="UIThemeOption"/> is the
        /// grayscale palette. Surfaced for other palette providers
        /// (notably <see cref="osu.Game.Overlays.OverlayColourProvider"/>)
        /// to apply the same "zero saturation" rule to their dynamic
        /// HSL outputs without each needing its own config hookup.
        /// </summary>
        public static bool IsGrayscaleTheme => activeTheme == UIThemeOption.GrayscaleByFsyori;

        /// <summary>
        /// True when the active <see cref="UIThemeOption"/> is the
        /// midnight palette (deep purple / fuchsia accents on top of
        /// the grayscale-style structural reskin).
        /// </summary>
        public static bool IsMidnightTheme => activeTheme == UIThemeOption.Midnight;

        /// <summary>
        /// True when the active theme should adopt the structural
        /// reskin parts of grayscale (sharp corner radii, mounted
        /// legacy chrome panels). Currently both Grayscale and
        /// Midnight opt in; the call sites that want palette-specific
        /// behaviour should check <see cref="IsGrayscaleTheme"/> or
        /// <see cref="IsMidnightTheme"/> individually.
        /// </summary>
        public static bool UsesGrayscaleStructure => IsGrayscaleTheme || IsMidnightTheme;

        /// <summary>
        /// Theme-aware replacement for <see cref="Color4Extensions.FromHex"/>
        /// used by every chrome-accent instance field in this class.
        /// In the default <see cref="UIThemeOption.Torii"/> theme this
        /// returns the Torii palette colour. In grayscale mode it
        /// returns <paramref name="grayscaleHex"/> if provided
        /// (fsyori's literal palette value from their reskin branch),
        /// or falls back to luminance-preserving auto-desaturation if
        /// the caller didn't specify an explicit grayscale value
        /// (covers any Torii-only field fsyori never touched).
        ///
        /// Why a two-value signature instead of one + an auto-desat
        /// rule: fsyori curated specific grays per chrome accent that
        /// don't follow a pure luminance rule (their Pink is
        /// <c>#cccccc</c>, not <c>luminance(#ff66aa) = #9c9c9c</c>).
        /// Passing both hexes lets us land 1:1 on fsyori's published
        /// palette where they made an explicit choice, and still
        /// covers Torii-specific additions (the cyan/violet/lime/
        /// carmine families etc.) with a sane auto-default rather
        /// than a stale colour bleeding into the grayscale UI.
        ///
        /// Static + private so it can be used inside field initialiser
        /// expressions on this class.
        /// </summary>
        private static Color4 fromHex(string toriiHex, string? grayscaleHex = null, string? midnightHex = null)
        {
            if (activeTheme == UIThemeOption.GrayscaleByFsyori)
            {
                if (grayscaleHex != null)
                    return Color4Extensions.FromHex(grayscaleHex);

                // Fallback: luminance-preserving desaturation for
                // fields fsyori never touched. ITU-R BT.601 luma
                // coefficients — same formula
                // `ForegroundTextColourFor` uses for contrast
                // decisions, keeps "what counts as bright" consistent
                // across the file.
                var src = Color4Extensions.FromHex(toriiHex);
                float luma = 0.299f * src.R + 0.587f * src.G + 0.114f * src.B;
                return new Color4(luma, luma, luma, src.A);
            }

            if (activeTheme == UIThemeOption.Midnight)
            {
                if (midnightHex != null)
                    return Color4Extensions.FromHex(midnightHex);

                // Auto-tint fallback: for chrome accents that don't have an
                // explicit midnight value, take the source colour, preserve
                // its luminance and saturation amount, and shift the hue
                // toward the magenta family (~320°). Already-greyish
                // sources stay close to themselves; vivid colours pull
                // into the midnight palette so unenumerated accents still
                // feel coherent with the curated ones.
                return autoTintMidnight(Color4Extensions.FromHex(toriiHex));
            }

            return Color4Extensions.FromHex(toriiHex);
        }

        private const float MIDNIGHT_TARGET_HUE_DEGREES = 320f;
        private const float MIDNIGHT_HUE_PULL = 0.75f;

        /// <summary>
        /// Pull a colour toward the midnight palette by rotating its hue
        /// toward magenta proportionally to its saturation. Returns the
        /// source unchanged for near-neutral colours.
        /// </summary>
        private static Color4 autoTintMidnight(Color4 src)
        {
            float r = src.R, g = src.G, b = src.B;
            float max = MathF.Max(r, MathF.Max(g, b));
            float min = MathF.Min(r, MathF.Min(g, b));
            float l = (max + min) * 0.5f;
            float d = max - min;
            float s = d == 0f ? 0f : (l < 0.5f ? d / (max + min) : d / (2f - max - min));
            float h = 0f;
            if (d != 0f)
            {
                if (max == r) h = ((g - b) / d) % 6f;
                else if (max == g) h = ((b - r) / d) + 2f;
                else h = ((r - g) / d) + 4f;
                h *= 60f;
                if (h < 0f) h += 360f;
            }
            // Skew the pull amount by saturation so neutrals (s ≈ 0)
            // barely move and vivid hues snap toward magenta.
            float pull = MIDNIGHT_HUE_PULL * s;
            float newH = lerpAngle(h, MIDNIGHT_TARGET_HUE_DEGREES, pull);
            return hslToColor(newH, MathF.Min(1f, s * 1.05f), l, src.A);
        }

        private static float lerpAngle(float a, float b, float t)
        {
            float diff = ((b - a + 540f) % 360f) - 180f;
            return (a + diff * t + 360f) % 360f;
        }

        private static Color4 hslToColor(float h, float s, float l, float a)
        {
            float c = (1f - MathF.Abs(2f * l - 1f)) * s;
            float hp = h / 60f;
            float x = c * (1f - MathF.Abs(hp % 2f - 1f));
            float r1, g1, b1;
            if (hp < 1f) { r1 = c; g1 = x; b1 = 0; }
            else if (hp < 2f) { r1 = x; g1 = c; b1 = 0; }
            else if (hp < 3f) { r1 = 0; g1 = c; b1 = x; }
            else if (hp < 4f) { r1 = 0; g1 = x; b1 = c; }
            else if (hp < 5f) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }
            float m = l - c * 0.5f;
            return new Color4(r1 + m, g1 + m, b1 + m, a);
        }

        /// <summary>
        /// The maximum star rating colour which can be distinguished against a black background.
        /// </summary>
        public const float STAR_DIFFICULTY_DEFINED_COLOUR_CUTOFF = 6.5f;

        /// <summary>
        /// Star rating at which display text switches from static colours to a gradient.
        /// </summary>
        public const float STAR_DIFFICULTY_TEXT_GRADIENT_CUTOFF = 9.0f;

        public static readonly (float, Color4)[] STAR_DIFFICULTY_SPECTRUM =
        {
            (0.1f, Color4Extensions.FromHex("aaaaaa")),
            (0.1f, Color4Extensions.FromHex("4290fb")),
            (1.25f, Color4Extensions.FromHex("4fc0ff")),
            (2.0f, Color4Extensions.FromHex("4fffd5")),
            (2.5f, Color4Extensions.FromHex("7cff4f")),
            (3.3f, Color4Extensions.FromHex("f6f05c")),
            (4.2f, Color4Extensions.FromHex("ff8068")),
            (4.9f, Color4Extensions.FromHex("ff4e6f")),
            (5.8f, Color4Extensions.FromHex("c645b8")),
            (6.7f, Color4Extensions.FromHex("6563de")),
            (7.7f, Color4Extensions.FromHex("18158e")),
            (9.0f, Color4.Black),
            (10.0f, Color4.Black),
        };

        public static readonly (float, Color4)[] STAR_DIFFICULTY_TEXT_SPECTRUM =
        {
            (9.0f, Color4Extensions.FromHex("f6f05c")),
            (9.9f, Color4Extensions.FromHex("ff8068")),
            (10.6f, Color4Extensions.FromHex("ff4e6f")),
            (11.5f, Color4Extensions.FromHex("c645b8")),
            (12.4f, Color4Extensions.FromHex("6563de")),
        };

        /// <summary>
        /// Retrieves the colour for a given point in the star range.
        /// </summary>
        public Color4 ForStarDifficulty(double starDifficulty) => ColourUtils.SampleFromLinearGradient(STAR_DIFFICULTY_SPECTRUM, (float)Math.Round(starDifficulty, 2, MidpointRounding.AwayFromZero));

        /// <summary>
        /// Retrieves the colour for the text inside the star rating display.
        /// </summary>
        public Color4 ForStarDifficultyText(double starDifficulty)
        {
            if (starDifficulty < STAR_DIFFICULTY_DEFINED_COLOUR_CUTOFF)
                return Color4.Black.Opacity(0.75f);

            if (starDifficulty < STAR_DIFFICULTY_TEXT_GRADIENT_CUTOFF)
                // Torii: fsyori swaps Orange1 (warm amber accent text)
                // for a high-luminance light gray so the star-difficulty
                // text reads as part of the chrome rather than a vivid
                // accent. The above-cutoff gradient (line 167) still
                // uses STAR_DIFFICULTY_TEXT_SPECTRUM — fsyori left that
                // untouched, keeping the high-rating colour ladder for
                // ★9+ maps even on the grayscale theme.
                return IsGrayscaleTheme ? Color4Extensions.FromHex(@"e5e5e5") : Orange1;

            return ColourUtils.SampleFromLinearGradient(STAR_DIFFICULTY_TEXT_SPECTRUM, (float)Math.Round(starDifficulty, 2, MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// Retrieves the colour for a <see cref="ScoreRank"/>.
        /// </summary>
        public static Color4 ForRank(ScoreRank rank)
        {
            switch (rank)
            {
                case ScoreRank.XH:
                case ScoreRank.X:
                    return Color4Extensions.FromHex(@"de31ae");

                case ScoreRank.SH:
                case ScoreRank.S:
                    return Color4Extensions.FromHex(@"02b5c3");

                case ScoreRank.A:
                    return Color4Extensions.FromHex(@"88da20");

                case ScoreRank.B:
                    return Color4Extensions.FromHex(@"e3b130");

                case ScoreRank.C:
                    return Color4Extensions.FromHex(@"ff8e5d");

                case ScoreRank.D:
                    return Color4Extensions.FromHex(@"ff5a5a");

                case ScoreRank.F:
                default:
                    return Color4Extensions.FromHex(@"3f3f3f");
            }
        }

        /// <summary>
        /// Retrieves the colour for a <see cref="HitResult"/>.
        /// </summary>
        public Color4 ForHitResult(HitResult result)
        {
            // Torii: fsyori's reskin remaps every hit-result accent
            // (miss=red, meh=yellow, ok=green, good=greenlight,
            // great=blue) to a luminance ladder of pure grays so the
            // judgment counters / hit-meter / pp panel reads as a
            // grayscale gradient rather than a rainbow stack. Defaults
            // keep the original Torii saturated accents.
            switch (result)
            {
                case HitResult.IgnoreMiss:
                case HitResult.SmallTickMiss:
                    return IsGrayscaleTheme ? Gray(0.4f) : Color4.Gray;

                case HitResult.Miss:
                case HitResult.LargeTickMiss:
                case HitResult.ComboBreak:
                    return IsGrayscaleTheme ? Gray(0.2f) : Red;

                case HitResult.Meh:
                    return IsGrayscaleTheme ? Gray(0.5f) : Yellow;

                case HitResult.Ok:
                    return IsGrayscaleTheme ? Gray(0.7f) : Green;

                case HitResult.Good:
                    return IsGrayscaleTheme ? Gray(0.85f) : GreenLight;

                case HitResult.SmallTickHit:
                case HitResult.LargeTickHit:
                case HitResult.SliderTailHit:
                case HitResult.Great:
                    return IsGrayscaleTheme ? Gray(1.0f) : Blue;

                default:
                    return IsGrayscaleTheme ? Gray(0.9f) : BlueLight;
            }
        }

        /// <summary>
        /// Retrieves a colour for the given <see cref="BeatmapOnlineStatus"/>.
        /// A <see langword="null"/> value indicates that a "background" shade from the local <see cref="OverlayColourProvider"/>
        /// (or another fallback colour) should be used.
        /// </summary>
        /// <remarks>
        /// Sourced from web: https://github.com/ppy/osu-web/blob/007eebb1916ed5cb6a7866d82d8011b1060a945e/resources/assets/less/layout.less#L36-L50
        /// </remarks>
        public static Color4? ForBeatmapSetOnlineStatus(BeatmapOnlineStatus status)
        {
            // Torii: the RANKED / LOVED / QUALIFIED pill on every
            // beatmap-set card is one of the most chrome-visible spots
            // where Torii's default rainbow pops against fsyori's
            // grayscale chrome. fsyori remapped each status to a
            // single grayscale slot — RANKED is the brightest (0.9,
            // "ranked is good"), Loved/Qualified slightly less, WIP
            // and locally-modified darker, with Graveyard staying
            // pure black. Defaults preserve the Torii rainbow.
            switch (status)
            {
                case BeatmapOnlineStatus.None:
                    return IsGrayscaleTheme ? Gray(0.5f) : Color4.RosyBrown;

                case BeatmapOnlineStatus.LocallyModified:
                    return IsGrayscaleTheme ? Gray(0.7f) : Color4.OrangeRed;

                case BeatmapOnlineStatus.Ranked:
                case BeatmapOnlineStatus.Approved:
                    return IsGrayscaleTheme ? Gray(0.9f) : Color4Extensions.FromHex(@"b3ff66");

                case BeatmapOnlineStatus.Loved:
                    return IsGrayscaleTheme ? Gray(0.85f) : Color4Extensions.FromHex(@"ff66ab");

                case BeatmapOnlineStatus.Qualified:
                    return IsGrayscaleTheme ? Gray(0.8f) : Color4Extensions.FromHex(@"66ccff");

                case BeatmapOnlineStatus.Pending:
                    return IsGrayscaleTheme ? Gray(0.6f) : Color4Extensions.FromHex(@"ffd966");

                case BeatmapOnlineStatus.WIP:
                    return IsGrayscaleTheme ? Gray(0.4f) : Color4Extensions.FromHex(@"ff9966");

                case BeatmapOnlineStatus.Graveyard:
                    return Color4.Black;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Retrieves the main accent colour for a <see cref="ModType"/>.
        /// </summary>
        public Color4 ForModType(ModType modType)
        {
            // Torii: this is THE method that paints every mod button
            // in Settings → Mods (auto = blue, diff up = red, diff down
            // = lime, conversion = purple, fun = pink, system = yellow).
            // fsyori remaps each one to a luminance step on a single
            // gray ladder so the mod-select tray reads as a monochrome
            // grid in the grayscale theme. Bumped DifficultyIncrease
            // up to 0.9 (brightest) intentionally — "harder mods stand
            // out" is the existing semantic and the gray ladder
            // preserves it.
            switch (modType)
            {
                case ModType.Automation:
                    return IsGrayscaleTheme ? Gray(0.7f) : Blue1;

                case ModType.DifficultyIncrease:
                    return IsGrayscaleTheme ? Gray(0.9f) : Red1;

                case ModType.DifficultyReduction:
                    return IsGrayscaleTheme ? Gray(0.8f) : Lime1;

                case ModType.Conversion:
                    return IsGrayscaleTheme ? Gray(0.6f) : Purple1;

                case ModType.Fun:
                    return IsGrayscaleTheme ? Gray(0.8f) : Pink1;

                case ModType.System:
                    return IsGrayscaleTheme ? Gray(0.6f) : Yellow;

                default:
                    throw new ArgumentOutOfRangeException(nameof(modType), modType, "Unknown mod type");
            }
        }

        /// <summary>
        /// Retrieves the main accent colour for a <see cref="RoomCategory"/>.
        /// </summary>
        public Color4? ForRoomCategory(RoomCategory roomCategory)
        {
            // Torii: multiplayer room "category" pill — Spotlight green
            // / Featured-Artist blue under default Torii, both flat
            // light grays under fsyori's grayscale.
            switch (roomCategory)
            {
                case RoomCategory.Spotlight:
                    return IsGrayscaleTheme ? Gray(1.0f) : SpotlightColour;

                case RoomCategory.FeaturedArtist:
                    return IsGrayscaleTheme ? Gray(0.85f) : FeaturedArtistColour;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Retrieves the accent colour representing a <see cref="Room"/>'s current status.
        /// </summary>
        public Color4 ForRoomStatus(Room room)
        {
            // Torii: multiplayer lobby state colour. Default Torii uses
            // YellowDarker for ended, Purple for playing,
            // GreenDark/GreenLight for available (locked vs open).
            // fsyori collapses everything to a flat gray ladder where
            // brighter = more "active" (Playing = white, open = light
            // gray, locked = mid gray, ended = dim).
            if (room.HasEnded)
                return IsGrayscaleTheme ? Gray(0.2f) : YellowDarker;

            switch (room.Status)
            {
                case RoomStatus.Playing:
                    return IsGrayscaleTheme ? Gray(1.0f) : Purple;

                default:
                    if (IsGrayscaleTheme)
                        return room.HasPassword ? Gray(0.4f) : Gray(0.7f);

                    return room.HasPassword ? GreenDark : GreenLight;
            }
        }

        /// <summary>
        /// Retrieves colour for a <see cref="RankingTier"/>.
        /// See https://www.figma.com/file/YHWhp9wZ089YXgB7pe6L1k/Tier-Colours
        /// </summary>
        public static ColourInfo ForRankingTier(RankingTier tier)
        {
            switch (tier)
            {
                default:
                case RankingTier.Iron:
                    return Color4Extensions.FromHex(@"BAB3AB");

                case RankingTier.Bronze:
                    return ColourInfo.GradientVertical(Color4Extensions.FromHex(@"B88F7A"), Color4Extensions.FromHex(@"855C47"));

                case RankingTier.Silver:
                    return ColourInfo.GradientVertical(Color4Extensions.FromHex(@"E0E0EB"), Color4Extensions.FromHex(@"A3A3C2"));

                case RankingTier.Gold:
                    return ColourInfo.GradientVertical(Color4Extensions.FromHex(@"F0E4A8"), Color4Extensions.FromHex(@"E0C952"));

                case RankingTier.Platinum:
                    return ColourInfo.GradientVertical(Color4Extensions.FromHex(@"A8F0EF"), Color4Extensions.FromHex(@"52E0DF"));

                case RankingTier.Rhodium:
                    return ColourInfo.GradientVertical(Color4Extensions.FromHex(@"D9F8D3"), Color4Extensions.FromHex(@"A0CF96"));

                case RankingTier.Radiant:
                    return ColourInfo.GradientVertical(Color4Extensions.FromHex(@"97DCFF"), Color4Extensions.FromHex(@"ED82FF"));

                case RankingTier.Lustrous:
                    return ColourInfo.GradientVertical(Color4Extensions.FromHex(@"FFE600"), Color4Extensions.FromHex(@"ED82FF"));
            }
        }

        /// <summary>
        /// Returns a foreground text colour that is supposed to contrast well with
        /// the supplied <paramref name="backgroundColour"/>.
        /// </summary>
        public static Color4 ForegroundTextColourFor(Color4 backgroundColour)
        {
            // formula taken from the RGB->YIQ conversions: https://en.wikipedia.org/wiki/YIQ
            // brightness here is equivalent to the Y component in the above colour model, which is a rough estimate of lightness.
            float brightness = 0.299f * backgroundColour.R + 0.587f * backgroundColour.G + 0.114f * backgroundColour.B;
            // Torii: fsyori darkens the "text on light bg" branch from
            // Gray(0.2) to Gray(0.1) — the chrome is overall darker
            // in the grayscale theme so dark text on light pills needs
            // to be ALMOST black to keep the same contrast ratio.
            return Gray(brightness > 0.5f ? (IsGrayscaleTheme ? 0.1f : 0.2f) : 0.9f);
        }

        public readonly Color4 TeamColourRed = fromHex("#AA1414", "666666", "B81E59");
        public readonly Color4 TeamColourBlue = fromHex("#1462AA", "999999", "5B4BB8");

        // See https://github.com/ppy/osu-web/blob/master/resources/assets/less/colors.less
        // Midnight palette: a fuchsia / electric-violet rework where every
        // chrome family pulls toward the magenta/purple side of the wheel.
        // Yellow + Green keep enough warmth and saturation to stay legible
        // for difficulty rings and judgement colours.
        public readonly Color4 PurpleLighter = fromHex(@"eeeeff", @"f5f5f5", @"f0d8ff");
        public readonly Color4 PurpleLight = fromHex(@"aa88ff", @"cccccc", @"c094ff");
        public readonly Color4 PurpleLightAlternative = fromHex(@"cba4da", @"bbbbbb", @"b876e8");
        public readonly Color4 Purple = fromHex(@"8866ee", @"999999", @"9f4bff");
        public readonly Color4 PurpleDark = fromHex(@"6644cc", @"666666", @"6f24cc");
        public readonly Color4 PurpleDarkAlternative = fromHex(@"312436", @"222222", @"2d1438");
        public readonly Color4 PurpleDarker = fromHex(@"441188", @"111111", @"350a70");

        public readonly Color4 PinkLighter = fromHex(@"ffddee", @"eeeeee", @"ffe0f5");
        public readonly Color4 PinkLight = fromHex(@"ff99cc", @"dddddd", @"ff80d8");
        public readonly Color4 Pink = fromHex(@"ff66aa", @"cccccc", @"ec3cc7");
        public readonly Color4 PinkDark = fromHex(@"cc5288", @"aaaaaa", @"b8259f");
        public readonly Color4 PinkDarker = fromHex(@"bb1177", @"888888", @"821573");

        public readonly Color4 BlueLighter = fromHex(@"ddffff", @"f0f0f0", @"d9ddff");
        public readonly Color4 BlueLight = fromHex(@"99eeff", @"d0d0d0", @"92a4ff");
        public readonly Color4 Blue = fromHex(@"66ccff", @"b0b0b0", @"5b6ce8");
        public readonly Color4 BlueDark = fromHex(@"44aadd", @"808080", @"3d4ab2");
        public readonly Color4 BlueDarker = fromHex(@"2299bb", @"505050", @"1f2477");

        public readonly Color4 YellowLighter = fromHex(@"ffffdd", @"f5f5f5", @"fff2c8");
        public readonly Color4 YellowLight = fromHex(@"ffdd55", @"d5d5d5", @"ffd76e");
        public readonly Color4 Yellow = fromHex(@"ffcc22", @"b5b5b5", @"ffbf3a");
        public readonly Color4 YellowDark = fromHex(@"eeaa00", @"858585", @"cf8a18");
        public readonly Color4 YellowDarker = fromHex(@"cc6600", @"555555", @"8c5b00");

        public readonly Color4 GreenLighter = fromHex(@"eeffcc", @"f2f2f2", @"d6f0dc");
        public readonly Color4 GreenLight = fromHex(@"b3d944", @"d2d2d2", @"8fc99c");
        public readonly Color4 Green = fromHex(@"88b300", @"b2b2b2", @"62b378");
        public readonly Color4 GreenDark = fromHex(@"668800", @"727272", @"3d8050");
        public readonly Color4 GreenDarker = fromHex(@"445500", @"424242", @"2a5736");

        public readonly Color4 Sky = fromHex(@"6bb5ff", @"999999", @"7a85f0");
        public readonly Color4 GreySkyLighter = fromHex(@"c6e3f4", @"dddddd", @"c5c9eb");
        public readonly Color4 GreySkyLight = fromHex(@"8ab3cc", @"aaaaaa", @"6e75a0");
        public readonly Color4 GreySky = fromHex(@"405461", @"444444", @"3a3b58");
        public readonly Color4 GreySkyDark = fromHex(@"303d47", @"222222", @"25243a");
        public readonly Color4 GreySkyDarker = fromHex(@"21272c", @"111111", @"181624");

        public readonly Color4 SeaFoam = fromHex(@"05ffa2", @"ffffff", @"4af9c0");
        public readonly Color4 GreySeaFoamLighter = fromHex(@"9ebab1", @"cccccc", @"b0a8c4");
        public readonly Color4 GreySeaFoamLight = fromHex(@"4d7365", @"999999", @"4f4a72");
        public readonly Color4 GreySeaFoam = fromHex(@"33413c", @"333333", @"2d2842");
        public readonly Color4 GreySeaFoamDark = fromHex(@"2c3532", @"222222", @"22202f");
        public readonly Color4 GreySeaFoamDarker = fromHex(@"1e2422", @"111111", @"15131e");

        public readonly Color4 Cyan = fromHex(@"05f4fd", @"ffffff", @"4cdcff");
        public readonly Color4 GreyCyanLighter = fromHex(@"77b1b3", @"cccccc", @"9098c4");
        public readonly Color4 GreyCyanLight = fromHex(@"436d6f", @"999999", @"464a78");
        public readonly Color4 GreyCyan = fromHex(@"293d3e", @"333333", @"282940");
        public readonly Color4 GreyCyanDark = fromHex(@"243536", @"222222", @"1f1f30");
        public readonly Color4 GreyCyanDarker = fromHex(@"1e2929", @"111111", @"161624");

        public readonly Color4 Lime = fromHex(@"82ff05", @"ffffff", @"c0f04a");
        public readonly Color4 GreyLimeLighter = fromHex(@"deff87", @"cccccc", @"d0c8b0");
        public readonly Color4 GreyLimeLight = fromHex(@"657259", @"999999", @"706a52");
        public readonly Color4 GreyLime = fromHex(@"3f443a", @"333333", @"3a3528");
        public readonly Color4 GreyLimeDark = fromHex(@"32352e", @"222222", @"2a2520");
        public readonly Color4 GreyLimeDarker = fromHex(@"2e302b", @"111111", @"1f1c18");

        public readonly Color4 Violet = fromHex(@"bf04ff", @"ffffff", @"d04cff");
        public readonly Color4 GreyVioletLighter = fromHex(@"ebb8fe", @"cccccc", @"e8b8f5");
        public readonly Color4 GreyVioletLight = fromHex(@"685370", @"999999", @"7a4b80");
        public readonly Color4 GreyViolet = fromHex(@"46334d", @"333333", @"4a2858");
        public readonly Color4 GreyVioletDark = fromHex(@"2c2230", @"222222", @"2e1838");
        public readonly Color4 GreyVioletDarker = fromHex(@"201823", @"111111", @"1f1028");

        public readonly Color4 Carmine = fromHex(@"ff0542", @"ffffff", @"ff476e");
        public readonly Color4 GreyCarmineLighter = fromHex(@"deaab4", @"cccccc", @"dca6b9");
        public readonly Color4 GreyCarmineLight = fromHex(@"644f53", @"999999", @"6d4358");
        public readonly Color4 GreyCarmine = fromHex(@"342b2d", @"333333", @"3a2030");
        public readonly Color4 GreyCarmineDark = fromHex(@"302a2b", @"222222", @"2a1822");
        public readonly Color4 GreyCarmineDarker = fromHex(@"241d1e", @"111111", @"1d101a");

        public readonly Color4 Gray0 = fromHex(@"000");
        public readonly Color4 Gray1 = fromHex(@"111");
        public readonly Color4 Gray2 = fromHex(@"222");
        public readonly Color4 Gray3 = fromHex(@"333");
        public readonly Color4 Gray4 = fromHex(@"444");
        public readonly Color4 Gray5 = fromHex(@"555");
        public readonly Color4 Gray6 = fromHex(@"666");
        public readonly Color4 Gray7 = fromHex(@"777");
        public readonly Color4 Gray8 = fromHex(@"888");
        public readonly Color4 Gray9 = fromHex(@"999");
        public readonly Color4 GrayA = fromHex(@"aaa");
        public readonly Color4 GrayB = fromHex(@"bbb");
        public readonly Color4 GrayC = fromHex(@"ccc");
        public readonly Color4 GrayD = fromHex(@"ddd");
        public readonly Color4 GrayE = fromHex(@"eee");
        public readonly Color4 GrayF = fromHex(@"fff");

        #region "Basic" colour theme

        // Reference: https://www.figma.com/file/VIkXMYNPMtQem2RJg9k2iQ/Asset%2FColours?node-id=1838%3A3

        // Note that the colours in this region are also defined in `OverlayColourProvider` as `Colour{0,1,2,3,4}`.
        // The difference as to which should be used where comes down to context.
        // If the colour in question is supposed to always match the view in which it is displayed theme-wise, use `OverlayColourProvider`.
        // If the colour usage is special and in general differs from the surrounding view in choice of hue, use the `OsuColour` constants.

        public readonly Color4 Pink0 = fromHex(@"ff99c7", @"e0e0e0");
        public readonly Color4 Pink1 = fromHex(@"ff66ab", @"cccccc");
        public readonly Color4 Pink2 = fromHex(@"eb4791", @"aaaaaa");
        public readonly Color4 Pink3 = fromHex(@"cc3378", @"777777");
        public readonly Color4 Pink4 = fromHex(@"6b2e49", @"444444");

        public readonly Color4 Purple0 = fromHex(@"b299ff", @"d0d0d0");
        public readonly Color4 Purple1 = fromHex(@"8c66ff", @"b0b0b0");
        public readonly Color4 Purple2 = fromHex(@"7047eb", @"909090");
        public readonly Color4 Purple3 = fromHex(@"5933cc", @"606060");
        public readonly Color4 Purple4 = fromHex(@"3d2e6b", @"303030");

        public readonly Color4 Blue0 = fromHex(@"99ddff", @"e8e8e8");
        public readonly Color4 Blue1 = fromHex(@"66ccff", @"c8c8c8");
        public readonly Color4 Blue2 = fromHex(@"47b4eb", @"a8a8a8");
        public readonly Color4 Blue3 = fromHex(@"3399cc", @"888888");
        public readonly Color4 Blue4 = fromHex(@"2e576b", @"484848");

        public readonly Color4 Green0 = fromHex(@"99ffa2", @"eeeeee");
        public readonly Color4 Green1 = fromHex(@"66ff73", @"dddddd");
        public readonly Color4 Green2 = fromHex(@"47eb55", @"cccccc");
        public readonly Color4 Green3 = fromHex(@"33cc40", @"bbbbbb");
        public readonly Color4 Green4 = fromHex(@"2e6b33", @"aaaaaa");

        public readonly Color4 Lime0 = fromHex(@"ccff99", @"f5f5f5");
        public readonly Color4 Lime1 = fromHex(@"b2ff66", @"e5e5e5");
        public readonly Color4 Lime2 = fromHex(@"99eb47", @"d5d5d5");
        public readonly Color4 Lime3 = fromHex(@"7fcc33", @"c5c5c5");
        public readonly Color4 Lime4 = fromHex(@"4c6b2e", @"b5b5b5");

        public readonly Color4 Orange0 = fromHex(@"ffe699", @"fdfdfd");
        public readonly Color4 Orange1 = fromHex(@"ffd966", @"ededed");
        public readonly Color4 Orange2 = fromHex(@"ebc247", @"dddddd");
        public readonly Color4 Orange3 = fromHex(@"cca633", @"cdcdcd");
        public readonly Color4 Orange4 = fromHex(@"6b5c2e", @"bdbdbd");

        public readonly Color4 DarkOrange0 = fromHex(@"ffbb99", @"333333");
        public readonly Color4 DarkOrange1 = fromHex(@"ff9966", @"2b2b2b");
        public readonly Color4 DarkOrange2 = fromHex(@"eb7e47", @"222222");
        public readonly Color4 DarkOrange3 = fromHex(@"cc6633", @"1a1a1a");
        public readonly Color4 DarkOrange4 = fromHex(@"6b422e", @"111111");

        public readonly Color4 Red0 = fromHex(@"ff9b9b", @"f0f0f0");
        public readonly Color4 Red1 = fromHex(@"ff6666", @"d0d0d0");
        public readonly Color4 Red2 = fromHex(@"eb4747", @"b0b0b0");
        public readonly Color4 Red3 = fromHex(@"cc3333", @"909090");
        public readonly Color4 Red4 = fromHex(@"6b2e2e", @"707070");

        #endregion

        // Content Background
        public readonly Color4 B5 = fromHex(@"222a28", @"1a1a1a");

        public readonly Color4 RedLighter = fromHex(@"ffeded", @"f5f5f5");
        public readonly Color4 RedLight = fromHex(@"ed7787", @"d5d5d5");
        public readonly Color4 Red = fromHex(@"ed1121", @"b5b5b5");
        public readonly Color4 RedDark = fromHex(@"ba0011", @"757575");
        public readonly Color4 RedDarker = fromHex(@"870000");

        public readonly Color4 ChatBlue = fromHex(@"17292e", @"121212");

        public readonly Color4 ContextMenuGray = fromHex(@"223034", @"1c1c1c");

        public Color4 SpotlightColour => Green2;
        public Color4 FeaturedArtistColour => Blue2;

        public Color4 DangerousButtonColour => Pink3;
    }
}
