// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;

namespace osu.Game.Skinning.Components.Mania
{
    /// <summary>
    /// Legacy-skin variant of <see cref="ManiaRatioCounter"/>. Renders
    /// using the loaded skin's <c>score-N.png</c> bitmap digit sprites
    /// (and the <c>score-dot.png</c> glyph for the decimal separator)
    /// so the counter visually fits next to the rest of a legacy HUD.
    ///
    /// Graceful fallback when no legacy fonts available
    /// -------------------------------------------------
    /// Originally this just unconditionally returned a
    /// <see cref="LegacySpriteText"/>. Worked great on actual legacy
    /// skins, but on Argon / Triangles / any non-legacy skin
    /// LegacySpriteText resolves through <c>ISkinSource.GetTexture</c>
    /// which returns null for missing bitmap glyphs — the counter
    /// renders blank ("AFK in the toolbox preview" was the literal
    /// user complaint).
    ///
    /// Now we probe the active skin for the score-0 glyph at load
    /// time. Glyph found → use LegacySpriteText (the intended bitmap
    /// look). Glyph missing → fall back to a regular OsuSpriteText
    /// using the Numeric font, so the counter at least SHOWS its
    /// number instead of looking broken. Users on legacy skins get
    /// the proper bitmap-font experience; users who add this on a
    /// non-legacy skin get a sane fallback rather than a confusing
    /// empty box.
    /// </summary>
    public partial class LegacyManiaRatioCounter : ManiaRatioCounter
    {
        // canBeNull: true because the skin editor's toolbox preview
        // instantiates components in a dependency-borrowing container
        // that doesn't always have ISkinSource cached. Without the
        // guard, CreateSpriteText would NRE during the base
        // RollingCounter.load() call and crash the editor on first
        // open. We treat "no source" the same as "no bitmap font" —
        // fall back to the default font and the user sees the value.
        [Resolved(canBeNull: true)]
        private ISkinSource? skinSource { get; set; }

        public LegacyManiaRatioCounter()
        {
            // Conservative default placement: sits in the top-right
            // corner like the classic accuracy counter, just below it
            // so the two read as a stack of complementary metrics.
            // Skin editor users can drag it anywhere from here.
            Anchor = Anchor.TopRight;
            Origin = Anchor.TopRight;

            Scale = new Vector2(0.55f);
            Margin = new MarginPadding { Top = 60, Right = 17 };
        }

        protected sealed override OsuSpriteText CreateSpriteText()
        {
            if (hasLegacyScoreFont())
            {
                return new LegacySpriteText(LegacyFont.Score)
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    FixedWidth = true,
                };
            }

            // Non-legacy skin loaded — render with the default numeric
            // font so the user can still see a value. Same fixedWidth
            // / anchor wiring so a future skin-swap to a legacy skin
            // re-renders cleanly without re-adding the component.
            return new OsuSpriteText
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Font = OsuFont.Numeric.With(size: 22, fixedWidth: true),
            };
        }

        /// <summary>
        /// Probe the active skin for the <c>score-0</c> bitmap glyph.
        /// If absent (or there's no skin source at all — e.g. when
        /// living inside the editor's toolbox preview), the skin
        /// doesn't have score digits and <see cref="LegacySpriteText"/>
        /// would render blank. The caller falls back to default text
        /// rendering in that case so the counter always shows
        /// something.
        /// </summary>
        private bool hasLegacyScoreFont()
        {
            if (skinSource == null)
                return false;

            try
            {
                string prefix = skinSource.GetFontPrefix(LegacyFont.Score);
                return skinSource.GetTexture($@"{prefix}-0") != null;
            }
            catch
            {
                // Defensive: GetFontPrefix internally calls source.GetConfig
                // which can throw in some skin-source compositions
                // we have no easy visibility into. Treat any throw as
                // "no font available" rather than crashing the editor.
                return false;
            }
        }
    }
}
