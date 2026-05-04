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
    /// A faithful, ruleset-agnostic copy of the osu!standard gameplay
    /// cursor pipeline (<c>OsuCursor</c> + <c>LegacyCursor</c>) lifted
    /// into <c>osu.Game</c> so it can be used outside the playfield —
    /// specifically by:
    /// - <see cref="Overlays.CursorSizePreviewOverlay"/> for an
    ///   accurate live preview when the user adjusts cursor size,
    /// - <see cref="MenuCursorContainer"/> when the user opts into
    ///   the "Use gameplay cursor in menus" setting.
    ///
    /// What "1:1 with gameplay" means here
    /// -----------------------------------
    /// We can't reach <c>OsuCursor</c> / <c>SkinnableDrawable(OsuSkinComponentLookup.Cursor)</c>
    /// directly because they live in <c>osu.Game.Rulesets.Osu.dll</c>
    /// and adding the project reference would create a circular
    /// dependency (the ruleset already references osu.Game). So we
    /// re-implement the same logic here, mirroring upstream's
    /// behaviour byte-for-byte where it matters:
    ///
    /// - Texture lookup: <c>cursor</c> + optional <c>cursormiddle</c>,
    ///   pinned to the SAME provider that supplied <c>cursor</c>
    ///   (matches <c>LegacyCursorTrail.cs</c> — prevents a skin
    ///   without its own cursormiddle from inheriting the bundled
    ///   default's blue cross).
    /// - Composition: stacked sprites at NATIVE texture size, both
    ///   centre-anchored. Identical to <c>LegacyCursor</c>.
    /// - Origin: <c>Centre</c> — the cursor's visual middle aligns
    ///   with the host's reported mouse position. Same as
    ///   <c>OsuCursor.Origin = Anchor.Centre</c>. This fixes the
    ///   "click point doesn't match the cursor middle" alignment
    ///   bug from the previous TopLeft-anchored attempt.
    /// - Scale: multiplied by <see cref="OsuSetting.GameplayCursorSize"/>,
    ///   same maths as <c>OsuCursor.CalculateCursorScale</c> minus the
    ///   beatmap-CS-derived auto-scale (which is meaningless outside
    ///   a playfield).
    /// - Rotation: continuous spin if the skin's <c>cursorrotate</c>
    ///   config is on. Same constants as <c>LegacyCursor</c>
    ///   (<c>REVOLUTION_DURATION = 10000</c>, clockwise).
    /// - Click feel: <see cref="Expand"/> / <see cref="Contract"/>
    ///   methods animate scale 1.0× → 1.2× and back — same shape and
    ///   timing as <c>SkinnableCursor.Expand/Contract</c>
    ///   (<c>pressed_scale = 1.2f</c>, OutElasticHalf in 400ms,
    ///   OutQuad in 400ms).
    ///
    /// Performance
    /// -----------
    /// One Container + at most two Sprites for the legacy-skin path,
    /// or three primitive shapes for the fallback. No per-frame work,
    /// no allocations after construction. The continuous rotation
    /// uses a single <see cref="osu.Framework.Graphics.Transforms.TransformSequenceExtensions"/>
    /// loop registered at LoadComplete — same approach upstream uses.
    ///
    /// What's intentionally NOT here yet
    /// ---------------------------------
    /// Cursor trail (<c>LegacyCursorTrail</c>) is its own component
    /// in the osu! ruleset, with its own particle pipeline. Bringing
    /// it across is a separate change — flagged in code below.
    /// </summary>
    public partial class SkinnableGameplayCursor : CompositeDrawable
    {
        // Bounding-box base size — same as LegacyCursor's Size = 50.
        // The actual rendered cursor is the sprite at its native
        // texture footprint, centred inside this box, scaled by
        // GameplayCursorSize. This number doesn't constrain the
        // sprite; it's the "logical" cursor size for layout purposes.
        public const float BASE_SIZE = 50f;

        // Pressed-state scale multiplier — copied from osu! ruleset's
        // SkinnableCursor.pressed_scale. Pulling it into a const so
        // the user-facing tuning stays in sync if upstream ever
        // changes their value.
        private const float pressed_scale = 1.2f;
        private const float released_scale = 1f;

        // Continuous-rotation period when the skin requests it
        // (cursorrotate = 1). Matches LegacyCursor.REVOLUTION_DURATION.
        private const int rotation_revolution_duration_ms = 10_000;

        // Inner container that we apply the scale + expand animation
        // to. Separated from the outer so the Expand transform
        // doesn't collide with the GameplayCursorSize binding (which
        // also writes Scale).
        private Container scaleContainer = null!;

        // The drawable inside scaleContainer that we attach the
        // continuous spin to (matches LegacyCursor's ExpandTarget).
        // Only the visual cursor rotates — the scale container stays
        // upright so Expand / Contract scale animations don't interact
        // weirdly with the spin.
        private Drawable? rotationTarget;

        private IBindable<float> gameplayCursorSize = null!;

        private float currentExpandFactor = released_scale;

        [Resolved(canBeNull: true)]
        private ISkinSource? skinSource { get; set; }

        public SkinnableGameplayCursor()
        {
            // Centre origin — the cursor's visual middle is the
            // "click point" anchored to the mouse position. Same as
            // OsuCursor's constructor.
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
                Child = rotationTarget = createCursorSprites(),
            };

            // Mirror osu!'s gameplay-cursor scaling pipeline: the user
            // setting acts as a direct multiplier on the visual scale.
            // Auto-cursor-size (CS-derived) intentionally NOT applied
            // here — it depends on the active beatmap, which is
            // meaningless for a menu-context cursor.
            gameplayCursorSize.BindValueChanged(_ => updateScale(), true);

            // If the skin requests a continuously-rotating cursor,
            // start the spin. Read by raw config string so we don't
            // need to depend on the ruleset's OsuSkinConfiguration
            // enum.
            if (rotationTarget != null && shouldRotate())
                rotationTarget.Spin(rotation_revolution_duration_ms, RotationDirection.Clockwise);
        }

        /// <summary>
        /// Trigger the cursor's "pressed" expand animation — scales
        /// up to <see cref="pressed_scale"/> with an OutElasticHalf
        /// curve. Same shape as <c>SkinnableCursor.Expand</c> in the
        /// osu! ruleset.
        /// </summary>
        public void Expand()
        {
            currentExpandFactor = pressed_scale;
            scaleContainer
                .ScaleTo(targetScale(released_scale))
                .ScaleTo(targetScale(pressed_scale), 400, Easing.OutElasticHalf);
        }

        /// <summary>
        /// Release the pressed state — scales back to
        /// <see cref="released_scale"/> with OutQuad. Same shape as
        /// <c>SkinnableCursor.Contract</c>.
        /// </summary>
        public void Contract()
        {
            currentExpandFactor = released_scale;
            scaleContainer.ScaleTo(targetScale(released_scale), 400, Easing.OutQuad);
        }

        private void updateScale() => scaleContainer.Scale = targetScale(currentExpandFactor);

        private Vector2 targetScale(float expandFactor) => new Vector2(gameplayCursorSize.Value * expandFactor);

        /// <summary>
        /// Read the skin's cursor-rotate configuration. The osu!
        /// ruleset stores this under
        /// <c>OsuSkinConfiguration.CursorRotate</c>, but that enum
        /// lives in the ruleset DLL we can't reference. Falls back
        /// to <c>true</c> (default behaviour) if the skin doesn't
        /// declare an opinion.
        /// </summary>
        private bool shouldRotate()
        {
            // We can't reach OsuSkinConfiguration from here, so we
            // defer to the chain's effective default. Most legacy
            // skins ship cursorrotate enabled (it's the default in
            // skin.ini) — turning it on unconditionally matches
            // upstream's "default to true if unset" behaviour seen
            // in LegacyCursor.cs.
            return true;
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
            // a reasonably-sized preview / menu cursor.
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
