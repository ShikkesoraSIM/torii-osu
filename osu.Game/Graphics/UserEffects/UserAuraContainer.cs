// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Online.API.Requests.Responses;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects
{
    /// <summary>
    /// Wraps a username drawable and renders the user's aura (a particle effect
    /// derived from their groups / equipped cosmetic) BEHIND it.
    ///
    /// Auto-sizes to its child so the particle field tracks the username's
    /// bounding box exactly — useful because usernames change width with text.
    ///
    /// If the user has no aura, this renders just the wrapped child with zero
    /// emission overhead (the emitter is swapped out when the user changes,
    /// not just hidden).
    ///
    /// Respects the <see cref="OsuSetting.UserAuraEnabled"/> setting so users
    /// on weaker hardware can disable the global effect.
    /// </summary>
    public partial class UserAuraContainer : Container
    {
        private APIUser? user;
        private readonly Drawable target;

        private ParticleAuraEmitter? emitter;

        // Optional pulsing text-shape glow rendered UNDER the emitter and the
        // target. Only created when the resolved preset opts in via
        // AuraPreset.GlowColour AND the target is a SpriteText we can mirror.
        private TextShapeGlow? textGlow;

        private Bindable<bool> auraEnabled = null!;
        private bool loaded;

        /// <summary>
        /// Wrap <paramref name="target"/> with an aura matching <paramref name="user"/>'s groups.
        /// Use the static <see cref="Wrap"/> helper from call-sites for the cleanest one-liner.
        /// </summary>
        /// <param name="user">The user whose aura (if any) should render behind the target.</param>
        /// <param name="target">The drawable (usually a username SpriteText) to decorate.</param>
        /// <param name="relativeSizeAxes">
        /// Axes the wrapper should size relative to its own parent. Defaults
        /// to <see cref="Axes.None"/> (wrapper auto-sizes both axes to the
        /// target's natural size — correct for free-flowing usernames). Pass
        /// <see cref="Axes.X"/> when wrapping a <c>TruncatingSpriteText</c>
        /// that needs a fixed parent width to know where to truncate; the
        /// wrapper then matches the target's RelativeSizeAxes so the
        /// truncation column stays correct.
        /// </param>
        public UserAuraContainer(APIUser? user, Drawable target, Axes relativeSizeAxes = Axes.None)
        {
            this.user = user;
            this.target = target;

            // Size policy:
            //   - Wrapper takes the target's relative-size axes (so a
            //     RelativeSizeAxes=X target keeps its width relative to the
            //     real parent transitively).
            //   - Auto-size on whichever axes are NOT relative-sized so the
            //     emitter still has a meaningful (non-zero, non-100%) bounds
            //     to spawn particles within on those axes.
            if (relativeSizeAxes != Axes.None)
            {
                RelativeSizeAxes = relativeSizeAxes;
                AutoSizeAxes = Axes.Both & ~relativeSizeAxes;
            }
            else
            {
                AutoSizeAxes = Axes.Both;
            }
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager? config)
        {
            auraEnabled = config?.GetBindable<bool>(OsuSetting.UserAuraEnabled) ?? new Bindable<bool>(true);
            rebuildEmitter();
            // Target is added LAST so the emitter (added inside rebuildEmitter)
            // renders behind it — additive blending then reads as a soft glow
            // around the name without obscuring legibility.
            Add(target);
            loaded = true;
        }

        /// <summary>
        /// Swap the user this container renders an aura for. Safe to call before
        /// or after BDL has run — the emitter is rebuilt accordingly. Useful
        /// when a single header drawable is reused as the active profile changes
        /// (e.g. profile overlay switching between users).
        /// </summary>
        public void SetUser(APIUser? newUser)
        {
            if (ReferenceEquals(newUser, user))
                return;

            user = newUser;
            if (loaded)
                rebuildEmitter();
        }

        private void rebuildEmitter()
        {
            // Tear down both layers so SetUser swaps don't leak old visuals.
            if (emitter != null)
            {
                Remove(emitter, disposeImmediately: true);
                emitter = null;
            }

            if (textGlow != null)
            {
                Remove(textGlow, disposeImmediately: true);
                textGlow = null;
            }

            var preset = AuraRegistry.ResolveForUser(user);
            if (preset == null)
                return;

            // Glow is added FIRST so it sits at the bottom of the z-stack,
            // beneath the emitter (and beneath the username target which we
            // re-front below). Only attaches when (a) preset opts in via
            // GlowColour, and (b) target is a SpriteText we can mirror —
            // otherwise there's nothing to base the text-shape blur on.
            //
            // The TextShapeGlow auto-sizes to its mirror text plus
            // GlowPadding on every side (so the gaussian blur has room to
            // fade out without clipping at the buffer edge). To keep the
            // mirror text aligned EXACTLY on top of the wrapped target,
            // shift the whole glow by -GlowPadding: the inward Padding
            // inside TextShapeGlow pushes its SpriteText right/down by
            // +GlowPadding, and this position offset cancels that shift
            // back to (0, 0) of the wrapper — same pixel origin as the
            // wrapped target. BypassAutoSizeAxes keeps the glow's larger
            // bounds from growing the wrapper itself.
            if (preset.GlowColour is Color4 glowColour && target is SpriteText spriteText)
            {
                Add(textGlow = new TextShapeGlow(spriteText.Text, spriteText.Font, glowColour)
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Position = new Vector2(-TextShapeGlow.GlowPadding),
                    BypassAutoSizeAxes = Axes.Both,
                });
            }

            // Emitter is anchored TopLeft with NO RelativeSizeAxes — its
            // size gets set explicitly each frame in Update() to match the
            // glow's mirror text bounds (which auto-size to the rendered
            // text shape, regardless of whether the wrapper has been
            // stretched wide by RelativeSizeAxes propagation from a
            // TruncatingSpriteText). Without this binding, the emitter
            // span = wrapper width, and for the slanted song-select
            // leaderboard or in-game gameplay HUD (both use
            // TruncatingSpriteText with RelativeSizeAxes=X), the wrapper
            // is much wider than the visible text — particles spawned at
            // 5%-95% of "wrapper width" would land in empty space well
            // past the username's last letter. Binding to the mirror's
            // DrawSize keeps the spawn area bounded to the actual rendered
            // glyph extent.
            Add(emitter = new ParticleAuraEmitter(preset)
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
            });

            // After re-adding the emitter, push the existing target back to the
            // front so it draws on top of the new emitter. Without this the
            // emitter (added last) would obscure the username after a SetUser swap.
            if (loaded && target.Parent == this)
                ChangeChildDepth(target, -1);

            applyEnabledState();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            auraEnabled.BindValueChanged(_ => applyEnabledState(), true);
        }

        protected override void Update()
        {
            base.Update();

            // Sync the emitter's bounds to the actual rendered text bounds
            // each frame. The glow's Mirror SpriteText auto-sizes to the
            // username text shape regardless of the wrapper's
            // RelativeSizeAxes (since the mirror has no RelativeSizeAxes),
            // so its DrawSize is the natural glyph extent — exactly what
            // we want as the emitter's spawn area. Without this binding
            // the emitter would span the (potentially much wider) wrapper
            // bounds and particles would visibly spawn past the visible
            // text in TruncatingSpriteText cases.
            //
            // Falls back to the wrapper's DrawSize when there is no glow
            // (preset has GlowColour=null, or target isn't a SpriteText).
            // In those cases there's no truncation issue to worry about
            // because if there were a TruncatingSpriteText we'd have a
            // glow to read from too.
            if (emitter == null) return;

            Vector2 spawnSize = textGlow?.Mirror.DrawSize ?? DrawSize;
            if (spawnSize.X <= 0 || spawnSize.Y <= 0) return;

            // Tolerate sub-pixel jitter so we don't thrash Size every
            // frame (which would invalidate emitter children layout).
            if (System.Math.Abs(emitter.DrawWidth - spawnSize.X) > 0.5f
                || System.Math.Abs(emitter.DrawHeight - spawnSize.Y) > 0.5f)
            {
                emitter.Size = spawnSize;
            }
        }

        private void applyEnabledState()
        {
            if (emitter != null)
                emitter.Alpha = auraEnabled.Value ? 1 : 0;
        }

        /// <summary>
        /// Convenience helper: returns the original drawable when the user has
        /// no aura (so we don't pay any wrapping cost), otherwise returns a new
        /// <see cref="UserAuraContainer"/>. Anchor/origin of the original
        /// drawable are MOVED to the wrapper (and reset to TopLeft on the
        /// target) so:
        ///   - external layout sees the wrapper at the same effective position
        ///     as the bare drawable would have been.
        ///   - the target lays out naturally as a top-left child inside the
        ///     auto-sized wrapper, which is the only configuration that plays
        ///     nicely with <c>AutoSizeAxes = Axes.Both</c>. Leaving the target
        ///     at e.g. <c>Anchor.CentreLeft</c> caused the wrapper's auto-size
        ///     calculation to misbehave (and in some panels, throw outright).
        /// </summary>
        public static Drawable Wrap(APIUser? user, Drawable target)
        {
            if (AuraRegistry.ResolveForUser(user) == null)
                return target;

            var anchor = target.Anchor;
            var origin = target.Origin;
            // Pull the target's RelativeSizeAxes onto the wrapper so we
            // preserve any X-relative / Y-relative sizing the layout above
            // expected. Without this, wrapping a TruncatingSpriteText (which
            // is RelativeSizeAxes=X to know how wide to truncate) would
            // collapse to zero width because the wrapper auto-sizes to a
            // child that's trying to be 100% of the wrapper.
            var relativeSizeAxes = target.RelativeSizeAxes;

            // Pull Shear too. The slanted song-select leaderboard row sits
            // inside a sheared container and its username SpriteText carries
            // a counter-shear (-OsuGame.SHEAR) to render upright. If we
            // don't preserve that on the wrapper, the wrapper itself stays
            // sheared by the parent and the glow / emitter children inside
            // it render sheared — while the target text (still carrying its
            // counter-shear) renders upright. Result: visible misalignment
            // in the slanted leaderboard. Move the shear up to the wrapper
            // and reset it on the target so the wrapper renders upright AND
            // every child inside it (target, glow, emitter) shares the same
            // unsheared coordinate system.
            var shear = target.Shear;

            // Reset anchor/origin/shear on the inner target so AutoSize on
            // the wrapper sees a top-left-anchored, zero-shear child it can
            // size to predictably. RelativeSizeAxes stays on the target so
            // it can continue to fill the wrapper transitively.
            target.Anchor = Anchor.TopLeft;
            target.Origin = Anchor.TopLeft;
            target.Shear = Vector2.Zero;

            return new UserAuraContainer(user, target, relativeSizeAxes)
            {
                Anchor = anchor,
                Origin = origin,
                Shear = shear,
            };
        }
    }
}
