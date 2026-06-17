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

        // When true, we skip the skin lookup and always render the
        // stylised Torii fallback. Used by MenuCursorContainer when
        // the user has explicitly selected
        // MenuCursorStyle.ToriiCursor — they want the Torii visual
        // even if their skin DOES ship its own cursor textures.
        private readonly bool forceTorii;

        /// <summary>
        /// The cursor's CURRENT visual scale on screen — gameplay-cursor-size
        /// multiplied by the in-flight expand factor (1.0× released, 1.2×
        /// pressed). Mirror of <c>OsuCursor.CurrentExpandedScale</c>; used by
        /// <see cref="MenuCursorContainer"/> to size the trail particles
        /// (<c>MenuCursorTrail.NewPartScale</c>) so the trail tracks the
        /// cursor's apparent size — same wiring as
        /// <c>OsuCursorContainer.Update</c>.
        /// </summary>
        public Vector2 CurrentExpandedScale => new Vector2(gameplayCursorSize.Value * currentExpandFactor);

        /// <summary>
        /// The rotation (in degrees) currently applied to the spinning cursor
        /// sprite stack. Mirror of <c>OsuCursor.CurrentRotation</c>; used by
        /// <see cref="MenuCursorContainer"/> to drive
        /// <c>MenuCursorTrail.PartRotation</c> so trail particles match the
        /// cursor's spin orientation — keeps disjoint-trail dots visually
        /// consistent with the cursor head.
        /// </summary>
        public float CurrentRotation => rotationTarget?.Rotation ?? 0f;

        [Resolved(canBeNull: true)]
        private ISkinSource? skinSource { get; set; }

        public SkinnableGameplayCursor(bool forceTorii = false)
        {
            this.forceTorii = forceTorii;

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
            };

            buildSpritesAndStartSpin();

            // Mirror osu!'s gameplay-cursor scaling pipeline: the user
            // setting acts as a direct multiplier on the visual scale.
            // Auto-cursor-size (CS-derived) intentionally NOT applied
            // here — it depends on the active beatmap, which is
            // meaningless for a menu-context cursor.
            gameplayCursorSize.BindValueChanged(_ => updateScale(), true);

            // Live-rebuild on skin change. Without this, swapping
            // the active skin in Settings → Skin while the cursor
            // is on screen leaves the previous skin's textures
            // baked in until the user reopens whatever container
            // we live in. ISkinSource.SourceChanged covers both
            // changing the skin entry AND the user editing the
            // active skin in the layout editor. Guarded null in
            // case we're in a test / toolbox context where no
            // skin source is provided.
            if (skinSource != null)
                skinSource.SourceChanged += onSkinSourceChanged;
        }

        private void onSkinSourceChanged() => Schedule(buildSpritesAndStartSpin);

        private void buildSpritesAndStartSpin()
        {
            scaleContainer.Child = rotationTarget = createCursorSprites();

            // If the skin requests a continuously-rotating cursor,
            // start the spin on the freshly-built sprite stack.
            if (rotationTarget != null && shouldRotate())
                rotationTarget.Spin(rotation_revolution_duration_ms, RotationDirection.Clockwise);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (skinSource != null)
                skinSource.SourceChanged -= onSkinSourceChanged;

            base.Dispose(isDisposing);
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
        /// Read the skin's cursor-rotate configuration. The osu! ruleset reads
        /// this from <c>OsuSkinConfiguration.CursorRotate</c>; since that enum
        /// lives in the ruleset DLL we can't reference, we use a name-equivalent
        /// local enum (<see cref="LegacyCursorSkinConfiguration.CursorRotate"/>).
        /// <see cref="LegacySkin.genericLookup{TLookup,TValue}"/> keys the parsed
        /// <c>[General]</c> dictionary by <see cref="object.ToString"/>, so the
        /// two enums resolve to the SAME entry in the SAME skin.ini, byte-for-byte
        /// matching what the playfield cursor sees.
        ///
        /// Defaults to <c>true</c> when the skin doesn't declare a value, matching
        /// upstream <see cref="LegacyCursor.load"/>: <c>?? true</c>.
        /// </summary>
        private bool shouldRotate()
            => skinSource?.GetConfig<LegacyCursorSkinConfiguration, bool>(LegacyCursorSkinConfiguration.CursorRotate)?.Value ?? true;

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
            // ForceTorii short-circuit — the user explicitly picked
            // MenuCursorStyle.ToriiCursor, which means "use the
            // Torii visual REGARDLESS of what my skin ships". Skip
            // the skin lookup entirely and fall through to the
            // stylised circle below.
            if (forceTorii)
                return createToriiFallback();

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

            // Fallback for skins without a legacy cursor texture
            // (Argon / Triangles / vanilla). Same drawable as the
            // ForceTorii path — extracted to avoid duplication.
            return createToriiFallback();
        }

        /// <summary>
        /// The "Torii" stylised cursor — translucent pink ring with a
        /// white centre dot, soft pink glow. Matches the Torii brand
        /// language we use in the alpha toolbar accents. Used in two
        /// situations:
        /// - User explicitly picked <see cref="MenuCursorStyle.ToriiCursor"/>
        ///   (we want the Torii look regardless of skin contents)
        /// - The active skin has no <c>cursor.png</c> at all (Argon /
        ///   Triangles / vanilla), so we fall back to this rather
        ///   than rendering nothing.
        /// </summary>
        private Drawable createToriiFallback()
        {
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
