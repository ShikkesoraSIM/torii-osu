// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Screens.SelectV2
{
    /// <summary>
    /// Renders the active skin's <c>songselect-bottom</c> texture as
    /// the chrome strip across the bottom of V2 song select, behind
    /// the <see cref="osu.Game.Screens.Footer.ScreenFooter"/>'s
    /// action buttons. Reproduces the exact rendering stable osu!
    /// did in its song select screen — verified from the upstream
    /// stable source at
    /// <c>osu-stable-source/osu!/GameModes/Select/SongSelection.cs:733</c>:
    /// <code>
    /// detailsBack = new pSprite(TextureManager.Load(@"songselect-bottom"),
    ///     Fields.TopLeft, Origins.BottomLeft, Clocks.Game,
    ///     new Vector2(0, 480), 0.7F, true, Color.White);
    /// detailsBack.VectorScale = new Vector2(
    ///     (window_width / ratio) / (sprite_width * 0.625f), 1);
    /// </code>
    /// Stable stretched the texture's X axis to fit screen width
    /// while keeping the Y axis at its natural pixel height (the
    /// 0.625 factor is stable's @2x-asset scaling, baked into the
    /// VectorScale). The texture's natural aspect is ~11.4 : 1 so
    /// stretching X-only to a 16 : 9 / 21 : 9 / 32 : 9 screen
    /// barely distorts visibly — the strip is mostly uniform dark
    /// with detail accents that survive the stretch.
    ///
    /// Texture source — any skin, not just one bundle
    /// ----------------------------------------------
    /// The lookup goes through <see cref="ISkinSource.GetTexture"/>
    /// against whatever skin the user currently has selected. Any
    /// imported stable-style .osk that ships <c>songselect-bottom</c>
    /// (Spoo's OFF SCRIPT, stable-era skin downloads, user-authored
    /// ports) drives the same render path — Torii does not bundle a
    /// chrome skin of its own. Earlier iterations shipped a filtered
    /// stable-chrome built-in to guarantee the texture was available,
    /// but the bundle's 22.9 MB installer cost wasn't justified once
    /// the in-code fallback (below) landed: the
    /// fallback already looks clean on its own, and a user wanting
    /// the literal stable look can just drop the .osk into their
    /// skins folder.
    ///
    /// Skins without the texture
    /// -------------------------
    /// <see cref="ISkinSource.GetTexture"/> returns null for skins
    /// that don't ship <c>songselect-bottom</c> (Argon, Triangles,
    /// Classic, most user-imported lazer-era skins). When null we
    /// hide the texture sprite and show a Torii-Nova-styled fallback
    /// instead: a 64 px dark vertical gradient with a thin white
    /// accent line at the top, a faint secondary divider just below
    /// it, and a soft additive lift at the top edge for depth. The
    /// result is a clean grayscale chrome strip that defines the
    /// boundary between the carousel above and the action buttons
    /// below without competing with the song background art.
    ///
    /// Skin changes
    /// ------------
    /// Subscribed to <see cref="ISkinSource.SourceChanged"/> so the
    /// strip swaps live when the user switches skins from the
    /// settings dropdown — no song-select reload required.
    /// </summary>
    public partial class LegacyFooterChromeStrip : CompositeDrawable
    {
        // Fallback chrome height when no songselect-bottom texture
        // is available. 64 px is enough vertical room for the
        // gradient + accent lines to read as a distinct UI region
        // without dominating the screen the way stable's 120 px
        // chrome strip does.
        private const float fallback_height = 64;

        [Resolved]
        private ISkinSource skinSource { get; set; } = null!;

        private Sprite bottomStripSprite = null!;
        private Container fallbackChrome = null!;

        public LegacyFooterChromeStrip()
        {
            RelativeSizeAxes = Axes.X;
            Height = fallback_height;

            Anchor = Anchor.BottomCentre;
            Origin = Anchor.BottomCentre;

            InternalChildren = new Drawable[]
            {
                // Torii-Nova grayscale fallback — shown when the
                // active skin doesn't ship songselect-bottom. Four
                // layers compose a soft chrome strip:
                //
                //   1. Vertical gradient: transparent at top,
                //      nearly-opaque at bottom. Lets the song
                //      background bleed through the upper edge
                //      while keeping the buttons below readable.
                //   2. Additive top-edge lift: a 12 px white gradient
                //      that fades down, giving the chrome a soft
                //      "emerging from above" appearance instead of
                //      a hard horizontal cutoff.
                //   3. Primary top accent: 1 px crisp white line at
                //      ~40 % alpha — the actual visual boundary
                //      between carousel and footer.
                //   4. Secondary accent: 1 px white line 3 px below
                //      the primary at ~12 % alpha — adds a stable-
                //      era double-line feel for sophistication
                //      without leaning into a full chrome bar.
                fallbackChrome = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = ColourInfo.GradientVertical(
                                new Color4(0, 0, 0, 110),
                                new Color4(0, 0, 0, 235)),
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 12,
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Colour = ColourInfo.GradientVertical(
                                new Color4(255, 255, 255, 28),
                                new Color4(255, 255, 255, 0)),
                            Blending = BlendingParameters.Additive,
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 1,
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Colour = new Color4(255, 255, 255, 105),
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 1,
                            Y = 3,
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Colour = new Color4(255, 255, 255, 30),
                        },
                    },
                },

                // Skin-shipped chrome texture (stable .osk imports
                // that include songselect-bottom). When loaded this
                // layers on top of the fallback at full alpha; when
                // not loaded its Alpha stays 0 and the fallback
                // shows through.
                bottomStripSprite = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    // Stretch only — height comes from the wrapper which
                    // we size to the texture's natural pixel height; X is
                    // wrapper-width = screen-width. End result: identical
                    // to stable's VectorScale(stretchX, 1) behaviour.
                    FillMode = FillMode.Stretch,
                    Alpha = 0f,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            applyTexture();
            skinSource.SourceChanged += applyTexture;
        }

        private void applyTexture()
        {
            // Marshal to the update thread — SourceChanged can fire
            // from background threads when skins finish loading.
            Schedule(() =>
            {
                var texture = skinSource.GetTexture(@"songselect-bottom");
                bottomStripSprite.Texture = texture;

                if (texture != null)
                {
                    // Skin-provided chrome: hide the fallback and
                    // size the wrapper to the texture's natural pixel
                    // height (DisplayHeight is @1x-equivalent so @2x
                    // bundles report the right value).
                    Height = texture.DisplayHeight;
                    bottomStripSprite.Alpha = 1f;
                    fallbackChrome.Alpha = 0f;
                }
                else
                {
                    // No chrome texture: show the Torii Nova fallback
                    // at its compact fallback_height.
                    Height = fallback_height;
                    bottomStripSprite.Alpha = 0f;
                    fallbackChrome.Alpha = 1f;
                }
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            if (skinSource != null)
                skinSource.SourceChanged -= applyTexture;

            base.Dispose(isDisposing);
        }
    }
}
