// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Configuration;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.Cursor
{
    /// <summary>
    /// Renders the active skin's GAMEPLAY cursor exactly the way the
    /// osu! ruleset would in the playfield — same texture lookup
    /// (<c>cursor</c> + optional <c>cursormiddle</c>), same composition
    /// (sprites stacked at native texture size, centred, no aspect-fit
    /// nonsense), same scaling (multiplied by
    /// <see cref="OsuSetting.GameplayCursorSize"/> just like
    /// <c>OsuCursor.CalculateCursorScale</c>).
    ///
    /// Lives in <c>osu.Game</c> rather than the osu! ruleset DLL so it
    /// can be used outside gameplay context — specifically by the
    /// <see cref="Overlays.CursorSizePreviewOverlay"/> (which needs an
    /// accurate preview of the in-game cursor) and by
    /// <see cref="MenuCursorContainer"/> when the user opts into the
    /// "use gameplay cursor in menus" setting.
    ///
    /// Why duplicate the rendering instead of reaching into the osu!
    /// ruleset
    /// ----------------------------------------------------------
    /// <c>OsuCursor</c> + <c>SkinnableDrawable(OsuSkinComponentLookup.Cursor)</c>
    /// live in the ruleset DLL; osu.Game can't reference them without
    /// a circular project reference. Re-implementing the legacy
    /// cursor visual here is ~30 lines and matches what the vast
    /// majority of actual skins ship (legacy <c>cursor.png</c> +
    /// <c>cursormiddle.png</c>). For non-legacy skins (Argon /
    /// Triangles / vanilla) we fall back to a stylised circle that
    /// stands in until the user installs a legacy skin.
    /// </summary>
    public partial class SkinnableGameplayCursor : CompositeDrawable
    {
        // Same base size as the LegacyCursor in osu.Game.Rulesets.Osu —
        // the bounding container the skinnable cursor lives inside
        // before user / mod / auto scaling is applied.
        public const float BASE_SIZE = 50f;

        // Wraps the actual sprite stack so we can scale it without
        // also scaling fade transitions / state animations applied to
        // the outer drawable by callers (e.g. MenuCursorContainer).
        private Container scaleContainer = null!;

        private IBindable<float> gameplayCursorSize = null!;

        [Resolved(canBeNull: true)]
        private ISkinSource? skinSource { get; set; }

        public SkinnableGameplayCursor()
        {
            Size = new Vector2(BASE_SIZE);
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            gameplayCursorSize = config.GetBindable<float>(OsuSetting.GameplayCursorSize);

            InternalChild = scaleContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Child = createCursorSprites(),
            };

            // Mirror osu!'s gameplay-cursor scaling pipeline: the user
            // setting acts as a direct multiplier on the visual scale.
            // Auto-cursor-size (CS-derived) intentionally NOT applied
            // here — it depends on the active beatmap, which is
            // meaningless for a menu-context cursor.
            gameplayCursorSize.BindValueChanged(s => scaleContainer.Scale = new Vector2(s.NewValue), true);
        }

        /// <summary>
        /// Build the cursor sprite stack. Mirrors LegacyCursor's
        /// composition: <c>cursor</c> texture as the outer sprite,
        /// optional <c>cursormiddle</c> stacked on top, both at
        /// NATIVE texture size and centre-anchored — which is what
        /// the in-game cursor renders as for any legacy skin.
        ///
        /// If the active skin doesn't ship <c>cursor.png</c> at all
        /// (Argon / Triangles / vanilla), return a stylised circle
        /// placeholder so the caller still has SOMETHING to show.
        /// </summary>
        private Drawable createCursorSprites()
        {
            // Resolve the FIRST skin provider in the chain that has
            // a `cursor` texture, then look up `cursormiddle` against
            // THAT SAME provider. This mirrors what LegacyCursorTrail
            // does in osu.Game.Rulesets.Osu and avoids a subtle bug
            // we hit before: lazer's skin chain falls back through
            // user-skin → DefaultLegacySkin → ResourceStore, so a
            // user whose own skin ships `cursor.png` WITHOUT a
            // matching `cursormiddle.png` would silently inherit the
            // default skin's middle (a blue cross), which then
            // composites on top of their cursor in the preview even
            // though it never appears in gameplay. Locking the lookup
            // to the same provider keeps "what you see in preview"
            // == "what you see in play".
            ISkin? cursorProvider = skinSource?.FindProvider(s => s.GetTexture(@"cursor") != null);
            Texture? cursor = cursorProvider?.GetTexture(@"cursor");

            if (cursor != null)
            {
                Texture? middle = cursorProvider?.GetTexture(@"cursormiddle");

                var stack = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Child = new Sprite
                    {
                        Texture = cursor,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                };

                if (middle != null)
                {
                    stack.Add(new Sprite
                    {
                        Texture = middle,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    });
                }

                return stack;
            }

            // Fallback for skins without a legacy cursor texture. The
            // proportions (28-ish circle with 32% center dot) match
            // OsuCursor.SIZE territory so non-legacy users still see
            // a reasonably-sized preview.
            return new CircularContainer
            {
                Size = new Vector2(28),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                MaskingSmoothness = 2f,
                BorderThickness = 2.5f,
                BorderColour = Color4.White.Opacity(0.95f),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Radius = 6,
                    Colour = new Color4(255, 130, 195, 130),
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(255, 138, 211, 110),
                    },
                    new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(0.32f),
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.White,
                        },
                    },
                },
            };
        }
    }
}
